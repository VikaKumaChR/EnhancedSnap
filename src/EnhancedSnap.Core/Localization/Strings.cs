using System.Globalization;
using System.Runtime.CompilerServices;
using EnhancedSnap.Core.Displays;
using EnhancedSnap.Core.Layouts;

namespace EnhancedSnap.Core.Localization;

/// <summary>
/// UI strings for the tray menu and the settings app. A code-based table instead of .resw keeps the
/// NativeAOT/unpackaged setup simple and lets both processes share one source of truth.
/// Each property name is the lookup key; missing translations fall back to English.
/// </summary>
public sealed partial class Strings
{
    public const string English = "en";
    public const string TraditionalChinese = "zh-Hant";
    public const string SimplifiedChinese = "zh-Hans";

    // UPDATE: Additional languages — add a StringTables.<lang>.cs dictionary and list it in Supported.
    public static IReadOnlyList<string> Supported { get; } = [English, TraditionalChinese, SimplifiedChinese];

    private readonly IReadOnlyDictionary<string, string> _table;

    private Strings(string language)
    {
        Language = language;
        _table = language switch
        {
            TraditionalChinese => StringTables.ZhHant,
            SimplifiedChinese => StringTables.ZhHans,
            _ => StringTables.En,
        };
    }

    public static Strings Current { get; private set; } = new(Resolve(""));

    public string Language { get; }

    public CultureInfo Culture => Language switch
    {
        TraditionalChinese => CultureInfo.GetCultureInfo("zh-TW"),
        SimplifiedChinese => CultureInfo.GetCultureInfo("zh-CN"),
        _ => CultureInfo.GetCultureInfo("en-US"),
    };

    /// <summary>Switch language. Empty or null follows the Windows display language.</summary>
    public static Strings Use(string? preference)
    {
        Current = new Strings(Resolve(preference));
        return Current;
    }

