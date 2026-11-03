using EnhancedSnap.Core.Displays;
using EnhancedSnap.Core.Layouts;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Core.Settings;
using EnhancedSnap.Settings.Controls;
using EnhancedSnap.Settings.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EnhancedSnap.Settings.Pages;

public sealed partial class DisplayItem(DisplayInfo display, string name, string details)
{
    public DisplayInfo Display { get; } = display;

    public string Name { get; } = name;

    public string Details { get; } = details;
}

public sealed partial class LayoutItem(LayoutDefinition definition, string name, string details, double aspect)
{
    public LayoutDefinition Definition { get; } = definition;

    public string Name { get; } = name;

    public string Details { get; } = details;

    public double Aspect { get; } = aspect;

    public double PreviewHeight { get; } = Math.Clamp(160 / aspect, 64, 132);
}

public sealed partial class LayoutsPage : Page
{
    private List<DisplayInfo> _displays = [];
    private DisplayInfo? _display;
    private bool _updating;

    public LayoutsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadDisplays();
    }

    public Strings S { get; } = Strings.Current;

    private SettingsViewModel Vm => App.ViewModel;

    private void LoadDisplays()
    {
        _displays = DisplayEnumerator.GetDisplays();
        var items = _displays.Select(d =>
        {
            string details = S.Format(S.Display_Details, d.Bounds.Width, d.Bounds.Height, (int)Math.Round(d.Scale * 100));
            if (d.IsPrimary)
            {
                details += " · " + S.Display_Primary;
            }

            return new DisplayItem(d, S.DisplayName(d), details);
        }).ToList();

        _updating = true;
        DisplayGrid.ItemsSource = items;
        var primary = items.FirstOrDefault(i => i.Display.IsPrimary) ?? items.FirstOrDefault();
        DisplayGrid.SelectedItem = primary;
        _updating = false;
        SelectDisplay(primary?.Display);
    }

    private void DisplayGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && DisplayGrid.SelectedItem is DisplayItem item)
        {
            SelectDisplay(item.Display);
        }
    }

    private void SelectDisplay(DisplayInfo? display)
    {
        _display = display;
        RefreshLayouts();
    }

    private void RefreshLayouts()
    {
        if (_display is null)
        {
            return;
        }

        var settings = Vm.Current;
        double aspect = _display.WorkArea.Height > 0 ? (double)_display.WorkArea.Width / _display.WorkArea.Height : 16.0 / 9.0;

        var items = LayoutCatalog.BuiltIns
            .Select(l => new LayoutItem(l, S.LayoutName(l), S.Format(S.Layout_ZoneCount, l.Zones.Count), aspect))
            .Concat(settings.CustomLayouts.Select(c =>
            {
                var definition = LayoutCatalog.FromCustom(c);
                return new LayoutItem(definition, c.Name, $"{S.Layout_Custom} · {S.Format(S.Layout_ZoneCount, definition.Zones.Count)}", aspect);
            }))
            .ToList();

        var current = LayoutCatalog.ResolveForMonitor(settings, _display.Id, _display.WorkArea.Width, _display.WorkArea.Height);
        _updating = true;
        LayoutGrid.ItemsSource = items;
        LayoutGrid.SelectedItem = items.FirstOrDefault(i => i.Definition.Id == current.Id);
        _updating = false;

        RefreshCustomList();
    }

    private void LayoutGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || _display is null || LayoutGrid.SelectedItem is not LayoutItem item)
        {
            return;
        }

        var display = _display;
        Vm.Update(s => s.Monitors[display.Id] = new MonitorSettings { LayoutId = item.Definition.Id, Name = S.DisplayName(display) });
        EngineClient.PreviewZones(display.Handle); // posted after the reload notification, so the new zones show
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e) => EngineClient.PreviewZones(_display?.Handle ?? 0);

    private void RefreshCustomList()
    {
        CustomList.Children.Clear();
        foreach (var custom in Vm.Current.CustomLayouts)
        {
            var definition = LayoutCatalog.FromCustom(custom);
            var preview = new Border
            {
                Width = 56,
                Height = 34,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                Child = new LayoutPreview { AspectRatio = CurrentAspect(), Layout = definition, ReferenceWidthEpx = 240, GapEpx = 8, MarginEpx = 6, CornerRadiusEpx = 3 },
            };

            var edit = new Button { Content = S.Action_Edit, Tag = custom.Id };
            edit.Click += EditLayout_Click;
            var delete = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, Tag = custom.Id };
            ToolTipService.SetToolTip(delete, S.Action_Delete);
            delete.Click += DeleteLayout_Click;

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            actions.Children.Add(edit);
            actions.Children.Add(delete);

            CustomList.Children.Add(new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header = BuildHeader(preview, custom.Name, S.Format(S.Layout_ZoneCount, definition.Zones.Count)),
                Content = actions,
            });
        }

        CustomEmpty.Visibility = Vm.Current.CustomLayouts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static StackPanel BuildHeader(UIElement preview, string name, string details)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = name });
        text.Children.Add(new TextBlock
        {
            Text = details,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        header.Children.Add(preview);
        header.Children.Add(text);
        return header;
    }

    private double CurrentAspect() =>
        _display is { WorkArea.Height: > 0 } d ? (double)d.WorkArea.Width / d.WorkArea.Height : 16.0 / 9.0;

    private async void NewLayout_Click(object sender, RoutedEventArgs e)
    {
        var created = await LayoutEditorDialog.ShowAsync(XamlRoot, null, CurrentAspect());
        if (created is null)
        {
            return;
        }

        var display = _display;
        Vm.Update(s =>
        {
            s.CustomLayouts.Add(created);
            if (display is not null)
            {
                s.Monitors[display.Id] = new MonitorSettings { LayoutId = created.Id, Name = S.DisplayName(display) };
            }
        });
        RefreshLayouts();
        if (display is not null)
        {
            EngineClient.PreviewZones(display.Handle);
        }
    }

    private async void EditLayout_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } || Vm.Current.CustomLayouts.FirstOrDefault(c => c.Id == id) is not { } existing)
        {
            return;
        }

        var edited = await LayoutEditorDialog.ShowAsync(XamlRoot, existing, CurrentAspect());
        if (edited is null)
        {
            return;
        }

        Vm.Update(s =>
        {
            int index = s.CustomLayouts.FindIndex(c => c.Id == id);
            if (index >= 0)
            {
                s.CustomLayouts[index] = edited;
            }
        });
        RefreshLayouts();
    }

    private async void DeleteLayout_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } || Vm.Current.CustomLayouts.FirstOrDefault(c => c.Id == id) is not { } existing)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = S.Format(S.Confirm_DeleteLayout, existing.Name),
            Content = S.Confirm_DeleteLayoutBody,
            PrimaryButtonText = S.Action_Delete,
            CloseButtonText = S.Action_Cancel,
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        Vm.Update(s =>
        {
            s.CustomLayouts.RemoveAll(c => c.Id == id);
            foreach (var key in s.Monitors.Where(m => m.Value.LayoutId == id).Select(m => m.Key).ToList())
            {
                s.Monitors.Remove(key);
            }
        });
        RefreshLayouts();
    }
}
