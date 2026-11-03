using EnhancedSnap.Core.Localization;
using EnhancedSnap.Settings.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace EnhancedSnap.Settings.Pages;

public sealed partial class ExcludedApp(string name)
{
    public string Name { get; } = name;
}

public sealed partial class AppsPage : Page
{
    public AppsPage()
    {
        InitializeComponent();
        Refresh();
    }

    public Strings S { get; } = Strings.Current;

    /// <summary>The Store package offers no administrator mode, so it doesn't suggest one.</summary>
    public string AdminBody => Core.AppInfo.IsPackaged ? S.Apps_AdminBodyPackaged : S.Apps_AdminBody;

    private SettingsViewModel Vm => App.ViewModel;

    // UPDATE: "Pick from running apps" flyout (enumerate top-level windows → process names) instead of typing.

    private void Refresh()
    {
        AppList.ItemsSource = Vm.Current.ExcludedApps.Select(a => new ExcludedApp(a)).ToList();
        EmptyText.Visibility = Vm.Current.ExcludedApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Add()
    {
        string name = AppBox.Text.Trim().Trim('"');
        if (name.Length == 0)
        {
            return;
        }

        name = Path.GetFileName(name); // accept full paths pasted from Explorer
        Vm.Update(s =>
        {
            if (!s.ExcludedApps.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                s.ExcludedApps.Add(name);
            }
        });
        AppBox.Text = string.Empty;
        Refresh();
    }

    private void Add_Click(object sender, RoutedEventArgs e) => Add();

    private void AppBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            Add();
            e.Handled = true;
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
        {
            Vm.Update(s => s.ExcludedApps.RemoveAll(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)));
            Refresh();
        }
    }
}
