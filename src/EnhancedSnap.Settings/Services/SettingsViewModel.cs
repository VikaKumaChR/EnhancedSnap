using System.ComponentModel;
using System.Runtime.CompilerServices;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Core.Platform;
using EnhancedSnap.Core.Settings;
using Microsoft.UI.Dispatching;

namespace EnhancedSnap.Settings.Services;

/// <summary>
/// Single source of truth for the settings UI. Every change is written to settings.json (debounced, so a
/// dragged slider doesn't hammer the disk) and the engine is told to reload — snapped windows re-flow live.
/// </summary>
public sealed partial class SettingsViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(180);
    private readonly DispatcherQueueTimer _saveTimer;
    private AppSettings _settings;

    public SettingsViewModel(DispatcherQueue dispatcher)
    {
        _settings = SettingsStore.Load();
        _saveTimer = dispatcher.CreateTimer();
        _saveTimer.IsRepeating = false;
        _saveTimer.Interval = SaveDelay;
        _saveTimer.Tick += (_, _) => SaveNow();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the UI language changed so the shell can rebuild its text.</summary>
    public event Action? LanguageChanged;

    public AppSettings Current => _settings;

    public bool Enabled { get => _settings.Enabled; set => Set(_settings.Enabled, value, v => _settings.Enabled = v); }

    public int ActivationIndex
    {
        get => (int)_settings.Activation;
        set => Set((int)_settings.Activation, value, v => _settings.Activation = (ActivationMode)Math.Clamp(v, 0, 1), nameof(ActivationIndex), nameof(ActivationHint));
    }

    public string ActivationHint => _settings.Activation == ActivationMode.HoldShift ? Strings.Current.Hint_HoldShift : Strings.Current.Hint_Always;

    public bool SpanWithCtrl { get => _settings.SpanWithCtrl; set => Set(_settings.SpanWithCtrl, value, v => _settings.SpanWithCtrl = v); }

    public bool ShowZonesOnAllMonitors { get => _settings.ShowZonesOnAllMonitors; set => Set(_settings.ShowZonesOnAllMonitors, value, v => _settings.ShowZonesOnAllMonitors = v); }

    public bool RestoreSizeOnUnsnap { get => _settings.RestoreSizeOnUnsnap; set => Set(_settings.RestoreSizeOnUnsnap, value, v => _settings.RestoreSizeOnUnsnap = v); }

    public bool KeepWindowsInZones { get => _settings.KeepWindowsInZones; set => Set(_settings.KeepWindowsInZones, value, v => _settings.KeepWindowsInZones = v); }

    public bool OverrideWinArrows { get => _settings.OverrideWinArrows; set => Set(_settings.OverrideWinArrows, value, v => _settings.OverrideWinArrows = v); }

    public bool MoveAcrossMonitors { get => _settings.MoveAcrossMonitors; set => Set(_settings.MoveAcrossMonitors, value, v => _settings.MoveAcrossMonitors = v); }

    public bool ReplaceWindowsSnap { get => _settings.ReplaceWindowsSnap; set => Set(_settings.ReplaceWindowsSnap, value, v => _settings.ReplaceWindowsSnap = v); }

    public bool EdgeSnapping { get => _settings.EdgeSnapping; set => Set(_settings.EdgeSnapping, value, v => _settings.EdgeSnapping = v); }

    public bool LayoutsOnMaximizeHover { get => _settings.LayoutsOnMaximizeHover; set => Set(_settings.LayoutsOnMaximizeHover, value, v => _settings.LayoutsOnMaximizeHover = v); }

    public bool SnapBar { get => _settings.SnapBar; set => Set(_settings.SnapBar, value, v => _settings.SnapBar = v); }

    public bool SnapAssist { get => _settings.SnapAssist; set => Set(_settings.SnapAssist, value, v => _settings.SnapAssist = v); }

    public bool GroupFrame { get => _settings.GroupFrame; set => Set(_settings.GroupFrame, value, v => _settings.GroupFrame = v); }

    public int FrameMaterialIndex
    {
        get => (int)_settings.GroupFrameMaterial;
        set => Set((int)_settings.GroupFrameMaterial, value, v => _settings.GroupFrameMaterial = (FrameMaterial)Math.Clamp(v, 0, 3));
    }

    public bool GroupsInTaskbar { get => _settings.GroupsInTaskbar; set => Set(_settings.GroupsInTaskbar, value, v => _settings.GroupsInTaskbar = v); }

    public bool RunAsAdministrator { get => _settings.RunAsAdministrator; set => Set(_settings.RunAsAdministrator, value, v => _settings.RunAsAdministrator = v); }

    public double Gap
    {
        get => _settings.Gap;
        set => Set(_settings.Gap, (int)Math.Round(value), v => _settings.Gap = v, nameof(Gap), nameof(GapText));
    }

    public string GapText => Strings.Current.Format(Strings.Current.Unit_Px, _settings.Gap);

    public double Margin
    {
        get => _settings.Margin;
        set => Set(_settings.Margin, (int)Math.Round(value), v => _settings.Margin = v, nameof(Margin), nameof(MarginText));
    }

    public string MarginText => Strings.Current.Format(Strings.Current.Unit_Px, _settings.Margin);

    public int CornersIndex { get => (int)_settings.Corners; set => Set((int)_settings.Corners, value, v => _settings.Corners = (CornerStyle)Math.Clamp(v, 0, 2)); }

    public int MaterialIndex { get => (int)_settings.Material; set => Set((int)_settings.Material, value, v => _settings.Material = (MaterialMode)Math.Clamp(v, 0, 1)); }

    public int MotionIndex { get => (int)_settings.Motion; set => Set((int)_settings.Motion, value, v => _settings.Motion = (MotionMode)Math.Clamp(v, 0, 1)); }

    /// <summary>0 = follow Windows, then <see cref="Strings.Supported"/> order.</summary>
    public int LanguageIndex
    {
        get => string.IsNullOrEmpty(_settings.Language) ? 0 : Math.Max(0, Strings.Supported.ToList().IndexOf(_settings.Language) + 1);
        set
        {
            string language = value <= 0 ? "" : Strings.Supported[Math.Min(value - 1, Strings.Supported.Count - 1)];
            if (language == _settings.Language)
            {
                return;
            }

            _settings.Language = language;
            Strings.Use(language);
            SaveNow();
            LanguageChanged?.Invoke();
        }
    }

    public bool LaunchAtStartup
    {
        get => StartupRegistration.IsEnabled();
        set
        {
            if (value != StartupRegistration.IsEnabled())
            {
                StartupRegistration.SetEnabled(value);
                Raise();
            }
        }
    }

    public bool WelcomeDismissed { get => _settings.WelcomeDismissed; set => Set(_settings.WelcomeDismissed, value, v => _settings.WelcomeDismissed = v); }

    /// <summary>Re-read settings.json (changed by the engine, e.g. tray "Pause").</summary>
    public void Reload()
    {
        _settings = SettingsStore.Load();
        Raise(string.Empty);
    }

    /// <summary>For structural edits (layouts, exclusions): mutate then commit immediately.</summary>
    public void Update(Action<AppSettings> change)
    {
        change(_settings);
        SaveNow();
        Raise(string.Empty);
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        if (SettingsStore.Save(_settings))
        {
            EngineClient.NotifySettingsChanged();
        }
    }

    private void Set<T>(T current, T value, Action<T> apply, [CallerMemberName] string? name = null, string? also = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return;
        }

        apply(value);
        Raise(name);
        if (also is not null)
        {
            Raise(also);
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
