<#
.SYNOPSIS
  Packs a published Enhanced Snap build into an MSIX for the Microsoft Store (called by build.ps1).

.DESCRIPTION
  Package layout = the publish folder
                 + Images\            the logos (packaging/Images, made by tools/make_icons.py)
                 + AppxManifest.xml   packaging/AppxManifest.xml with the identity from packaging/store-identity.json
                                      and the Windows App SDK classes the settings app activates
                 + resources.pri      the logos' scale / target-size / theme variants, merged with the settings
                                      app's own index (a packaged WinUI app reads its XAML from resources.pri)
  The package is not signed: Partner Center signs Store packages. Needs the Windows SDK (makeappx, makepri).

.EXAMPLE
  ./packaging/build-msix.ps1 -PublishDir artifacts/publish/win-x64 -Version 1.0.0 -Runtime win-x64 -OutDir artifacts/store
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$PublishDir,
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [ValidateSet('win-x64', 'win-arm64')] [string]$Runtime,
    [Parameter(Mandatory)] [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$packaging = $PSScriptRoot
$root = Split-Path $packaging -Parent
$arch = $Runtime.Substring(4)

# Windows SDK tools: the newest SDK that has both.
$sdkBin = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory -Filter '10.*' -ErrorAction SilentlyContinue |
    Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64' } |
    Where-Object { (Test-Path "$_\makeappx.exe") -and (Test-Path "$_\makepri.exe") } |
    Select-Object -First 1
if (-not $sdkBin) {
    throw 'makeappx.exe / makepri.exe not found. Install the Windows 11 SDK (Visual Studio Installer > Individual components).'
}

# Store packages use four-part versions whose last part is 0.
$parts = @($Version.Split('-')[0].Split('.'))
while ($parts.Count -lt 4) { $parts += '0' }
$packageVersion = ($parts[0..2] + '0') -join '.'

$identity = Get-Content "$packaging\store-identity.json" -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($field in 'Name', 'Publisher', 'PublisherDisplayName') {
    if (-not $identity.$field) { throw "store-identity.json: '$field' is empty" }
}

$layout = Join-Path $OutDir "layout-$Runtime"
Remove-Item -Recurse -Force $layout -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $layout | Out-Null
Copy-Item "$PublishDir\*" $layout -Recurse
Copy-Item "$packaging\Images" $layout -Recurse

# The settings app runs the Windows App SDK self-contained: its classes are registered in the exe's side-by-side
# manifest (registration-free WinRT, for unpackaged runs). Register the same classes in the package too, for every
# runtime DLL the payload kept (the AI and Widgets DLLs are trimmed, see EnhancedSnap.Settings.csproj).
$sxsManifest = Get-ChildItem "$root\src\EnhancedSnap.Settings\obj" -Recurse -Filter WindowsAppSDK.manifest -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like "*\Release\*\$Runtime\*" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $sxsManifest) { throw "WindowsAppSDK.manifest not found under src\EnhancedSnap.Settings\obj: publish the settings app first." }
$sxs = [xml](Get-Content $sxsManifest.FullName -Raw)
$ns = New-Object Xml.XmlNamespaceManager $sxs.NameTable
$ns.AddNamespace('asmv3', 'urn:schemas-microsoft-com:asm.v3')
$ns.AddNamespace('winrt', 'urn:schemas-microsoft-com:winrt.v1')
$classes = New-Object Text.StringBuilder
$registered = 0
foreach ($file in $sxs.SelectNodes('//asmv3:file', $ns)) {
    $activatable = $file.SelectNodes('winrt:activatableClass', $ns)
    if ($activatable.Count -eq 0 -or -not (Test-Path (Join-Path $layout $file.name))) { continue }
    [void]$classes.AppendLine('    <Extension Category="windows.activatableClass.inProcessServer">')
    [void]$classes.AppendLine('      <InProcessServer>')
    [void]$classes.AppendLine("        <Path>$($file.name)</Path>")
    foreach ($class in $activatable) {
        $threading = switch ($class.threadingModel) { 'sta' { 'STA' } 'mta' { 'MTA' } default { 'both' } }
        [void]$classes.AppendLine("        <ActivatableClass ActivatableClassId=`"$($class.name)`" ThreadingModel=`"$threading`" />")
        $registered++
    }
    [void]$classes.AppendLine('      </InProcessServer>')
    [void]$classes.AppendLine('    </Extension>')
}
if ($registered -eq 0) { throw 'No Windows App SDK classes found to register.' }

$tokens = [ordered]@{
    '$IdentityName$'         = [Security.SecurityElement]::Escape($identity.Name)
    '$Publisher$'            = [Security.SecurityElement]::Escape($identity.Publisher)
    '$PublisherDisplayName$' = [Security.SecurityElement]::Escape($identity.PublisherDisplayName)
    '$Version$'              = $packageVersion
    '$Architecture$'         = $arch
    '$WindowsAppSdkClasses$' = $classes.ToString().TrimEnd()
}
$manifest = Get-Content "$packaging\AppxManifest.xml" -Raw -Encoding UTF8
foreach ($token in $tokens.GetEnumerator()) { $manifest = $manifest.Replace($token.Key, $token.Value) }
[IO.File]::WriteAllText("$layout\AppxManifest.xml", $manifest, (New-Object Text.UTF8Encoding $false))

# makepri names resources relative to where indexing starts and would index every file of the layout, so it indexes
# a staging folder with just the logos (as Images\...) and the settings app's index; candidate paths stay valid in
# the layout.
$priRoot = Join-Path $OutDir "pri-$Runtime"
Remove-Item -Recurse -Force $priRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $priRoot | Out-Null
Copy-Item "$packaging\Images" $priRoot -Recurse
Copy-Item "$layout\EnhancedSnap.Settings.pri" $priRoot

$log = Join-Path $OutDir "msix-$Runtime.log"
& "$sdkBin\makepri.exe" new /pr $priRoot /cf "$packaging\priconfig.xml" /mn "$layout\AppxManifest.xml" /of "$layout\resources.pri" /o *> $log
if ($LASTEXITCODE) { throw "makepri failed (exit code $LASTEXITCODE); see $log" }
Remove-Item -Recurse -Force $priRoot

$msix = Join-Path $OutDir "EnhancedSnap-$Version-$arch.msix"
& "$sdkBin\makeappx.exe" pack /d $layout /p $msix /o *>> $log
if ($LASTEXITCODE) { throw "makeappx failed (exit code $LASTEXITCODE); see $log" }

Write-Host ("  {0}: {1} {2}, {3} Windows App SDK classes, identity {4}" -f (Split-Path $msix -Leaf), $identity.Name, $packageVersion, $registered, $identity.Publisher)
