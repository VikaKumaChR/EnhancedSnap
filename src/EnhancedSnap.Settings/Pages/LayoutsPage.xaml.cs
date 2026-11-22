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

        // What each is for: with zones on every drag, Shift is what skips them, so the pop down then needs Shift.
        bool always = settings.Activation == ActivationMode.Always;
        ZonesCaption.Text = always ? S.Layouts_ZonesCaptionAlways : S.Layouts_ZonesCaption;
        MenuCaption.Text = always ? S.SnapBar_HeaderDescAlways : S.SnapBar_HeaderDesc;
        RefreshCustomList();
        RefreshSnapBar();
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
        RefreshSnapBar(); // the main display's own zone layout leads its default set
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e) => EngineClient.PreviewZones(_display?.Handle ?? 0);

    private void RefreshCustomList()
    {
        CustomList.Children.Clear();
        foreach (var custom in Vm.Current.CustomLayouts)
        {
            var definition = LayoutCatalog.FromCustom(custom);
            var preview = LayoutGlyph(definition, 40, CurrentAspect());

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

    private StackPanel BuildHeader(UIElement preview, string name, string details)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = name });
        text.Children.Add(new TextBlock { Text = details, Style = (Style)Resources["SecondaryCaptionStyle"] });

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
            s.SnapBarLayouts.CustomLayoutIds.RemoveAll(i => i == id);
            foreach (var key in s.Monitors.Where(m => m.Value.LayoutId == id).Select(m => m.Key).ToList())
            {
                s.Monitors.Remove(key);
            }
        });
        RefreshLayouts();
    }

    // ------------------------------------------------------------------ the pop down at the top of the screen (suggestion00)

    /// <summary>
    /// The snap layout menu's sets (the same in the pop down at the top and on the maximize button): default (default
    /// snap) and top and bottom as previews, and the custom set (customization) as a list to add to, reorder and remove
    /// from. The menu is for every display, so it doesn't follow the display chosen below: the default set is shown as
    /// the main display gets it. Editing a layout's zones stays in the editor dialog; while a window is dragged, the
    /// pop down only offers what is set up here.
    /// </summary>
    private void RefreshSnapBar()
    {
        if ((_displays.FirstOrDefault(static d => d.IsPrimary) ?? _display) is not { } main)
        {
            return;
        }

        var settings = Vm.Current;
        var work = main.WorkArea;
        double aspect = work.Height > 0 ? (double)work.Width / work.Height : 16.0 / 9.0;
        var monitorLayout = LayoutCatalog.ResolveForMonitor(settings, main.Id, work.Width, work.Height);
        ClassicSetCard.HeaderIcon = LayoutIcon(LayoutCatalog.FindBuiltIn(LayoutCatalog.Halves)!, 20);
        HorizontalSetCard.HeaderIcon = LayoutIcon(LayoutCatalog.FindBuiltIn(LayoutCatalog.Rows)!, 20);
        CustomSetCard.HeaderIcon = new FontIcon { Glyph = "" };
        CustomSetCard.Description = S.Format(S.SnapBar_CustomDesc, SnapBarSettings.MaxCustomLayouts);
        FillPreviews(ClassicPreviews, SnapBarCatalog.Classic(work.Width, work.Height, main.Scale, monitorLayout), aspect);
        FillPreviews(HorizontalPreviews, SnapBarCatalog.Horizontal, aspect);

        var ids = settings.SnapBarLayouts.CustomLayoutIds;
        CustomSetCount.Text = S.Format(S.SnapBar_Count, ids.Count, SnapBarSettings.MaxCustomLayouts);
        AddToSetButton.IsEnabled = ids.Count < SnapBarSettings.MaxCustomLayouts;
        CustomSetList.Children.Clear();
        for (int i = 0; i < ids.Count; i++)
        {
            if (LayoutCatalog.Resolve(settings, ids[i]) is not { } layout)
            {
                continue;
            }

            var preview = LayoutGlyph(layout, 40, aspect);
            string name = S.LayoutName(layout);
            string details = $"{(layout.IsCustom ? S.Layout_Custom : S.SnapBar_BuiltIn)} · {S.Format(S.Layout_ZoneCount, layout.Zones.Count)}";

            // Move up and down first, so they keep their places (keyboard focus follows a moved layout by position).
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            actions.Children.Add(IconButton("", S.Action_MoveUp, name, i, i > 0, MoveUp_Click));
            actions.Children.Add(IconButton("", S.Action_MoveDown, name, i, i < ids.Count - 1, MoveDown_Click));
            if (layout.IsCustom)
            {
                // The same editor as above: its zones and its name.
                var edit = new Button { Content = S.Action_Edit, Tag = layout.Id };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(edit, $"{S.Action_Edit}: {name}");
                edit.Click += EditLayout_Click;
                actions.Children.Add(edit);
            }

            actions.Children.Add(IconButton("", S.Action_Remove, name, i, true, RemoveFromSet_Click));
            CustomSetList.Children.Add(new CommunityToolkit.WinUI.Controls.SettingsCard { Header = BuildHeader(preview, name, details), Content = actions });
        }

        CustomSetEmpty.Visibility = ids.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BuildAddMenu(settings, ids);
    }

    /// <summary>The layouts a set offers, as small previews named for screen readers.</summary>
    private void FillPreviews(StackPanel panel, IEnumerable<LayoutDefinition> layouts, double aspect)
    {
        panel.Children.Clear();
        foreach (var layout in layouts)
        {
            var preview = LayoutGlyph(layout, 36, aspect);
            string name = S.LayoutName(layout);
            ToolTipService.SetToolTip(preview, name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(preview, name);
            panel.Children.Add(preview);
        }
    }

    /// <summary>Every layout not in the custom set yet (built-in, then the user's own), and a new one made for it.</summary>
    private void BuildAddMenu(AppSettings settings, List<string> ids)
    {
        AddToSetMenu.Items.Clear();
        foreach (var layout in LayoutCatalog.BuiltIns.Where(l => l.Id != LayoutCatalog.Full && !ids.Contains(l.Id, StringComparer.OrdinalIgnoreCase)))
        {
            AddToSetMenu.Items.Add(AddItem(layout));
        }

        var own = settings.CustomLayouts.Where(c => !ids.Contains(c.Id)).ToList();
        if (own.Count > 0)
        {
            AddToSetMenu.Items.Add(new MenuFlyoutSeparator());
            foreach (var custom in own)
            {
                AddToSetMenu.Items.Add(AddItem(LayoutCatalog.FromCustom(custom)));
            }
        }

        AddToSetMenu.Items.Add(new MenuFlyoutSeparator());
        var create = new MenuFlyoutItem { Text = S.SnapBar_NewLayout, Icon = new FontIcon { Glyph = "" } };
        create.Click += NewLayoutForSet_Click;
        AddToSetMenu.Items.Add(create);
    }

    private MenuFlyoutItem AddItem(LayoutDefinition layout)
    {
        var item = new MenuFlyoutItem { Text = S.LayoutName(layout), Icon = LayoutIcon(layout, 16), Tag = layout.Id };
        item.Click += AddToSet_Click;
        return item;
    }

    private static Button IconButton(string glyph, string action, string layoutName, int index, bool enabled, RoutedEventHandler click)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 14 }, Tag = index, IsEnabled = enabled };
        ToolTipService.SetToolTip(button, action);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{action}: {layoutName}");
        button.Click += click;
        return button;
    }

    /// <summary>A layout as a square icon (its zones filled in the text color) for menus and card headers.</summary>
    private static PathIcon LayoutIcon(LayoutDefinition layout, double size)
    {
        double height = Math.Round(size * 0.7);
        var icon = Pictogram(layout, size, height, size / 10, Math.Round((size - height) / 2));
        icon.Width = size;
        icon.Height = size;
        return icon;
    }

    /// <summary>
    /// A small picture of a layout in the display's shape: its zones as solid rounded shapes 2 epx apart, in the secondary text
    /// color. At list size it reads where a scaled-down preview doesn't: below about 60 epx a preview's gaps and outlines
    /// shrink to hairlines and its light zones blend into the card. The colour comes from a style, so it follows a switch
    /// to light, dark or high contrast while the page is open.
    /// </summary>
    private PathIcon LayoutGlyph(LayoutDefinition layout, double width, double aspect)
    {
        double height = Math.Round(Math.Clamp(width / aspect, width * 0.4, width));
        var icon = Pictogram(layout, width, height, 2, 0);
        icon.Width = width;
        icon.Height = height;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Style = (Style)Resources["LayoutGlyphStyle"];
        return icon;
    }

    private static PathIcon Pictogram(LayoutDefinition layout, double width, double height, double gap, double top)
    {
        var zones = new Microsoft.UI.Xaml.Media.PathGeometry();
        foreach (var z in layout.Zones)
        {
            zones.Figures.Add(RoundedZone(
                z.X * (width + gap),
                top + (z.Y * (height + gap)),
                Math.Max(1, (z.W * (width + gap)) - gap),
                Math.Max(1, (z.H * (height + gap)) - gap)));
        }

        return new PathIcon { Data = zones };
    }

    /// <summary>
    /// A zone of a pictogram with rounded corners, like the zones of Windows' own snap layouts. Fluent 2 rounds shapes
    /// under 32 px by 2 px (Fluent 2, Shapes: "for shapes smaller than 32 pixels, the corner angle is reduced to 2
    /// pixels"); a zone narrower or lower than 8 epx gets a quarter of that, so it stays a rectangle and not a pill.
    /// </summary>
    private static Microsoft.UI.Xaml.Media.PathFigure RoundedZone(double left, double top, double width, double height)
    {
        double r = Math.Min(2, Math.Min(width, height) / 4);
        double right = left + width;
        double bottom = top + height;
        var corner = new Windows.Foundation.Size(r, r);
        var figure = new Microsoft.UI.Xaml.Media.PathFigure { StartPoint = new Windows.Foundation.Point(left + r, top), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new Microsoft.UI.Xaml.Media.LineSegment { Point = new Windows.Foundation.Point(right - r, top) });
        figure.Segments.Add(Arc(right, top + r));
        figure.Segments.Add(new Microsoft.UI.Xaml.Media.LineSegment { Point = new Windows.Foundation.Point(right, bottom - r) });
        figure.Segments.Add(Arc(right - r, bottom));
        figure.Segments.Add(new Microsoft.UI.Xaml.Media.LineSegment { Point = new Windows.Foundation.Point(left + r, bottom) });
        figure.Segments.Add(Arc(left, bottom - r));
        figure.Segments.Add(new Microsoft.UI.Xaml.Media.LineSegment { Point = new Windows.Foundation.Point(left, top + r) });
        figure.Segments.Add(Arc(left + r, top));
        return figure;

        Microsoft.UI.Xaml.Media.ArcSegment Arc(double x, double y) => new()
        {
            Point = new Windows.Foundation.Point(x, y),
            Size = corner,
            SweepDirection = Microsoft.UI.Xaml.Media.SweepDirection.Clockwise,
        };
    }

    private void AddToSet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string id })
        {
            Vm.Update(s =>
            {
                var ids = s.SnapBarLayouts.CustomLayoutIds;
                if (ids.Count < SnapBarSettings.MaxCustomLayouts && !ids.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    ids.Add(id);
                }
            });
            RefreshSnapBar();
        }
    }

    private async void NewLayoutForSet_Click(object sender, RoutedEventArgs e)
    {
        var created = await LayoutEditorDialog.ShowAsync(XamlRoot, null, CurrentAspect());
        if (created is null)
        {
            return;
        }

        Vm.Update(s =>
        {
            s.CustomLayouts.Add(created);
            if (s.SnapBarLayouts.CustomLayoutIds.Count < SnapBarSettings.MaxCustomLayouts)
            {
                s.SnapBarLayouts.CustomLayoutIds.Add(created.Id);
            }
        });
        RefreshLayouts();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveInSet(sender, -1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveInSet(sender, +1);

    /// <summary>Swaps a layout with its neighbour; keyboard focus follows it to its new place.</summary>
    private void MoveInSet(object sender, int step)
    {
        if (sender is not Button { Tag: int index })
        {
            return;
        }

        int target = index + step;
        bool moved = false;
        Vm.Update(s =>
        {
            var ids = s.SnapBarLayouts.CustomLayoutIds;
            if (index >= 0 && index < ids.Count && target >= 0 && target < ids.Count)
            {
                (ids[index], ids[target]) = (ids[target], ids[index]);
                moved = true;
            }
        });
        if (!moved)
        {
            return;
        }

        RefreshSnapBar();
        if (CustomSetList.Children.ElementAtOrDefault(target) is CommunityToolkit.WinUI.Controls.SettingsCard { Content: StackPanel actions })
        {
            // The same button, unless it has just become the disabled one at the end of the list: then the other.
            var button = actions.Children.OfType<Button>().ElementAtOrDefault(step < 0 ? 0 : 1);
            if (button is { IsEnabled: false })
            {
                button = actions.Children.OfType<Button>().ElementAtOrDefault(step < 0 ? 1 : 0);
            }

            button?.Focus(FocusState.Keyboard);
        }
    }

    private void RemoveFromSet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index })
        {
            Vm.Update(s =>
            {
                if (index >= 0 && index < s.SnapBarLayouts.CustomLayoutIds.Count)
                {
                    s.SnapBarLayouts.CustomLayoutIds.RemoveAt(index);
                }
            });
            RefreshSnapBar();
        }
    }
}
