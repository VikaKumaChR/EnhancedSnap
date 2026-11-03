using System.Numerics;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Layouts;
using EnhancedSnap.Engine.Overlay;
using EnhancedSnap.Engine.Snapping;
using Windows.UI.Composition;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Ui;

/// <summary>A snap suggestion: a layout with a window for each zone (zone 0 is the flyout's own window).</summary>
internal sealed record SnapSuggestion(LayoutDefinition Layout, nint[] Windows);

/// <summary>
/// Snap layouts flyout — Enhanced Snap's replacement for Windows' maximize-button flyout (also Win + Z) and, in drag
/// mode, for the snap bar at the top of the screen. Drawn like Windows' own: no card behind a layout, each zone a
/// rounded tile in ControlAltFillColorQuarternary with a 1 epx ControlStrongStroke outline, accent on hover; two
/// columns of layouts (three on ultrawides), one row in the snap bar. The layouts are the ones Windows offers for the
/// screen's size, and like Windows the first row holds snap suggestions: the window with recently used apps, whose
/// zones show the app's icon — one click snaps them all. Hovering a zone also previews it full-size on the monitor
/// (Windows doesn't).
/// </summary>
internal sealed class LayoutFlyout(CompositionHost host, DrawingSurfaces surfaces) : PopupWindow(host)
{
    private readonly List<Card> _cards = [];
    private (int Card, int Zone) _hover = (-1, -1);
    private MonitorZones? _monitor;
    private IReadOnlyList<LayoutDefinition> _layouts = [];
    private IReadOnlyList<SnapSuggestion> _suggestions = [];

    /// <summary>Raised when the hovered zone changes (layout null = none).</summary>
    public event Action<MonitorZones, LayoutDefinition?, int>? ZoneHovered;

    /// <summary>Raised when a zone is clicked (not in drag mode, where the drop decides).</summary>
    public event Action<nint, MonitorZones, LayoutDefinition, int>? ZoneChosen;

    /// <summary>Raised when a snap suggestion is clicked.</summary>
    public event Action<MonitorZones, SnapSuggestion>? SuggestionChosen;

    public nint Window { get; private set; }

    public bool DragMode { get; private set; }

    /// <summary>Anchor point (maximize button) the flyout belongs to; used for hover dismissal.</summary>
    public PointI Anchor { get; private set; }

    public (LayoutDefinition Layout, int Zone)? Hovered =>
        _hover.Card >= 0 && _cards[_hover.Card].Suggestion is null ? (_cards[_hover.Card].Layout, _hover.Zone) : null;

    /// <summary>Test probes: the suggestions on show.</summary>
    public SnapSuggestion? FirstSuggestion => _suggestions.ElementAtOrDefault(0);

    public SnapSuggestion? SecondSuggestion => _suggestions.ElementAtOrDefault(1);

    /// <param name="recent">Other windows, most recent first: the apps snap suggestions offer.</param>
    public void ShowFor(nint window, MonitorZones monitor, PointI anchor, FluentTheme theme, bool dragMode, IReadOnlyList<nint> recent)
    {
        Window = window;
        DragMode = dragMode;
        Anchor = anchor;
        _monitor = monitor;
        _layouts = PickLayouts(monitor);
        _suggestions = dragMode ? [] : Suggest(window, monitor, recent);

        float scale = (float)monitor.Display.Scale;
        var work = monitor.Display.WorkArea;
        double aspect = Math.Clamp(work.Width / (double)Math.Max(1, work.Height), 0.5, 3.6);
        int columns = dragMode ? _layouts.Count : Math.Min(aspect >= 2.1 ? 3 : 2, _layouts.Count);
        int rows = (int)Math.Ceiling(_layouts.Count / (double)columns) + (_suggestions.Count > 0 ? 1 : 0);
        float cardWidth = (dragMode ? 72 : 96) * scale;
        // Windows' cards are about 3:2 on ordinary screens; ultrawide and portrait screens keep their own shape.
        float shape = aspect >= 2.1 || aspect < 1 ? (float)aspect : MathF.Min((float)aspect, 1.55f);
        float cardHeight = MathF.Round(Math.Clamp(cardWidth / shape, 36 * scale, 96 * scale));
        float padding = 12 * scale;
        float paddingY = (dragMode ? 12 : 14) * scale; // Windows' flyout is a little taller at the top and bottom
        float spacing = 13 * scale;
        int width = (int)MathF.Round((padding * 2) + (columns * cardWidth) + ((columns - 1) * spacing));
        int height = (int)MathF.Round((paddingY * 2) + (rows * cardHeight) + ((rows - 1) * spacing));

        int left;
        int top;
        if (dragMode)
        {
            left = work.Left + ((work.Width - width) / 2);
            top = work.Top + (int)(10 * scale);
        }
        else
        {
            left = Math.Clamp(anchor.X - (width / 2), work.Left + 8, work.Right - width - 8);
            top = Math.Clamp(anchor.Y + (int)(18 * scale), work.Top + 8, work.Bottom - height - 8);
        }

        _hover = (-1, -1);
        _cardSize = new Vector2(cardWidth, cardHeight);
        _padding = new Vector2(padding, paddingY);
        _spacing = spacing;
        _columns = columns;
        ShowAt(new RectI(left, top, left + width, top + height), theme, scale);
    }

