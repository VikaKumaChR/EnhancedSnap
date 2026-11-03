using EnhancedSnap.Core.Layouts;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EnhancedSnap.Settings.Controls;

/// <summary>
/// Custom layout editor: columns or rows, each with a relative size and a number of splits,
/// with a live preview. Keyboard friendly (sliders and number boxes) — no fiddly drag handles.
/// </summary>
internal static class LayoutEditorDialog
{
    // UPDATE: Direct manipulation editor (drag dividers on a full-screen canvas, like the FancyZones grid editor).

    public static async Task<CustomLayout?> ShowAsync(XamlRoot root, CustomLayout? existing, double aspect)
    {
        var s = Strings.Current;
        var working = new CustomLayout
        {
            Id = existing?.Id ?? LayoutCatalog.NewCustomLayoutId(),
            Name = existing?.Name ?? s.Editor_DefaultName,
            Orientation = existing?.Orientation ?? LayoutOrientation.Columns,
            Tracks = existing?.Tracks.Select(t => new LayoutTrack { Weight = t.Weight, Splits = t.Splits }).ToList()
                ?? [new LayoutTrack { Weight = 2 }, new LayoutTrack { Weight = 1, Splits = 2 }],
        };

        var nameBox = new TextBox { Header = s.Editor_Name, Text = working.Name, MaxLength = 40 };
        var orientation = new RadioButtons { Header = s.Editor_Orientation, MaxColumns = 2, SelectedIndex = (int)working.Orientation };
        orientation.Items.Add(s.Editor_Columns);
        orientation.Items.Add(s.Editor_Rows);

        double previewHeight = 150;
        var preview = new LayoutPreview { AspectRatio = aspect, ReferenceWidthEpx = 520, GapEpx = 8, MarginEpx = 8, CornerRadiusEpx = 4 };
        var previewFrame = new Border
        {
            Width = Math.Clamp(previewHeight * aspect, 180, 420),
            Height = previewHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlAltFillColorTertiaryBrush"],
            Child = preview,
        };

        var tracksPanel = new StackPanel { Spacing = 6 };
        var addButton = new Button();

        void UpdatePreview() => preview.Layout = LayoutCatalog.FromCustom(Clone(working));

        void Rebuild()
        {
            tracksPanel.Children.Clear();
            bool columns = working.Orientation == LayoutOrientation.Columns;
            for (int i = 0; i < working.Tracks.Count; i++)
            {
                var track = working.Tracks[i];
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = s.Format(columns ? s.Editor_ColumnN : s.Editor_RowN, i + 1),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var size = new Slider { Minimum = 1, Maximum = 6, StepFrequency = 0.5, Value = track.Weight, VerticalAlignment = VerticalAlignment.Center, Header = null };
                ToolTipService.SetToolTip(size, s.Editor_Size);
                size.ValueChanged += (_, e) =>
                {
                    track.Weight = e.NewValue;
                    UpdatePreview();
                };
                var splits = new NumberBox
                {
                    Minimum = 1,
                    Maximum = CustomLayout.MaxSplits,
                    Value = track.Splits,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 112,
                };
                ToolTipService.SetToolTip(splits, s.Editor_Splits);
                splits.ValueChanged += (_, e) =>
                {
                    track.Splits = double.IsNaN(e.NewValue) ? 1 : (int)Math.Clamp(e.NewValue, 1, CustomLayout.MaxSplits);
                    UpdatePreview();
                };
                var remove = new Button
                {
                    Content = new FontIcon { Glyph = "", FontSize = 14 },
                    IsEnabled = working.Tracks.Count > 1,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                ToolTipService.SetToolTip(remove, s.Action_Remove);
                int index = i;
                remove.Click += (_, _) =>
                {
                    working.Tracks.RemoveAt(index);
                    Rebuild();
                };

                Grid.SetColumn(size, 1);
                Grid.SetColumn(splits, 2);
                Grid.SetColumn(remove, 3);
                row.Children.Add(label);
                row.Children.Add(size);
                row.Children.Add(splits);
                row.Children.Add(remove);
                tracksPanel.Children.Add(row);
            }

            addButton.Content = columns ? s.Editor_AddColumn : s.Editor_AddRow;
            addButton.IsEnabled = working.Tracks.Count < CustomLayout.MaxTracks;
            UpdatePreview();
        }

        orientation.SelectionChanged += (_, _) =>
        {
            working.Orientation = orientation.SelectedIndex == 1 ? LayoutOrientation.Rows : LayoutOrientation.Columns;
            Rebuild();
        };
        addButton.Click += (_, _) =>
        {
            working.Tracks.Add(new LayoutTrack());
            Rebuild();
        };

        var columnsHeader = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 8, 0, 0) };
        columnsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        columnsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columnsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        columnsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        var sizeHeader = Caption(s.Editor_Size);
        var splitsHeader = Caption(s.Editor_Splits);
        Grid.SetColumn(sizeHeader, 1);
        Grid.SetColumn(splitsHeader, 2);
        columnsHeader.Children.Add(sizeHeader);
        columnsHeader.Children.Add(splitsHeader);

        var content = new StackPanel { Spacing = 12, MinWidth = 440 };
        content.Children.Add(previewFrame);
        content.Children.Add(nameBox);
        content.Children.Add(orientation);
        content.Children.Add(columnsHeader);
        content.Children.Add(tracksPanel);
        content.Children.Add(addButton);
        Rebuild();

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = s.Editor_Title,
            Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = s.Action_Save,
            CloseButtonText = s.Action_Cancel,
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        working.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? s.Editor_DefaultName : nameBox.Text.Trim();
        working.Normalize();
        return working;
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };

    private static CustomLayout Clone(CustomLayout layout) => new()
    {
        Id = layout.Id,
        Name = layout.Name,
        Orientation = layout.Orientation,
        Tracks = layout.Tracks.Select(t => new LayoutTrack { Weight = t.Weight, Splits = t.Splits }).ToList(),
    };
}
