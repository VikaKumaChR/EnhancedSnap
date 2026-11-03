using System.ComponentModel;
using EnhancedSnap.Core;
using EnhancedSnap.Core.Displays;
using EnhancedSnap.Core.Layouts;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Core.Platform;
using EnhancedSnap.Settings.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace EnhancedSnap.Settings.Pages;

public sealed partial class GeneralPage : Page
{
    public GeneralPage()
    {
        InitializeComponent();
        WelcomeBar.IsOpen = App.ShowWelcome;
        AdminCard.Visibility = AppInfo.IsPackaged ? Visibility.Collapsed : Visibility.Visible; // a scheduled task outside the package
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public Strings S { get; } = Strings.Current;

    public SettingsViewModel Vm => App.ViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Vm.PropertyChanged += OnSettingsChanged;
        if (App.Window is { } window)
        {
            window.Activated += OnWindowActivated;
        }

        RefreshStatus();
        RefreshHero();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Vm.PropertyChanged -= OnSettingsChanged;
        if (App.Window is { } window)
        {
            window.Activated -= OnWindowActivated;
        }
    }

    // Engine state is re-checked when the window regains focus — no polling while it sits in the background.
    private void OnWindowActivated(object sender, WindowActivatedEventArgs args) => RefreshStatus();

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Vm.LaunchAtStartup))
        {
            // The Store package's startup task stays off once the user turned it off in Windows Settings: say where
            // to turn it on, and put the switch back.
            StartupBlockedBar.IsOpen = !Vm.LaunchAtStartup && StartupRegistration.IsBlocked();
            DispatcherQueue.TryEnqueue(() => StartupSwitch.IsOn = Vm.LaunchAtStartup);
        }

        RefreshStatus();
        RefreshHero();
    }

    private async void OpenStartupApps_Click(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:startupapps"));

    private void RefreshStatus()
    {
        bool running = EngineClient.IsRunning;
        bool active = running && Vm.Enabled;
        StatusTitle.Text = !running ? S.Status_NotRunning : Vm.Enabled ? S.Status_Running : S.Status_Paused;
        StatusBody.Text = running ? Vm.ActivationHint : S.Status_NotRunningDesc;
        StatusIcon.Glyph = active ? "" : "";
        StatusIcon.Foreground = (Brush)Application.Current.Resources[active ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush"];
        StartButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        PreviewButton.IsEnabled = active;
    }

    private void RefreshHero()
    {
        var displays = DisplayEnumerator.GetDisplays();
        var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault();
        if (primary is null)
        {
            return;
        }

        var settings = Vm.Current;
        HeroPreview.AspectRatio = primary.Bounds.Width / (double)Math.Max(1, primary.Bounds.Height);
        HeroPreview.Layout = LayoutCatalog.ResolveForMonitor(settings, primary.Id, primary.WorkArea.Width, primary.WorkArea.Height);
        HeroPreview.GapEpx = settings.Gap;
        HeroPreview.MarginEpx = settings.Margin;
        HeroPreview.CornerRadiusEpx = settings.Corners == Core.Settings.CornerStyle.RoundSmall ? 4 : 8;
        HeroPreview.Refresh();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        if (EngineClient.Start())
        {
            for (int i = 0; i < 20 && !EngineClient.IsRunning; i++)
            {
                await Task.Delay(150);
            }
        }

        StartButton.IsEnabled = true;
        RefreshStatus();
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e) => EngineClient.PreviewZones(0);

    private bool _adminBusy;

    /// <summary>On: one UAC prompt registers the elevated task and restarts the engine. Off: the engine restarts unelevated.</summary>
    private async void AdminSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_adminBusy || AdminSwitch.IsOn == Vm.RunAsAdministrator)
        {
            return;
        }

        _adminBusy = true;
        AdminSwitch.IsEnabled = false;
        AdminBar.IsOpen = false;
        try
        {
            if (AdminSwitch.IsOn)
            {
                if (await EngineClient.EnableElevationAsync())
                {
                    Vm.RunAsAdministrator = true;
                }
                else
                {
                    AdminSwitch.IsOn = false;
                    AdminBar.IsOpen = true;
                }
            }
            else
            {
                Vm.RunAsAdministrator = false;
                EngineClient.DropElevation();
            }
        }
        finally
        {
            AdminSwitch.IsEnabled = true;
            _adminBusy = false;
            RefreshStatus();
        }
    }

    private void WelcomeBar_Closed(InfoBar sender, InfoBarClosedEventArgs args) => Vm.WelcomeDismissed = true;
}
