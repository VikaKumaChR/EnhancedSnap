using EnhancedSnap.Core.Localization;
using EnhancedSnap.Settings.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace EnhancedSnap.Settings.Pages;

public sealed partial class KeyboardPage : Page
{
    public KeyboardPage()
    {
        InitializeComponent();
        BuildShortcuts();
    }

    public Strings S { get; } = Strings.Current;

    public SettingsViewModel Vm => App.ViewModel;

    private void BuildShortcuts()
    {
        AddRow([S.Key_Edge], S.Shortcut_EdgeDrag);
        AddRow(["Win", "Z"], S.Shortcut_WinZ);
        AddRow([S.Key_Divider], S.Shortcut_Divider);
        AddRow([S.Key_GapFill], S.Shortcut_GapFill);
        AddRow(["Alt", "Tab"], S.Shortcut_PickGroup);
        AddRow(["Shift", S.Key_Drag], S.Shortcut_Drag);
        AddRow(["Ctrl", S.Key_Drag], S.Shortcut_Span);
        AddRow(["Win", "←", "→"], S.Shortcut_LeftRight);
        AddRow(["Win", "↑"], S.Shortcut_Up);
        AddRow(["Win", "↓"], S.Shortcut_Down, last: true);
    }

    private void AddRow(string[] keys, string description, bool last = false)
    {
        int row = ShortcutGrid.RowDefinitions.Count;
        ShortcutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var caps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 12, 0, 12) };
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0)
            {
                caps.Children.Add(new TextBlock
                {
                    Text = keys[i] is "→" ? "/" : "+",
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                });
            }

            caps.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["KeyCapBorderStyle"],
                Child = new TextBlock { Text = keys[i], Style = (Style)Application.Current.Resources["KeyCapTextStyle"] },
            });
        }

        var text = new TextBlock { Text = description, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(caps, row);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);
        ShortcutGrid.Children.Add(caps);
        ShortcutGrid.Children.Add(text);

        if (!last)
        {
            var divider = new Border
            {
                Height = 1,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
            };
            Grid.SetRow(divider, row);
            Grid.SetColumnSpan(divider, 2);
            ShortcutGrid.Children.Add(divider);
        }
    }
}
