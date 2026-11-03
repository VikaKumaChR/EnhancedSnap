<#
.SYNOPSIS
  Builds a release of Enhanced Snap: unit tests → NativeAOT publish (engine + settings) → portable ZIP → MSI
  (GitHub release, artifacts/dist) → MSIX (Microsoft Store upload, artifacts/store).

.EXAMPLE
  ./build.ps1                         # x64 release of the version in Directory.Build.props
  ./build.ps1 -Version 1.0.1
  ./build.ps1 -Runtime win-arm64      # needs the MSVC ARM64 build tools for NativeAOT

.NOTES
  Requirements: .NET 10 SDK, Visual Studio 2022+/2026 with "Desktop development with C++" (NativeAOT linker),
  Windows 11 SDK for the MSIX (makeappx, makepri). NuGet restores the Windows App SDK and WiX v5 on first run.
  The Store package's identity comes from packaging/store-identity.json (see docs/PUBLISHING.md).
#>
[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$SkipTests,
    [switch]$SkipInstaller,
    [switch]$SkipMsix
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $Version) { $Version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version | Select-Object -First 1 }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

# The NativeAOT linker lookup shells out to vswhere.exe, which is not on PATH by default.
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if ((Test-Path $vsInstaller) -and ($env:PATH -notlike "*$vsInstaller*")) { $env:PATH = "$vsInstaller;$env:PATH" }

$platform = if ($Runtime -eq 'win-arm64') { 'ARM64' } else { 'x64' }
$arch = $Runtime.Substring(4)
$publish = Join-Path $root "artifacts\publish\$Runtime"
$dist = Join-Path $root 'artifacts\dist'
$store = Join-Path $root 'artifacts\store'

function Invoke-Step([string]$name, [scriptblock]$action) {
    Write-Host "`n==> $name" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "$name failed (exit code $LASTEXITCODE)" }
}

Remove-Item -Recurse -Force $publish, $dist, $store -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $publish, $dist | Out-Null

if (-not $SkipTests) {
    Invoke-Step 'Unit tests' { dotnet test "$root\tests\EnhancedSnap.Core.Tests\EnhancedSnap.Core.Tests.csproj" -c Release }
}

Invoke-Step 'Publish engine (NativeAOT)' {
    dotnet publish "$root\src\EnhancedSnap\EnhancedSnap.csproj" -c Release -r $Runtime -o $publish -p:Version=$Version
}

Invoke-Step 'Publish settings app (WinUI 3, NativeAOT, self-contained)' {
    dotnet publish "$root\src\EnhancedSnap.Settings\EnhancedSnap.Settings.csproj" -c Release -r $Runtime -p:Platform=$platform -o $publish -p:Version=$Version
}

Get-ChildItem $publish -Recurse -Include *.pdb | Remove-Item -Force
Copy-Item "$root\LICENSE" $publish -ErrorAction SilentlyContinue

$zip = Join-Path $dist "EnhancedSnap-$Version-$arch-portable.zip"
Invoke-Step 'Portable ZIP' {
    Remove-Item $zip -ErrorAction SilentlyContinue
    Compress-Archive -Path "$publish\*" -DestinationPath $zip -CompressionLevel Optimal
}

if (-not $SkipInstaller) {
    Invoke-Step 'MSI installer (WiX v5)' {
        $log = Join-Path $root 'artifacts\installer-build.log'
        # Freshly published files are often still being scanned by antivirus; retry once before failing.
        for ($attempt = 1; $attempt -le 2; $attempt++) {
            dotnet build "$root\installer\EnhancedSnap.Installer.wixproj" -c Release `
                -p:ProductVersion=$Version -p:InstallerPlatform=$arch "-p:PublishDir=$publish\" -o $dist *> $log
            if ($LASTEXITCODE -eq 0) { break }
            Write-Warning "Installer build attempt $attempt failed:"
            Select-String -Path $log -Pattern ' error ' | Select-Object -First 5 | ForEach-Object { Write-Warning $_.Line.Trim() }
            Start-Sleep -Seconds 3
        }
    }
    Get-ChildItem $dist -Filter *.wixpdb | Remove-Item -Force
}

if (-not $SkipMsix) {
    # Unsigned: only the Store can install it (Partner Center signs it), so it stays out of the GitHub assets.
    Invoke-Step 'MSIX package (Microsoft Store)' {
        New-Item -ItemType Directory -Force $store | Out-Null
        & "$root\packaging\build-msix.ps1" -PublishDir $publish -Version $Version -Runtime $Runtime -OutDir $store
    }
}

$hashes = Get-ChildItem $dist -File | Where-Object { $_.Extension -in '.msi', '.zip' } |
    ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
$hashes | Set-Content (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii

Write-Host "`nRelease $Version ($Runtime):" -ForegroundColor Green
Get-ChildItem $dist, $store -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -ne '.log' } |
    ForEach-Object { '  {0,-48} {1,8:N1} MB' -f (Resolve-Path -Relative $_.FullName), ($_.Length / 1MB) }
