using System.Diagnostics;
using EnhancedSnap.Core;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Core.Platform;
using EnhancedSnap.Settings.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EnhancedSnap.Settings.Pages;

/// <summary>Settings: app appearance (theme, language) and About (feedback, files, license, repository).</summary>
public sealed partial class AppSettingsPage : Page
{
    public AppSettingsPage()
    {
        InitializeComponent();
        VersionText.Text = S.Format(S.About_Version, AppInfo.Version);
    }

    public Strings S { get; } = Strings.Current;

    public SettingsViewModel Vm => App.ViewModel;

    // UPDATE: In-app update check (GitHub Releases) for the installer and ZIP versions; not when AppInfo.IsPackaged,
    // which the Store updates.

    /// <summary>Navigation parameter true: opened as About (--page about), so the About section opens.</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is true)
        {
            ShowAbout();
        }
    }

    /// <summary>Opens the About section and scrolls it into view.</summary>
    public void ShowAbout()
    {
        AboutExpander.IsExpanded = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => AboutExpander.StartBringIntoView());
    }

    private void Feedback_Click(object sender, RoutedEventArgs e) => Open(Feedback.IssueUrl());

    private void License_Click(object sender, RoutedEventArgs e) => Open(AppInfo.RepositoryFileUrl("LICENSE"));

    private void ThirdParty_Click(object sender, RoutedEventArgs e) => Open(AppInfo.RepositoryFileUrl("THIRD-PARTY-NOTICES.md"));

    private void Privacy_Click(object sender, RoutedEventArgs e) => Open(AppInfo.RepositoryFileUrl("PRIVACY.md"));

    private void Repository_Click(object sender, RoutedEventArgs e) => Open(AppInfo.RepositoryUrl);

    private void OpenData_Click(object sender, RoutedEventArgs e) => OpenFolder(AppInfo.DataDirectory);

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => OpenFolder(AppInfo.LogDirectory);

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        EngineClient.Quit();
        App.Window?.Close();
    }

    private static async void Open(string url) => await Windows.System.Launcher.LaunchUriAsync(new Uri(url));

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        string shown = AppInfo.ExplorerPath(path); // the Store package keeps the files it creates in its own folder
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{shown}\"") { UseShellExecute = false })?.Dispose();
    }
}
