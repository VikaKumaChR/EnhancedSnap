using System.Runtime.InteropServices;
using EnhancedSnap.Core;
using EnhancedSnap.Core.Interop;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Settings.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace EnhancedSnap.Settings;

public sealed partial class MainWindow : Window
{
    private const int DefaultWidthEpx = 1080;
    private const int DefaultHeightEpx = 780;

    public MainWindow(SettingsPage startPage)
    {
        InitializeComponent();

        // Fluent 2 material for long-lived app windows: Mica. WinUI falls back to a solid color by itself
        // when transparency is off, in Energy saver or on unsupported hardware.
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppInfo.BaseDirectory, "Assets", "EnhancedSnap.ico"));
        AppTitleBar.Loaded += (_, _) =>
        {
            UpdateTitleIcon();
            AppTitleBar.XamlRoot.Changed += (_, _) => UpdateTitleIcon();
        };
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Scale(520);
            presenter.PreferredMinimumHeight = Scale(480);
        }

        SizeAndCenter();
        ApplyTexts();
        App.ViewModel.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => App.ViewModel.LanguageChanged -= OnLanguageChanged;
        Activated += OnActivated;
        Select(startPage);
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _backgroundTimer;

    // AppIcon-<n>.png from tools/make_icons.py: the small sizes are pixel-fitted, so draw the one made for this scale.
    private static readonly int[] TitleIconSizes = [16, 20, 24, 28, 32, 36, 40, 48];
    private double _titleIconScale;

    /// <summary>16 epx title bar icon from the PNG rendered for the window's display scale (no resampling blur).</summary>
    private void UpdateTitleIcon()
    {
        double scale = AppTitleBar.XamlRoot?.RasterizationScale ?? 1;
        if (scale == _titleIconScale)
        {
            return;
        }

        _titleIconScale = scale;
        int needed = (int)Math.Round(16 * scale);
        int size = TitleIconSizes.FirstOrDefault(s => s >= needed, TitleIconSizes[^1]);
        TitleIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri($"ms-appx:///Assets/AppIcon-{size}.png"));
    }

    /// <summary>In the background for 5 s → efficiency mode and a trimmed working set; one-shot, no polling.</summary>
    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (_backgroundTimer is null)
            {
                _backgroundTimer = DispatcherQueue.CreateTimer();
                _backgroundTimer.Interval = TimeSpan.FromSeconds(5);
                _backgroundTimer.IsRepeating = false;
                _backgroundTimer.Tick += (_, _) => Services.BackgroundFootprint.Enter();
            }

            _backgroundTimer.Start();
        }
        else
        {
            _backgroundTimer?.Stop();
            Services.BackgroundFootprint.Leave();
        }
    }

    /// <summary>Called when a second launch (tray click, Start menu) asks this instance to come forward.</summary>
    public void BringToFront(SettingsPage page)
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        Win32.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        Select(page);
    }

    public void Select(SettingsPage page)
    {
        Nav.SelectedItem = page switch
        {
            SettingsPage.Layouts => NavLayouts,
            SettingsPage.Appearance => NavAppearance,
            SettingsPage.Keyboard => NavKeyboard,
            SettingsPage.Apps => NavApps,
            SettingsPage.About => NavAbout,
            _ => NavGeneral,
        };
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag })
        {
            return;
        }

        Type pageType = tag switch
        {
            "Layouts" => typeof(LayoutsPage),
            "Appearance" => typeof(AppearancePage),
            "Keyboard" => typeof(KeyboardPage),
            "Apps" => typeof(AppsPage),
            "About" => typeof(AboutPage),
            _ => typeof(GeneralPage),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
            ContentFrame.BackStack.Clear();
        }
    }

    private void OnLanguageChanged()
    {
        ApplyTexts();
        if (ContentFrame.CurrentSourcePageType is { } current)
        {
            ContentFrame.Navigate(current, null, new SuppressNavigationTransitionInfo());
            ContentFrame.BackStack.Clear();
        }
    }

    private void ApplyTexts()
    {
        var s = Strings.Current;
        Title = s.AppName;
        TitleText.Text = s.AppName;
        NavGeneral.Content = s.Nav_General;
        NavLayouts.Content = s.Nav_Layouts;
        NavAppearance.Content = s.Nav_Appearance;
        NavKeyboard.Content = s.Nav_Keyboard;
        NavApps.Content = s.Nav_Apps;
        NavAbout.Content = s.Nav_About;
    }

    private void SizeAndCenter()
    {
        int width = Scale(DefaultWidthEpx);
        int height = Scale(DefaultHeightEpx);
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        width = Math.Min(width, work.Width - 48);
        height = Math.Min(height, work.Height - 48);
        AppWindow.MoveAndResize(new RectInt32(work.X + ((work.Width - width) / 2), work.Y + ((work.Height - height) / 2), width, height));
    }

    private int Scale(int epx) => (int)Math.Round(epx * GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);
}
