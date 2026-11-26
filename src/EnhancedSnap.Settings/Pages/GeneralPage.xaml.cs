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

        // Store version: no administrator mode (its scheduled task would run the engine outside the package). The card
        // stays, says so, and links to the GitHub version instead of a switch.
        AdminSwitch.Visibility = AppInfo.IsPackaged ? Visibility.Collapsed : Visibility.Visible;
        AdminStoreLink.Visibility = AppInfo.IsPackaged ? Visibility.Visible : Visibility.Collapsed;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public Strings S { get; } = Strings.Current;

    public SettingsViewModel Vm => App.ViewModel;

    public string AdminDescription => AppInfo.IsPackaged ? S.Admin_StoreDesc : S.Setting_RunAsAdminDesc;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Vm.PropertyChanged += OnSettingsChanged;
        if (App.Window is { } window)
        {
            window.Activated += OnWindowActivated;
        }

        RefreshStatus();
        RefreshHero();
        RefreshAdminStatus();
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
    // So is sign-in start, which can change in Windows Settings (the info bar's button sends the user there).
    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        RefreshStatus();
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            bool startup = Vm.LaunchAtStartup;
            StartupSwitch.IsOn = startup;
            StartupBlockedBar.IsOpen &= !startup;
            RefreshAdminStatus();
        }
    }

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
            RefreshAdminStatus();
        }
    }

    /// <summary>
    /// Administrator mode is on: is it working? The running engine must be elevated; with no engine running, its
    /// scheduled task must at least exist. Checked off the UI thread (schtasks), on load and when the window comes back.
    /// </summary>
    private async void RefreshAdminStatus()
    {
        if (AppInfo.IsPackaged || !Vm.RunAsAdministrator || _adminBusy)
        {
            AdminRepairBar.IsOpen = false;
            return;
        }

        bool broken = await Task.Run(() =>
        {
            int engine = AdminMode.EngineProcessId();
            return engine != 0 ? AdminMode.IsElevated(engine) == false : !AdminMode.TaskExists();
        });
        AdminRepairBar.IsOpen = broken && Vm.RunAsAdministrator && !_adminBusy;
    }

    /// <summary>Repair = turn administrator mode on again: one UAC prompt registers the task and restarts the engine.</summary>
    private async void AdminRepair_Click(object sender, RoutedEventArgs e)
    {
        if (_adminBusy)
        {
            return;
        }

        _adminBusy = true;
        AdminRepairButton.IsEnabled = false;
        AdminBar.IsOpen = false;
        try
        {
            if (await EngineClient.EnableElevationAsync())
            {
                // The elevated engine takes a moment to show its window.
                for (int i = 0; i < 20 && AdminMode.IsElevated(AdminMode.EngineProcessId()) != true; i++)
                {
                    await Task.Delay(150);
                }
            }
            else
            {
                AdminBar.IsOpen = true;
            }
        }
        finally
        {
            AdminRepairButton.IsEnabled = true;
            _adminBusy = false;
            RefreshStatus();
            RefreshAdminStatus();
        }
    }

    private async void GetGitHubVersion_Click(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri(AppInfo.ReleasesUrl));

    private void WelcomeBar_Closed(InfoBar sender, InfoBarClosedEventArgs args) => Vm.WelcomeDismissed = true;
}
