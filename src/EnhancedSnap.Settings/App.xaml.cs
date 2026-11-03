using EnhancedSnap.Core;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Settings.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace EnhancedSnap.Settings;

public partial class App : Application
{
    private readonly SettingsPage _startPage;
    private readonly bool _welcome;
    private MainWindow? _window;
    private MessageWindow? _messages;

    public App(SettingsPage startPage, bool welcome)
    {
        _startPage = startPage;
        _welcome = welcome;
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled UI exception", e.Exception);
            e.Handled = true; // a settings glitch must never take the window down
        };
    }

    public static SettingsViewModel ViewModel { get; private set; } = null!;

    public static bool ShowWelcome { get; private set; }

    public static MainWindow? Window => (Current as App)?._window;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ViewModel = new SettingsViewModel(DispatcherQueue.GetForCurrentThread());
        Strings.Use(ViewModel.Current.Language);
        ShowWelcome = _welcome || !ViewModel.Current.WelcomeDismissed;

        _window = new MainWindow(_startPage);
        _messages = new MessageWindow(OnMessage);
        _window.Closed += (_, _) => _messages?.Dispose();
        _window.Activate();
    }

    private void OnMessage(uint message, nuint argument)
    {
        if (message == IpcMessages.SettingsReload)
        {
            ViewModel.Reload();
        }
        else if (message == IpcMessages.SettingsActivate)
        {
            _window?.BringToFront((SettingsPage)(int)argument);
        }
    }
}