    /// <summary>Hit-test a screen point (drag mode feeds the dragged pointer here). Returns true over a zone.</summary>
    public bool UpdatePointer(PointI screen)
    {
        var hit = (-1, -1);
        for (int c = 0; c < _cards.Count && hit.Item1 < 0; c++)
        {
            for (int z = 0; z < _cards[c].Zones.Length; z++)
            {
                if (_cards[c].Zones[z].Contains(screen))
                {
                    hit = (c, z);
                    break;
                }
            }
        }

        SetHover(hit);
        return hit.Item1 >= 0;
    }

    public override void Hide()
    {
        SetHover((-1, -1));
        base.Hide();
    }

    protected override void Build(int width, int height)
    {
        _cards.Clear();
        int row = 0;
        for (int i = 0; i < _suggestions.Count; i++)
        {
            AddCard(_suggestions[i].Layout, row, i, _suggestions[i]);
        }

        row = _suggestions.Count > 0 ? 1 : 0;
        for (int i = 0; i < _layouts.Count; i++)
        {
            AddCard(_layouts[i], row + (i / _columns), i % _columns, null);
        }
    }

    // UPDATE: Keyboard navigation (number keys and arrows, like Windows' Win + Z) through the input hook's popup keys.
    protected override void OnPointerMoved(PointI screen)
    {
        if (!DragMode)
        {
            UpdatePointer(screen);
        }
    }

    protected override void OnPointerExited()
    {
        if (!DragMode)
        {
            SetHover((-1, -1));
        }
    }

    protected override void OnReleased(PointI screen)
    {
        if (DragMode || !UpdatePointer(screen) || _monitor is null)
        {
            return;
        }

        if (_cards[_hover.Card].Suggestion is { } suggestion)
        {
            SuggestionChosen?.Invoke(_monitor, suggestion);
        }
        else if (Hovered is { } target)
        {
            ZoneChosen?.Invoke(Window, _monitor, target.Layout, target.Zone);
        }
    }

    private Vector2 _cardSize;
    private Vector2 _padding;
    private float _spacing;
    private int _columns = 3;

    private void AddCard(LayoutDefinition layout, int row, int column, SnapSuggestion? suggestion)
    {
        float zoneGap = MathF.Max(2, Px(4.5f));
        float stroke = MathF.Max(1, MathF.Round(Scale));
        var offset = new Vector2(_padding.X + (column * (_cardSize.X + _spacing)), _padding.Y + (row * (_cardSize.Y + _spacing)));
        var area = RectI.FromSize((int)offset.X, (int)offset.Y, (int)_cardSize.X, (int)_cardSize.Y);
        var zones = ZoneMath.ComputeZones(layout, area, 1, (int)zoneGap, 0);
        var visuals = new CompositionSpriteShape[zones.Length];
        var screenZones = new RectI[zones.Length];
        for (int z = 0; z < zones.Length; z++)
        {
            var r = zones[z];
            var (zone, shape, _) = Shapes.RoundedRect(C, new Vector2(r.Left, r.Top), new Vector2(r.Width, r.Height), Px(4), ZoneFill(suggestion, z), Theme.ZoneStroke, stroke);
            Root.Children.InsertAtTop(zone);
            visuals[z] = shape;
            screenZones[z] = r.Offset(Bounds.Left, Bounds.Top);
            if (suggestion is not null && z > 0 && z < suggestion.Windows.Length)
            {
                AddIcon(suggestion.Windows[z], r);
            }
        }

        _cards.Add(new Card(layout, screenZones, visuals, suggestion));
    }

    /// <summary>The suggested app's icon in the middle of its zone, as Windows shows it.</summary>
    private void AddIcon(nint window, RectI zone)
    {
        float size = MathF.Min(Px(20), MathF.Min(zone.Width, zone.Height) - Px(6));
        if (size < 8)
        {
            return;
        }

        nint icon = WindowIcons.GetIcon(window, out bool owned);
        var surface = icon == 0 ? null : surfaces.Icon(icon, (int)size);
        if (owned && icon != 0)
        {
            DestroyIcon(icon);
        }

        if (surface is not null)
        {
            AddSurface(surface, new Vector2(MathF.Round(zone.Left + ((zone.Width - size) / 2)), MathF.Round(zone.Top + ((zone.Height - size) / 2))), new Vector2(size, size));
        }
    }

