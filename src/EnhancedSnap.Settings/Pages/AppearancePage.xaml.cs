using System.ComponentModel;
using EnhancedSnap.Core.Displays;
using EnhancedSnap.Core.Layouts;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Core.Settings;
using EnhancedSnap.Settings.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System.Power;
using Windows.UI.ViewManagement;

namespace EnhancedSnap.Settings.Pages;

public sealed partial class AppearancePage : Page
{
    private readonly UISettings _uiSettings = new();

    public AppearancePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public Strings S { get; } = Strings.Current;

    public SettingsViewModel Vm => App.ViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Vm.PropertyChanged += OnSettingsChanged;
        PowerManager.EnergySaverStatusChanged += OnPowerChanged;
        _uiSettings.AdvancedEffectsEnabledChanged += OnEffectsChanged;
        RefreshNotices();
        RefreshPreview();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Vm.PropertyChanged -= OnSettingsChanged;
        PowerManager.EnergySaverStatusChanged -= OnPowerChanged;
        _uiSettings.AdvancedEffectsEnabledChanged -= OnEffectsChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => RefreshPreview();

    // Both events arrive on a worker thread.
    private void OnPowerChanged(object? sender, object e) => DispatcherQueue.TryEnqueue(RefreshNotices);

    private void OnEffectsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(RefreshNotices);

    /// <summary>Explain why zones are solid right now (the engine applies the same rules).</summary>
    private void RefreshNotices()
    {
        bool energySaver;
        try
        {
            energySaver = PowerManager.EnergySaverStatus == EnergySaverStatus.On;
        }
        catch
        {
            energySaver = false;
        }

        EnergyBar.IsOpen = energySaver;
        TransparencyBar.IsOpen = !energySaver && !_uiSettings.AdvancedEffectsEnabled;
    }

    private void RefreshPreview()
    {
        var settings = Vm.Current;
        var displays = DisplayEnumerator.GetDisplays();
        var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault();
        if (primary is not null)
        {
            Preview.AspectRatio = primary.Bounds.Width / (double)Math.Max(1, primary.Bounds.Height);
        }

        Preview.Layout = primary is null
            ? LayoutCatalog.FindBuiltIn(LayoutCatalog.Halves)
            : LayoutCatalog.ResolveForMonitor(settings, primary.Id, primary.WorkArea.Width, primary.WorkArea.Height);
        Preview.GapEpx = settings.Gap;
        Preview.MarginEpx = settings.Margin;
        Preview.CornerRadiusEpx = settings.Corners switch
        {
            CornerStyle.RoundSmall => 4,
            _ => 8,
        };
        Preview.Refresh();
    }
}