    public static string Resolve(string? preference)
    {
        if (!string.IsNullOrWhiteSpace(preference) && Supported.Contains(preference))
        {
            return preference;
        }

        var ui = CultureInfo.CurrentUICulture;
        if (ui.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            string name = ui.Name;
            bool traditional = name.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("TW", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("HK", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("MO", StringComparison.OrdinalIgnoreCase);
            return traditional ? TraditionalChinese : SimplifiedChinese;
        }

        return English;
    }

    public string Format(string pattern, params object?[] args) => string.Format(Culture, pattern, args);

    public string LayoutName(LayoutDefinition layout)
    {
        if (layout.IsCustom)
        {
            return layout.CustomName ?? Layout_Custom;
        }

        return layout.Id switch
        {
            LayoutCatalog.Halves => Layout_Halves,
            LayoutCatalog.Thirds => Layout_Thirds,
            LayoutCatalog.LeftTwoThirds => Layout_LeftTwoThirds,
            LayoutCatalog.RightTwoThirds => Layout_RightTwoThirds,
            LayoutCatalog.CenterFocus => Layout_CenterFocus,
            LayoutCatalog.MainStack => Layout_MainStack,
            LayoutCatalog.HalfStack => Layout_HalfStack,
            LayoutCatalog.Quarters => Layout_Quarters,
            LayoutCatalog.Grid3x2 => Layout_Grid3x2,
            LayoutCatalog.Rows => Layout_Rows,
            LayoutCatalog.Full => Layout_Full,
            _ => layout.Id,
        };
    }

    public string DisplayName(DisplayInfo display)
    {
        if (!string.IsNullOrWhiteSpace(display.FriendlyName))
        {
            return display.FriendlyName;
        }

        return display.IsInternal ? Display_BuiltIn : Format(Display_Generic, display.Number);
    }

    private string Get([CallerMemberName] string key = "") =>
        _table.TryGetValue(key, out var value) ? value
        : StringTables.En.TryGetValue(key, out value) ? value
        : key;

    // ---- Common
    public string AppName => Get();
    public string AppTagline => Get();
    public string Action_Start => Get();
    public string Action_Add => Get();
    public string Action_Remove => Get();
    public string Action_Edit => Get();
    public string Action_Delete => Get();
    public string Action_Save => Get();
    public string Action_Cancel => Get();
    public string Action_PreviewZones => Get();
    public string Unit_Px => Get();
    public string Toggle_On => Get();
    public string Toggle_Off => Get();

    // ---- Navigation
    public string Nav_General => Get();
    public string Nav_Layouts => Get();
    public string Nav_Appearance => Get();
    public string Nav_Keyboard => Get();
    public string Nav_Apps => Get();
    public string Nav_About => Get();

    // ---- Status
    public string Status_Running => Get();
    public string Status_Paused => Get();
    public string Status_NotRunning => Get();
    public string Status_NotRunningDesc => Get();
    public string Hint_HoldShift => Get();
    public string Hint_Always => Get();
    public string Welcome_Title => Get();
    public string Welcome_Body => Get();

    // ---- General
    public string General_Desc => Get();
    public string Section_Behavior => Get();
    public string Section_System => Get();
    public string Setting_Enabled => Get();
    public string Setting_EnabledDesc => Get();
    public string Setting_Activation => Get();
    public string Setting_ActivationDesc => Get();
    public string Activation_HoldShift => Get();
    public string Activation_Always => Get();
    public string Setting_Span => Get();
    public string Setting_SpanDesc => Get();
    public string Setting_AllMonitors => Get();
    public string Setting_AllMonitorsDesc => Get();
    public string Setting_RestoreSize => Get();
    public string Setting_RestoreSizeDesc => Get();
    public string Setting_KeepInZones => Get();
    public string Setting_KeepInZonesDesc => Get();
    public string Setting_Startup => Get();
    public string Setting_StartupDesc => Get();
    public string Startup_Blocked => Get();
    public string Action_OpenStartupApps => Get();
    public string Setting_Language => Get();
    public string Setting_LanguageDesc => Get();
    public string Language_System => Get();

    // ---- Layouts
    public string Layouts_Desc => Get();
    public string Layouts_Display => Get();
    public string Layouts_PreviewHint => Get();
    public string Layouts_Templates => Get();
    public string Layouts_Custom => Get();
    public string Layouts_CustomDesc => Get();
    public string Layouts_CustomEmpty => Get();
    public string Display_BuiltIn => Get();
    public string Display_Generic => Get();
    public string Display_Primary => Get();
    public string Display_Details => Get();
    public string Layout_Halves => Get();
    public string Layout_Thirds => Get();
    public string Layout_LeftTwoThirds => Get();
    public string Layout_RightTwoThirds => Get();
    public string Layout_CenterFocus => Get();
    public string Layout_MainStack => Get();

    public string Layout_HalfStack => Get();
    public string Layout_Quarters => Get();
    public string Layout_Grid3x2 => Get();
    public string Layout_Rows => Get();
    public string Layout_Full => Get();
    public string Layout_Custom => Get();
    public string Layout_ZoneCount => Get();
    public string Action_NewLayout => Get();
    public string Editor_Title => Get();
    public string Editor_Name => Get();
    public string Editor_DefaultName => Get();
    public string Editor_Orientation => Get();
    public string Editor_Columns => Get();
    public string Editor_Rows => Get();
    public string Editor_ColumnN => Get();
    public string Editor_RowN => Get();
    public string Editor_Size => Get();
    public string Editor_Splits => Get();
    public string Editor_AddColumn => Get();
    public string Editor_AddRow => Get();
    public string Confirm_DeleteLayout => Get();
    public string Confirm_DeleteLayoutBody => Get();

    // ---- Appearance
    public string Appearance_Desc => Get();
    public string Section_Spacing => Get();
    public string Section_Effects => Get();
    public string Setting_Gap => Get();
    public string Setting_GapDesc => Get();
    public string Setting_Margin => Get();
    public string Setting_MarginDesc => Get();
    public string Setting_Corners => Get();
    public string Setting_CornersDesc => Get();
    public string Corners_Round => Get();
    public string Corners_Small => Get();
    public string Corners_System => Get();
    public string Setting_Material => Get();
    public string Setting_MaterialDesc => Get();
    public string Material_Auto => Get();
    public string Material_Solid => Get();
    public string Setting_Motion => Get();
    public string Setting_MotionDesc => Get();
    public string Motion_Auto => Get();
    public string Motion_Off => Get();
    public string Energy_Title => Get();
    public string Energy_Body => Get();
    public string Transparency_Off => Get();
    public string Preview_Caption => Get();

    // ---- Keyboard
    public string Keyboard_Desc => Get();
    public string Setting_OverrideKeys => Get();
    public string Setting_OverrideKeysDesc => Get();
    public string Setting_AcrossMonitors => Get();
    public string Setting_AcrossMonitorsDesc => Get();
    public string Shortcuts_Title => Get();
    public string Shortcut_Drag => Get();
    public string Shortcut_Span => Get();
    public string Shortcut_LeftRight => Get();
    public string Shortcut_Up => Get();
    public string Shortcut_Down => Get();
    public string Key_Drag => Get();

    // ---- Apps
    public string Apps_Desc => Get();
    public string Apps_Placeholder => Get();
    public string Apps_Hint => Get();
    public string Apps_Empty => Get();
    public string Apps_AdminTitle => Get();
    public string Apps_AdminBody => Get();
    public string Apps_AdminBodyPackaged => Get();

    // ---- About
    public string About_Version => Get();
    public string About_Desc => Get();
    public string About_OpenData => Get();
    public string About_OpenLogs => Get();
    public string About_Energy => Get();
    public string About_EnergyBody => Get();
    public string About_Roadmap => Get();
    public string About_RoadmapBody => Get();
    public string About_Quit => Get();

    // ---- Snap groups, flyouts, Snap Assist
    public string Groups_Join => Get();
    public string Group_TaskbarTitle => Get();
    public string Assist_Title => Get();
    public string Section_Replace => Get();
    public string Setting_ReplaceSnap => Get();
    public string Setting_ReplaceSnapDesc => Get();
    public string Setting_EdgeSnapping => Get();
    public string Setting_EdgeSnappingDesc => Get();
    public string Setting_LayoutsHover => Get();
    public string Setting_LayoutsHoverDesc => Get();
    public string Setting_SnapBar => Get();
    public string Setting_SnapBarDesc => Get();
    public string Setting_SnapAssist => Get();
    public string Setting_SnapAssistDesc => Get();
    public string Setting_GroupFrame => Get();
    public string Setting_GroupFrameDesc => Get();
    public string Setting_FrameMaterial => Get();
    public string Setting_FrameMaterialDesc => Get();
    public string FrameMaterial_Mica => Get();
    public string FrameMaterial_MicaAlt => Get();
    public string FrameMaterial_Acrylic => Get();
    public string FrameMaterial_Solid => Get();
    public string Setting_GroupsInTaskbar => Get();
    public string Setting_GroupsInTaskbarDesc => Get();
    public string Setting_RunAsAdmin => Get();
    public string Setting_RunAsAdminDesc => Get();
    public string Admin_Failed => Get();
    public string Shortcut_WinZ => Get();
    public string Shortcut_EdgeDrag => Get();
    public string Shortcut_Divider => Get();
    public string Key_Edge => Get();
    public string Key_Divider => Get();
    public string Key_GapFill => Get();
    public string Shortcut_GapFill => Get();
    public string Shortcut_PickGroup => Get();

    // ---- Tray & notifications
    public string Tray_Tooltip => Get();
    public string Tray_TooltipPaused => Get();
    public string Tray_Settings => Get();
    public string Tray_Layouts => Get();
    public string Tray_Pause => Get();
    public string Tray_Quit => Get();
    public string Notify_ElevatedTitle => Get();
    public string Notify_ElevatedBody => Get();
    public string Notify_ElevatedBodyPackaged => Get();
    public string Notify_UnsupportedOs => Get();
}