    private Windows.UI.Color ZoneFill(SnapSuggestion? suggestion, int zone) =>
        suggestion is not null && zone > 0 ? Theme.ZoneAppFill : Theme.ZoneFill;

    private void SetHover((int Card, int Zone) hit)
    {
        if (hit == _hover)
        {
            return;
        }

        if (_hover.Card >= 0)
        {
            Paint(_hover, hovered: false);
        }

        _hover = hit;
        if (hit.Card >= 0)
        {
            Paint(hit, hovered: true);
        }

        if (_monitor is not null)
        {
            // A suggestion previews where this window goes (zone 0); the other windows follow it there.
            var card = hit.Card >= 0 ? _cards[hit.Card] : null;
            ZoneHovered?.Invoke(_monitor, card?.Layout, card?.Suggestion is null ? hit.Zone : 0);
        }
    }

    /// <summary>Accent on the hovered zone — on every zone of a suggestion, which is chosen as a whole.</summary>
    private void Paint((int Card, int Zone) target, bool hovered)
    {
        var card = _cards[target.Card];
        for (int z = 0; z < card.Visuals.Length; z++)
        {
            if (card.Suggestion is null && z != target.Zone)
            {
                continue;
            }

            card.Visuals[z].FillBrush = C.CreateColorBrush(hovered ? Theme.Accent : ZoneFill(card.Suggestion, z));
            card.Visuals[z].StrokeBrush = C.CreateColorBrush(hovered ? Theme.Accent : Theme.ZoneStroke);
        }
    }

    /// <summary>
    /// The layouts Windows offers for the screen: four on screens under 1920 effective pixels wide (halves, 2/3 + 1/3,
    /// one + two stacked, quarters), six on wider ones; ultrawide and portrait screens get their own sets. The monitor's
    /// own saved layout comes first when it isn't one of them.
    /// </summary>
    private static List<LayoutDefinition> PickLayouts(MonitorZones monitor)
    {
        var work = monitor.Display.WorkArea;
        double aspect = work.Width / (double)Math.Max(1, work.Height);
        double effectiveWidth = work.Width / Math.Max(0.5, monitor.Display.Scale);
        string[] ids = aspect >= 2.1
            ? [LayoutCatalog.CenterFocus, LayoutCatalog.Thirds, LayoutCatalog.Halves, LayoutCatalog.MainStack, LayoutCatalog.Quarters, LayoutCatalog.Grid3x2]
            : aspect < 1
                ? [LayoutCatalog.Rows, LayoutCatalog.Halves, LayoutCatalog.Quarters]
                : effectiveWidth >= 1900
                    ? [LayoutCatalog.Halves, LayoutCatalog.LeftTwoThirds, LayoutCatalog.Thirds, LayoutCatalog.HalfStack, LayoutCatalog.Quarters, LayoutCatalog.CenterFocus]
                    : [LayoutCatalog.Halves, LayoutCatalog.LeftTwoThirds, LayoutCatalog.HalfStack, LayoutCatalog.Quarters];

        var layouts = ids.Select(id => LayoutCatalog.FindBuiltIn(id)!).ToList();
        if (layouts.All(l => l.Id != monitor.Layout.Id) && monitor.Layout.Zones.Count > 1)
        {
            layouts.Insert(0, monitor.Layout);
            layouts.RemoveAt(layouts.Count - 1);
        }

        return layouts;
    }

    /// <summary>
    /// Snap suggestions, like Windows': the window beside the most recent other app, and (with two) beside both stacked.
    /// Portrait screens get the window above the most recent app.
    /// </summary>
    private static List<SnapSuggestion> Suggest(nint window, MonitorZones monitor, IReadOnlyList<nint> recent)
    {
        var others = recent.Where(h => h != window).Take(2).ToArray();
        if (others.Length == 0)
        {
            return [];
        }

        var work = monitor.Display.WorkArea;
        if (work.Width < work.Height)
        {
            return [new SnapSuggestion(LayoutCatalog.FindBuiltIn(LayoutCatalog.Rows)!, [window, others[0]])];
        }

        var suggestions = new List<SnapSuggestion> { new(LayoutCatalog.FindBuiltIn(LayoutCatalog.Halves)!, [window, others[0]]) };
        if (others.Length > 1)
        {
            suggestions.Add(new SnapSuggestion(LayoutCatalog.FindBuiltIn(LayoutCatalog.HalfStack)!, [window, others[0], others[1]]));
        }

        return suggestions;
    }

    private sealed record Card(LayoutDefinition Layout, RectI[] Zones, CompositionSpriteShape[] Visuals, SnapSuggestion? Suggestion);
}
