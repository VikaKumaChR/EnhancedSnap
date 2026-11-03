using System.Diagnostics;
using EnhancedSnap.Core;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Settings.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EnhancedSnap.Settings.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        VersionText.Text = S.Format(S.About_Version, AppInfo.Version);
    }

    public Strings S { get; } = Strings.Current;

    // UPDATE: In-app update check (GitHub Releases) for the installer and ZIP versions; not when AppInfo.IsPackaged,
    // which the Store updates.

    private void OpenData_Click(object sender, RoutedEventArgs e) => OpenFolder(AppInfo.DataDirectory);

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => OpenFolder(AppInfo.LogDirectory);

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        EngineClient.Quit();
        App.Window?.Close();
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        string shown = AppInfo.ExplorerPath(path); // the Store package keeps the files it creates in its own folder
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{shown}\"") { UseShellExecute = false })?.Dispose();
    }
}
