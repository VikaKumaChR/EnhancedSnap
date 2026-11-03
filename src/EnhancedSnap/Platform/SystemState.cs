using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Engine.Native;
using Microsoft.Win32;
using Windows.UI;
using Windows.UI.ViewManagement;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Platform;

/// <summary>
/// Everything about the system that changes how zones are rendered: theme, accent, transparency,
/// animation preference, high contrast, remote sessions and Energy saver. All updates are event driven
/// (WM_SETTINGCHANGE / WM_POWERBROADCAST); nothing here polls.
/// </summary>
internal sealed unsafe class SystemState
{
    /// <summary>GUID_ENERGY_SAVER_STATUS (Windows 11 24H2+): 0 off, 1 standard, 2 high savings.</summary>
    public static readonly Guid EnergySaverStatusGuid = new("550E8400-E29B-41D4-A716-446655440000");

    /// <summary>GUID_POWER_SAVING_STATUS (legacy battery saver, still raised on older Windows 11 builds).</summary>
    public static readonly Guid PowerSavingStatusGuid = new("E00958C0-C213-4ACE-AC77-FECCED2EEEA5");

    private UISettings? _uiSettings;

    public bool DarkMode { get; private set; }

    /// <summary>App mode. Windows' own snap flyout and Edge's frame follow it, so all snapping UI does too.</summary>
    public bool AppDarkMode { get; private set; }

    public bool TransparencyEnabled { get; private set; } = true;

    public bool AnimationsEnabled { get; private set; } = true;

    public bool HighContrast { get; private set; }

    public bool RemoteSession { get; private set; }

    public int EnergySaverLevel { get; private set; }

    public bool BatterySaver { get; private set; }

    public bool EnergySaverActive => EnergySaverLevel > 0 || BatterySaver;

    public Color Accent { get; private set; } = Color.FromArgb(255, 0, 120, 212);

    public Color AccentLight2 { get; private set; } = Color.FromArgb(255, 153, 235, 255);

    public Color AccentDark1 { get; private set; } = Color.FromArgb(255, 0, 90, 158);

    /// <summary>Signature used to detect whether a refresh actually changed anything.</summary>
    public string Fingerprint =>
        $"{DarkMode}|{AppDarkMode}|{TransparencyEnabled}|{AnimationsEnabled}|{HighContrast}|{RemoteSession}|{EnergySaverActive}|{Accent}|{AccentLight2}|{AccentDark1}";

    /// <summary>Re-reads theme, accent and accessibility settings. Returns true when something changed.</summary>
    public bool Refresh()
    {
        string before = Fingerprint;
        try
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            DarkMode = personalize?.GetValue("SystemUsesLightTheme") is int light && light == 0;
            AppDarkMode = personalize?.GetValue("AppsUseLightTheme") is int appLight && appLight == 0;
            TransparencyEnabled = personalize?.GetValue("EnableTransparency") is not int transparency || transparency != 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"Theme registry read failed: {ex.Message}");
        }

        int animations = 1;
        AnimationsEnabled = !SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, &animations, 0) || animations != 0;

        var hc = new HIGHCONTRASTW { cbSize = (uint)sizeof(HIGHCONTRASTW) };
        HighContrast = SystemParametersInfo(SPI_GETHIGHCONTRAST, hc.cbSize, &hc, 0) && (hc.dwFlags & HCF_HIGHCONTRASTON) != 0;

        RemoteSession = GetSystemMetrics(SM_REMOTESESSION) != 0;

        try
        {
            _uiSettings ??= new UISettings();
            Accent = _uiSettings.GetColorValue(UIColorType.Accent);
            AccentLight2 = _uiSettings.GetColorValue(UIColorType.AccentLight2);
            AccentDark1 = _uiSettings.GetColorValue(UIColorType.AccentDark1);
        }
        catch (Exception ex)
        {
            Log.Warn($"Accent colors unavailable: {ex.Message}");
        }

        return before != Fingerprint;
    }

    /// <summary>Handles PBT_POWERSETTINGCHANGE payloads. Returns true when the effective state changed.</summary>
    public bool OnPowerSetting(Guid setting, uint value)
    {
        bool before = EnergySaverActive;
        if (setting == EnergySaverStatusGuid)
        {
            EnergySaverLevel = (int)value;
        }
        else if (setting == PowerSavingStatusGuid)
        {
            BatterySaver = value != 0;
        }
        else
        {
            return false;
        }

        Log.Info($"Power: energy saver level {EnergySaverLevel}, battery saver {BatterySaver}");
        return before != EnergySaverActive;
    }

    public static Color FromColorRef(uint colorRef, byte alpha = 255) =>
        Color.FromArgb(alpha, (byte)(colorRef & 0xFF), (byte)((colorRef >> 8) & 0xFF), (byte)((colorRef >> 16) & 0xFF));
}
