using System.Numerics;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Layouts;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Core.Settings;
using EnhancedSnap.Engine.Overlay;
using EnhancedSnap.Engine.Snapping;
using Windows.UI;
using Windows.UI.Composition;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Ui;

/// <summary>A snap suggestion: a layout with a window for each zone (zone 0 is the flyout's own window).</summary>
internal sealed record SnapSuggestion(LayoutDefinition Layout, nint[] Windows);

/// <summary>
/// Snap layouts: Enhanced Snap's replacement for Windows' maximize-button flyout (also Win + Z) and, as the pop down,
/// for the snap bar at the top of the screen while a window is dragged. Drawn like Windows' own: no card behind a
/// layout, each zone a rounded tile in ControlAltFillColorQuarternary with a 1 epx ControlStrongStroke outline, accent
/// on hover. The flyout's first row holds snap suggestions, like Windows': the window with recently used apps, whose
/// zones show the app's icon — one click snaps them all. Hovering a zone also previews it full-size on the monitor
/// (Windows doesn't).
/// <para>
/// Both offer the same sets of layouts (suggestion00), one tab each below the cards: default (default snap: what
/// Windows offers), top and bottom, and the user's own (customization). A tab shows its set when the pointer rests on it
/// for the system hover time (a pointer passing over doesn't); in the flyout a click does too, and while a window is
/// dragged the mouse wheel. The panel keeps one size for every set, so nothing under the pointer moves when the set
/// changes; a set with more layouts than fit in a row wraps into even rows.
/// </para>
/// <para>
/// The pop down behaves like Windows' snap bar: while a window is dragged towards the top it peeks — only its blank
/// bottom edge shows at the top of the screen — and it slides all the way down when the pointer reaches the top. It
/// draws in a canvas window (transparent, its own Fluent 2 shadow) that reaches from the top of the work area down past
/// the expanded panel, so the panel can slide inside it without ever reaching a display above.
/// </para>
/// </summary>
internal sealed class LayoutFlyout : PopupWindow
{
    /// <summary>How much of the pop down shows while it peeks: its blank bottom edge (native: 16 px at 150 %).</summary>
    public const float PeekVisibleEpx = 11;

    private const nuint TabTimerId = 1;
    private const nuint HideTimerId = 2;
    private const uint TabDwellFallbackMs = 300;
    private const float BarCardWidthEpx = 72;
    private const float CardSpacingEpx = 13;
    private const float TabMinWidthEpx = 64;   // a tab is its name and 12 epx either side, at least this wide (建議值)
    private const float TabPaddingEpx = 12;
    private const float TabFallbackEpx = 96;   // when the name can't be measured
    private const float TabHeightEpx = 40; // a touch target (Learn, guidelines for targeting: 7.5 mm, about 40 × 40 epx)
    private const float TabSpacingEpx = 4;
    private const float TabGapEpx = 4;
    private const float TabBottomEpx = 6;
    private const float RestTopEpx = 25; // the expanded pop down rests this far below the top (native: 38 px at 150 %)
    private const float ShadowBlurEpx = 28; // Fluent 2 elevation, high ramp ($shadow28): key blur n = 28, y n / 2 = 14
    private const float ShadowOffsetEpx = 14;
    private const float AmbientBlurEpx = 8;     // …and the ambient layer: blur 8 in light,
    private const float AmbientBlurDarkEpx = 2; // 2 in dark

    private readonly DrawingSurfaces _surfaces;
    private readonly bool _bar;
    private readonly List<Card> _cards = [];
    private readonly List<Tab> _tabs = [];
    private (int Card, int Zone) _hover = (-1, -1);
    private int _tabHover = -1;
    private PointI _pointer;
    private IReadOnlyList<LayoutDefinition> _layouts = [];
    private IReadOnlyList<SnapBarGroup> _sets = [];
    private int _active;
    private IReadOnlyList<SnapSuggestion> _suggestions = [];
    private ContainerVisual? _host;
    private ContainerVisual? _cardLayer;
    private ContainerVisual? _panel;
    private ContainerVisual? _content; // the pop down's cards and tabs: hidden while it only peeks
    private ShapeVisual? _indicator;
    private Vector2 _cardSize;
    private Vector2 _padding;
    private Vector2 _panelSize;
    private float _spacing;
    private int _columns = 3;
    private int _fit = SnapBarCatalog.MaxColumns;
    private RectI _cardArea;
    private float _tabTop;
    private PointI _origin;
    private float _margin;
    private float _peekY;
    private float _hiddenY;
    private float _restY;
    private bool _hiding;
    private uint _dwellMs = TabDwellFallbackMs;
    private float[] _tabWidths = [];

    /// <param name="snapBar">The pop down at the top while a window is dragged; otherwise the maximize-button flyout.</param>
    public LayoutFlyout(CompositionHost host, DrawingSurfaces surfaces, bool snapBar = false)
        : base(host, canvas: snapBar)
    {
        _surfaces = surfaces;
        _bar = snapBar;
    }

    /// <summary>Raised when the hovered zone changes (layout null = none).</summary>
    public event Action<MonitorZones, LayoutDefinition?, int>? ZoneHovered;

    /// <summary>Raised when a zone is clicked (the flyout; in the pop down the drop decides).</summary>
    public event Action<nint, MonitorZones, LayoutDefinition, int>? ZoneChosen;

    /// <summary>Raised when a snap suggestion is clicked.</summary>
    public event Action<MonitorZones, SnapSuggestion>? SuggestionChosen;

    /// <summary>Another set shows (how: "hover", "click", "wheel" or "test").</summary>
    public event Action<SnapBarSet, string>? SetChanged;

    public nint Window { get; private set; }

    /// <summary>The pop down shown while a window is dragged (not the maximize-button flyout).</summary>
    public bool DragMode => _bar;

    /// <summary>Anchor point (maximize button) the flyout belongs to; used for hover dismissal.</summary>
    public PointI Anchor { get; private set; }

    public MonitorZones? Monitor { get; private set; }

    /// <summary>All the way down, so its zones and tabs take the pointer (the flyout always is).</summary>
    public bool IsExpanded { get; private set; }

    /// <summary>On screen and not on its way out.</summary>
    public bool IsShown => IsVisible && !_hiding;

    /// <summary>The pop down shows only its bottom edge at the top of the screen.</summary>
    public bool IsPeeking => _bar && IsShown && !IsExpanded;

    /// <summary>The panel on screen; for the pop down, where it is when expanded.</summary>
    public RectI PanelBounds => RectI.FromSize(_origin.X, _origin.Y, (int)_panelSize.X, (int)_panelSize.Y);

    public (LayoutDefinition Layout, int Zone)? Hovered =>
        _hover.Card >= 0 && _cards[_hover.Card].Suggestion is null ? (_cards[_hover.Card].Layout, _hover.Zone) : null;

    /// <summary>The set on show.</summary>
    public SnapBarSet? ActiveSet => _sets.Count > 0 ? _sets[_active].Set : null;

    /// <summary>How many sets it offers (one tab each when more than one).</summary>
    public int SetCount => _sets.Count;

    /// <summary>Test probes: the suggestions on show.</summary>
    public SnapSuggestion? FirstSuggestion => _suggestions.ElementAtOrDefault(0);

    public SnapSuggestion? SecondSuggestion => _suggestions.ElementAtOrDefault(1);

    /// <summary>The maximize-button / Win + Z flyout: snap suggestions, then the active set's layouts; sets as tabs.</summary>
    /// <param name="recent">Other windows, most recent first: the apps snap suggestions offer.</param>
    public void ShowFor(nint window, MonitorZones monitor, PointI anchor, FluentTheme theme, IReadOnlyList<nint> recent,
        IReadOnlyList<SnapBarGroup> sets, int active)
    {
        Begin(window, monitor, anchor, sets, active);
        _suggestions = Suggest(window, monitor, recent);

        float scale = (float)monitor.Display.Scale;
        var work = monitor.Display.WorkArea;
        double aspect = Aspect(work);
        _cardSize = CardSize(96 * scale, aspect, scale);
        float padding = 12 * scale;
        float paddingY = 14 * scale; // Windows' flyout is a little taller at the top and bottom
        float spacing = CardSpacingEpx * scale;

        // One size for every set, the largest: switching sets moves nothing under the pointer. Two columns, like Windows'
        // (three on ultrawides), and more when the largest set (a custom set holds up to 12) would be taller than the
        // work area.
        int most = Math.Max(1, _sets.Count > 0 ? _sets.Max(static s => s.Layouts.Count) : _layouts.Count);
        bool tabs = _sets.Count > 1;
        float chrome = paddingY + (tabs ? (TabGapEpx + TabHeightEpx + TabBottomEpx) * scale : paddingY);
        int columns = Math.Min(aspect >= 2.1 ? 3 : 2, most);
        int fitWide = Math.Max(1, (int)((work.Width - 16 - (2 * padding) + spacing) / (_cardSize.X + spacing)));
        int mostColumns = Math.Min(Math.Min(most, SnapBarCatalog.MaxColumns), fitWide); // never wider than the work area
        int rows;
        float gridHeight;
        while (true)
        {
            rows = (int)Math.Ceiling(most / (double)columns) + (_suggestions.Count > 0 ? 1 : 0);
            gridHeight = (rows * _cardSize.Y) + ((rows - 1) * spacing);
            if (columns >= mostColumns || chrome + gridHeight <= work.Height - 16)
            {
                break; // fits, or as wide as it can be: then the top stays on screen and only the bottom may run over
            }

            columns++;
        }

        float gridWidth = (columns * _cardSize.X) + ((columns - 1) * spacing);
        int width = (int)MathF.Round((padding * 2) + MathF.Max(gridWidth, tabs ? TabsWidth(scale) : 0));
        int height = (int)MathF.Round(chrome + gridHeight);
        int left = Math.Clamp(anchor.X - (width / 2), work.Left + 8, Math.Max(work.Left + 8, work.Right - width - 8));
        int top = Math.Clamp(anchor.Y + (int)(18 * scale), work.Top + 8, Math.Max(work.Top + 8, work.Bottom - height - 8));

        _padding = new Vector2(padding, paddingY);
        _spacing = spacing;
        _columns = columns;
        _panelSize = new Vector2(width, height);
        _tabTop = MathF.Round(paddingY + gridHeight + (TabGapEpx * scale));
        _origin = new PointI(left, top);
        _margin = 0;
        IsExpanded = true;
        ShowAt(new RectI(left, top, left + width, top + height), theme, scale);
    }

    /// <summary>The pop down at the top while a window is dragged: peeking, or expanded right away.</summary>
    public void ShowBar(nint window, MonitorZones monitor, FluentTheme theme, IReadOnlyList<SnapBarGroup> sets, int active, bool expand)
    {
        var bounds = monitor.Display.Bounds;
        Begin(window, monitor, new PointI(bounds.Center.X, bounds.Top), sets, active);
        _suggestions = [];

        float scale = (float)monitor.Display.Scale;
        var work = monitor.Display.WorkArea;
        _cardSize = CardSize(BarCardWidthEpx * scale, Aspect(work), scale);
        float padding = 12 * scale;
        float paddingY = 12 * scale;
        float spacing = CardSpacingEpx * scale;

        // One size for every set, the widest and the tallest: switching sets moves nothing under the pointer.
        _fit = SnapBarCatalog.FitColumns((work.Width / scale) - 32 - (2 * 12), BarCardWidthEpx, CardSpacingEpx);
        int columns = 1;
        int rows = 1;
        foreach (int count in _sets.Count > 0 ? _sets.Select(static s => s.Layouts.Count) : [_layouts.Count])
        {
            var grid = SnapBarCatalog.Grid(count, _fit);
            columns = Math.Max(columns, grid.Columns);
            rows = Math.Max(rows, grid.Rows);
        }

        bool tabs = _sets.Count > 1;
        float cardsWidth = (columns * _cardSize.X) + ((columns - 1) * spacing);
        float cardsHeight = (rows * _cardSize.Y) + ((rows - 1) * spacing);
        float width = MathF.Round((padding * 2) + MathF.Max(cardsWidth, tabs ? TabsWidth(scale) : 0));
        float height = MathF.Round(paddingY + cardsHeight + (tabs ? (TabGapEpx + TabHeightEpx + TabBottomEpx) * scale : paddingY));

        _padding = new Vector2(padding, paddingY);
        _spacing = spacing;
        _columns = columns;
        _panelSize = new Vector2(width, height);
        _cardArea = RectI.FromSize((int)MathF.Round((width - cardsWidth) / 2), (int)MathF.Round(paddingY), (int)MathF.Round(cardsWidth), (int)MathF.Round(cardsHeight));
        _tabTop = MathF.Round(paddingY + cardsHeight + (TabGapEpx * scale));

        // The canvas: from the top of the work area down past the expanded panel, with room for the shadow around it.
        float shadowReach = MathF.Ceiling((ShadowBlurEpx + ShadowOffsetEpx) * scale);
        _margin = MathF.Ceiling(ShadowBlurEpx * scale);
        _restY = MathF.Round(RestTopEpx * scale);
        _peekY = MathF.Round(PeekVisibleEpx * scale) - height;
        _hiddenY = -height - shadowReach;
        int panelLeft = work.Left + (int)MathF.Round((work.Width - width) / 2);
        int left = panelLeft - (int)_margin;
        int top = work.Top;
        int canvasRight = left + (int)(width + (2 * _margin));
        int canvasBottom = top + (int)(_restY + height + shadowReach);
        _origin = new PointI(panelLeft, top + (int)_restY);
        IsExpanded = expand;
        ShowAt(new RectI(left, top, canvasRight, canvasBottom), theme, scale);
        MovePanel(expand ? _restY : _peekY, expand ? FluentMotion.Expand : FluentMotion.Enter, FluentMotion.Decelerate);
    }

    /// <summary>Peeking → all the way down: the pointer reached the top. "Direct Entrance", 250 ms (Learn, Motion).</summary>
    public void Expand()
    {
        if (!_bar || !IsShown || IsExpanded)
        {
            return;
        }

        IsExpanded = true;
        MovePanel(_restY, FluentMotion.Expand, FluentMotion.Decelerate);
        FadeContent(1f, FluentMotion.Expand);
    }

    /// <summary>Expanded → peeking again: the pointer moved away, but not far. An existing element moving, 167 ms.</summary>
    public void Collapse()
    {
        if (!_bar || !IsShown || !IsExpanded)
        {
            return;
        }

        IsExpanded = false;
        ForgetPointer();
        MovePanel(_peekY, FluentMotion.Enter, FluentMotion.Reposition);
        FadeContent(0f, FluentMotion.Enter);
    }

    /// <summary>The pop down slides up out of view and fades ("Direct Exit": 167 ms, always with a fade), then hides.</summary>
    public void HideAnimated()
    {
        if (!IsVisible || _hiding)
        {
            return;
        }

        if (!_bar || !Theme.Animate)
        {
            Hide();
            return;
        }

        _hiding = true;
        IsExpanded = false;
        ForgetPointer();
        MovePanel(_hiddenY, FluentMotion.Enter, FluentMotion.Decelerate, opacity: 0f);
        SetTimer(Hwnd, HideTimerId, (uint)FluentMotion.Enter.TotalMilliseconds + 20, 0);
    }

    /// <summary>Hit-test a screen point (the pop down is fed the dragged pointer). Returns true over a zone.</summary>
    public bool UpdatePointer(PointI screen)
    {
        _pointer = screen;
        if (!IsExpanded || _hiding)
        {
            SetTabHover(-1);
            SetHover((-1, -1));
            return false;
        }

        int tab = -1;
        for (int t = 0; t < _tabs.Count; t++)
        {
            if (_tabs[t].Rect.Contains(screen))
            {
                tab = t;
                break;
            }
        }

        SetTabHover(tab);
        var hit = (-1, -1);
        for (int c = 0; tab < 0 && c < _cards.Count && hit.Item1 < 0; c++)
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

    /// <summary>The mouse wheel turned while the pop down is up: the next (+1) or previous (−1) set, round and round.</summary>
    public void CycleSet(int step, string how = "wheel")
    {
        if (_sets.Count > 1)
        {
            SwitchTo((((_active + step) % _sets.Count) + _sets.Count) % _sets.Count, how);
        }
    }

    /// <summary>Test probe: a tab's centre on screen.</summary>
    public PointI? TabCenter(int index) => index >= 0 && index < _tabs.Count ? _tabs[index].Rect.Center : null;

    /// <summary>Test probe: the centre of a zone of a card on screen.</summary>
    public PointI? ZoneCenter(int card, int zone) =>
        card >= 0 && card < _cards.Count && zone >= 0 && zone < _cards[card].Zones.Length ? _cards[card].Zones[zone].Center : null;

    /// <summary>For the log: the set on show, its layouts and the other sets.</summary>
    public string Describe() =>
        $"{(ActiveSet?.ToString() ?? "layouts")} [{string.Join(", ", _layouts.Select(static l => l.Id))}]"
        + (_sets.Count > 1 ? $" of {string.Join("/", _sets.Select(static s => s.Set))}" : string.Empty);

    /// <summary>Hides at once. Quietly: whoever hides it also clears the zone preview.</summary>
    public override void Hide()
    {
        KillTimer(Hwnd, TabTimerId);
        KillTimer(Hwnd, HideTimerId);
        _hiding = false;
        IsExpanded = false;
        _hover = (-1, -1);
        _tabHover = -1;
        base.Hide();
    }

    protected override void Build(int width, int height)
    {
        _cards.Clear();
        _tabs.Clear();
        _indicator = null;
        _panel = null;
        _content = null;
        _host = Root;
        if (_bar)
        {
            _panel = C.CreateContainerVisual();
            _panel.Size = _panelSize;
            _panel.Offset = new Vector3(_margin, _hiddenY, 0);
            _panel.Opacity = Theme.Animate ? 0f : 1f; // it fades in as it slides in
            Root.Children.InsertAtTop(_panel);
            BuildPanelSurface(_panel);

            // While it peeks, its edge is blank, like Windows' own: the cards and tabs show as it comes down.
            _content = C.CreateContainerVisual();
            _content.Size = _panelSize;
            _content.Opacity = IsExpanded ? 1f : 0f;
            _panel.Children.InsertAtTop(_content);
            _host = _content;
        }

        _cardLayer = C.CreateContainerVisual();
        _cardLayer.Size = _panelSize;
        _host.Children.InsertAtTop(_cardLayer);
        BuildCards();
        BuildTabs();
    }

    // UPDATE: Keyboard navigation (number keys and arrows, like Windows' Win + Z) through the input hook's popup keys.
    protected override void OnPointerMoved(PointI screen)
    {
        if (!_bar)
        {
            UpdatePointer(screen);
        }
    }

    protected override void OnPointerExited()
    {
        if (!_bar)
        {
            SetTabHover(-1);
            SetHover((-1, -1));
        }
    }

    protected override void OnReleased(PointI screen)
    {
        if (_bar || Monitor is null)
        {
            return;
        }

        for (int t = 0; t < _tabs.Count; t++)
        {
            if (_tabs[t].Rect.Contains(screen))
            {
                SwitchTo(t, "click");
                return;
            }
        }

        if (!UpdatePointer(screen))
        {
            return;
        }

        if (_cards[_hover.Card].Suggestion is { } suggestion)
        {
            SuggestionChosen?.Invoke(Monitor, suggestion);
        }
        else if (Hovered is { } target)
        {
            ZoneChosen?.Invoke(Window, Monitor, target.Layout, target.Zone);
        }
    }

    /// <summary>The pointer rested on a tab for the hover time: show its set. The pop down's exit finished: hide.</summary>
    protected override void OnTimer(nuint id)
    {
        KillTimer(Hwnd, id);
        if (id == TabTimerId && _tabHover >= 0 && _tabHover != _active)
        {
            SwitchTo(_tabHover, "hover");
        }
        else if (id == HideTimerId && _hiding)
        {
            Hide();
        }
    }

    private static double Aspect(RectI work) => Math.Clamp(work.Width / (double)Math.Max(1, work.Height), 0.5, 3.6);

    /// <summary>Windows' cards are about 3:2 on ordinary screens; ultrawide and portrait screens keep their own shape.</summary>
    private static Vector2 CardSize(float cardWidth, double aspect, float scale)
    {
        float shape = aspect >= 2.1 || aspect < 1 ? (float)aspect : MathF.Min((float)aspect, 1.55f);
        return new Vector2(cardWidth, MathF.Round(Math.Clamp(cardWidth / shape, 36 * scale, 96 * scale)));
    }

    private void Begin(nint window, MonitorZones monitor, PointI anchor, IReadOnlyList<SnapBarGroup> sets, int active)
    {
        KillTimer(Hwnd, TabTimerId);
        KillTimer(Hwnd, HideTimerId);
        _hiding = false;
        Window = window;
        Monitor = monitor;
        Anchor = anchor;
        _sets = sets;
        _active = _sets.Count > 0 ? Math.Clamp(active, 0, _sets.Count - 1) : 0;
        var work = monitor.Display.WorkArea;
        _layouts = _sets.Count > 0 ? _sets[_active].Layouts : SnapBarCatalog.Classic(work.Width, work.Height, monitor.Display.Scale, monitor.Layout);
        _hover = (-1, -1);
        _tabHover = -1;
        _pointer = anchor;
        _dwellMs = MouseHoverTimeMs(TabDwellFallbackMs); // as long as the user set a hover to take
        MeasureTabs((float)monitor.Display.Scale);
    }

    /// <summary>Each tab as wide as its name (measured: names differ in length by language) and its padding.</summary>
    private void MeasureTabs(float scale)
    {
        _tabWidths = new float[_sets.Count > 1 ? _sets.Count : 0];
        for (int i = 0; i < _tabWidths.Length; i++)
        {
            float text = _surfaces.MeasureWidth(Name(_sets[i].Set), MathF.Round(12 * scale), semibold: true) ?? (TabFallbackEpx - (2 * TabPaddingEpx)) * scale;
            _tabWidths[i] = MathF.Ceiling(MathF.Max(TabMinWidthEpx * scale, text + (2 * TabPaddingEpx * scale)));
        }
    }

    private float TabsWidth(float scale) => _tabWidths.Sum() + (Math.Max(0, _tabWidths.Length - 1) * TabSpacingEpx * scale);

    private void ForgetPointer()
    {
        KillTimer(Hwnd, TabTimerId);
        _tabHover = -1;
        PaintTabs();
        SetHover((-1, -1));
    }

    /// <summary>
    /// The pop down's own panel — its window is a transparent canvas — with what DWM draws for other popups: the acrylic
    /// or solid surface with an 8 epx radius (Learn, Geometry: flyouts) and its stroke, and a shadow, here Fluent 2's
    /// elevation 28 (fluent2.microsoft.design/elevation: the high elevation ramp, $shadow28, which Fluent gives bottom
    /// sheets, side navigation and raised tab bars — panels at a screen edge, like this one): a key layer (blur 28, y 14;
    /// 24 % light, 28 % dark) and an ambient layer (y 0; blur 8 and 20 % light, blur 2 and 20 % dark). The shadows are
    /// cast by opaque sprites under the surface, so the acrylic never has to render inside an offscreen layer. None in
    /// high contrast, which outlines with the stroke.
    /// </summary>
    private void BuildPanelSurface(ContainerVisual panel)
    {
        float radius = Px(8);
        if (!Theme.HighContrast)
        {
            AddShadowCaster(panel, radius, Theme.Dark ? AmbientBlurDarkEpx : AmbientBlurEpx, 0, 0.20f);
            AddShadowCaster(panel, radius, ShadowBlurEpx, ShadowOffsetEpx, Theme.Dark ? 0.28f : 0.24f);
        }

        if (Theme.Acrylic)
        {
            var backdrop = C.CreateSpriteVisual();
            backdrop.Brush = C.CreateHostBackdropBrush();
            backdrop.Size = _panelSize;
            var outline = C.CreateRoundedRectangleGeometry();
            outline.Size = _panelSize;
            outline.CornerRadius = new Vector2(radius);
            backdrop.Clip = C.CreateGeometricClip(outline);
            panel.Children.InsertAtTop(backdrop);
        }

        var (surface, _, _) = Shapes.RoundedRect(C, Vector2.Zero, _panelSize, radius, Theme.PanelTint, Theme.PanelStroke, MathF.Max(1, Px(1)));
        panel.Children.InsertAtTop(surface);
    }

    /// <summary>
    /// One layer of the panel's shadow. The caster is a plain rectangle inset just enough that its corners touch the
    /// panel's rounded corners (r × (1 − 1/√2)), so it never shows past them; the blur rounds the shadow anyway. Its
    /// strength is the colour's alpha. (A LayerVisual holding the rounded shape cast a shadow about four times too dark and
    /// cut off on one side in this transparent window.)
    /// </summary>
    private void AddShadowCaster(ContainerVisual panel, float radius, float blurEpx, float offsetEpx, float alpha)
    {
        float inset = MathF.Ceiling(radius * (1 - (1 / MathF.Sqrt(2))));
        var caster = C.CreateSpriteVisual();
        caster.Offset = new Vector3(inset, inset, 0);
        caster.Size = Vector2.Max(Vector2.One, _panelSize - new Vector2(2 * inset));
        caster.Brush = C.CreateColorBrush(Color.FromArgb(255, Theme.PanelTint.R, Theme.PanelTint.G, Theme.PanelTint.B));
        var shadow = C.CreateDropShadow();
        shadow.BlurRadius = Px(blurEpx);
        shadow.Offset = new Vector3(0, Px(offsetEpx), 0);
        shadow.Color = Color.FromArgb((byte)MathF.Round(255 * alpha), 0, 0, 0);
        caster.Shadow = shadow;
        panel.Children.InsertAtTop(caster);
    }

    /// <summary>Shows or hides the pop down's cards and tabs (peeking: blank edge), at once when animations are off.</summary>
    private void FadeContent(float opacity, TimeSpan duration)
    {
        if (_content is null)
        {
            return;
        }

        if (!Theme.Animate)
        {
            _content.Opacity = opacity;
            return;
        }

        _content.StartAnimation("Opacity", Shapes.Fade(C, opacity, duration));
    }

    /// <summary>Moves the pop down's panel (and fades it): no animation when Windows' animation effects are off.</summary>
    private void MovePanel(float y, TimeSpan duration, (Vector2 C1, Vector2 C2) curve, float opacity = 1f)
    {
        if (_panel is null)
        {
            return;
        }

        var target = new Vector3(_margin, y, 0);
        if (!Theme.Animate)
        {
            _panel.Offset = target;
            _panel.Opacity = opacity;
            return;
        }

        var move = C.CreateVector3KeyFrameAnimation();
        move.InsertKeyFrame(1f, target, C.CreateCubicBezierEasingFunction(curve.C1, curve.C2));
        move.Duration = duration;
        _panel.StartAnimation("Offset", move);
        var fade = C.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, opacity);
        fade.Duration = duration;
        _panel.StartAnimation("Opacity", fade);
    }

    private void BuildCards()
    {
        if (_bar)
        {
            BuildSet();
            return;
        }

        float gridWidth = (_columns * _cardSize.X) + ((_columns - 1) * _spacing);
        float left = MathF.Round((_panelSize.X - gridWidth) / 2);
        for (int i = 0; i < _suggestions.Count; i++)
        {
            AddCard(_suggestions[i].Layout, CardOffset(left, 0, i), _suggestions[i]);
        }

        int row = _suggestions.Count > 0 ? 1 : 0;
        for (int i = 0; i < _layouts.Count; i++)
        {
            AddCard(_layouts[i], CardOffset(left, row + (i / _columns), i % _columns), null);
        }
    }

    private Vector2 CardOffset(float left, int row, int column) =>
        new(left + (column * (_cardSize.X + _spacing)), _padding.Y + (row * (_cardSize.Y + _spacing)));

    /// <summary>
    /// The pop down's active set, centred in the card area (a set with fewer cards than the widest, or a shorter last
    /// row, leaves even space on both sides).
    /// </summary>
    private void BuildSet()
    {
        var (columns, rows) = SnapBarCatalog.Grid(_layouts.Count, _fit);
        float setHeight = (rows * _cardSize.Y) + ((rows - 1) * _spacing);
        float top = _cardArea.Top + ((_cardArea.Height - setHeight) / 2);
        for (int i = 0; i < _layouts.Count; i++)
        {
            int row = i / columns;
            int inRow = Math.Min(columns, _layouts.Count - (row * columns));
            float rowWidth = (inRow * _cardSize.X) + ((inRow - 1) * _spacing);
            float left = _cardArea.Left + ((_cardArea.Width - rowWidth) / 2);
            AddCard(_layouts[i], new Vector2(MathF.Round(left + ((i % columns) * (_cardSize.X + _spacing))), MathF.Round(top + (row * (_cardSize.Y + _spacing)))), null);
        }
    }

    /// <summary>
    /// With more than one set: the sets as tabs below the cards, like WinUI's SelectorBar — the name (the active one
    /// semibold), a subtle fill under the pointer and a short accent bar under the active tab.
    /// </summary>
    private void BuildTabs()
    {
        if (_sets.Count < 2 || _host is null)
        {
            return;
        }

        float tabHeight = Px(TabHeightEpx);
        float spacing = Px(TabSpacingEpx);
        float x = MathF.Round((_panelSize.X - TabsWidth(Scale)) / 2);
        var clear = Color.FromArgb(0, 0, 0, 0);
        for (int i = 0; i < _sets.Count && i < _tabWidths.Length; i++)
        {
            float tabWidth = _tabWidths[i];
            var offset = new Vector2(MathF.Round(x), _tabTop);
            var labelSize = new Vector2(tabWidth, tabHeight - Px(6));
            var (background, shape, _) = Shapes.RoundedRect(C, offset, new Vector2(tabWidth, tabHeight), Px(4), clear, clear, MathF.Max(1, Px(1)));
            _host.Children.InsertAtTop(background);
            string name = Name(_sets[i].Set);
            int w = (int)labelSize.X;
            int h = (int)labelSize.Y;
            var normal = AddSurfaceTo(_host, _surfaces.Text(name, w, h, Px(12), false, Theme.TextSecondary, TextAlign.Center), offset, labelSize);
            var selected = AddSurfaceTo(_host, _surfaces.Text(name, w, h, Px(12), true, Theme.Text, TextAlign.Center), offset, labelSize);
            var rect = RectI.FromSize((int)offset.X, (int)offset.Y, (int)tabWidth, (int)tabHeight).Offset(_origin.X, _origin.Y);
            _tabs.Add(new Tab(rect, offset, tabWidth, shape, normal, selected));
            x += tabWidth + spacing;
        }

        if (_tabs.Count < 2)
        {
            return;
        }

        var size = new Vector2(Px(16), MathF.Max(2, Px(3)));
        (_indicator, _, _) = Shapes.RoundedRect(C, IndicatorOffset(_active, size), size, size.Y / 2, Theme.Accent, null);
        _host.Children.InsertAtTop(_indicator);
        PaintTabs();
    }

    private Vector2 IndicatorOffset(int tab, Vector2 size) =>
        new(MathF.Round(_tabs[tab].Offset.X + ((_tabs[tab].Width - size.X) / 2)), _tabs[tab].Offset.Y + Px(TabHeightEpx) - size.Y - Px(2));

    private static string Name(SnapBarSet set) => set switch
    {
        SnapBarSet.Horizontal => Strings.Current.SnapBar_Horizontal,
        SnapBarSet.Custom => Strings.Current.SnapBar_Custom,
        _ => Strings.Current.SnapBar_Classic,
    };

    private void SetTabHover(int tab)
    {
        if (tab == _tabHover)
        {
            return;
        }

        _tabHover = tab;
        KillTimer(Hwnd, TabTimerId);
        if (tab >= 0 && tab != _active)
        {
            SetTimer(Hwnd, TabTimerId, _dwellMs, 0);
        }

        PaintTabs();
    }

    /// <summary>Names (active semibold), the hover fill (an outline in high contrast).</summary>
    private void PaintTabs()
    {
        var clear = Color.FromArgb(0, 0, 0, 0);
        for (int i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            tab.Normal.IsVisible = i != _active;
            tab.Selected.IsVisible = i == _active;
            bool hovered = i == _tabHover;
            if (Theme.HighContrast)
            {
                tab.Background.StrokeBrush = C.CreateColorBrush(hovered ? Theme.Text : clear);
            }
            else
            {
                // SubtleFillColorSecondary: the hover fill of tabs and other subtle buttons.
                tab.Background.FillBrush = C.CreateColorBrush(!hovered ? clear : Theme.Dark ? Color.FromArgb(15, 255, 255, 255) : Color.FromArgb(10, 0, 0, 0));
            }
        }
    }

    /// <summary>Shows another set in place: new cards (faded in), the tabs and the accent bar follow.</summary>
    private void SwitchTo(int index, string how)
    {
        if (!IsShown || !IsExpanded || index == _active || index < 0 || index >= _sets.Count || _cardLayer is null)
        {
            return;
        }

        KillTimer(Hwnd, TabTimerId);
        SetHover((-1, -1));
        _active = index;
        _layouts = _sets[index].Layouts;
        _cardLayer.Children.RemoveAll();
        _cards.Clear();
        BuildCards();
        PaintTabs();
        if (_indicator is not null)
        {
            var target = new Vector3(IndicatorOffset(index, _indicator.Size), 0);
            if (Theme.Animate)
            {
                var slide = C.CreateVector3KeyFrameAnimation();
                slide.InsertKeyFrame(1f, target, C.CreateCubicBezierEasingFunction(FluentMotion.Decelerate.C1, FluentMotion.Decelerate.C2));
                slide.Duration = FluentMotion.Glide;
                _indicator.StartAnimation("Offset", slide);
            }
            else
            {
                _indicator.Offset = target;
            }
        }

        if (Theme.Animate)
        {
            _cardLayer.Opacity = 0f;
            _cardLayer.StartAnimation("Opacity", Shapes.Fade(C, 1f, FluentMotion.Enter));
        }

        SetChanged?.Invoke(_sets[index].Set, how);
        UpdatePointer(_pointer); // the pointer may be over a card of the new set
    }

    private void AddCard(LayoutDefinition layout, Vector2 offset, SnapSuggestion? suggestion)
    {
        float zoneGap = MathF.Max(2, Px(4.5f));
        float stroke = MathF.Max(1, MathF.Round(Scale));
        var area = RectI.FromSize((int)offset.X, (int)offset.Y, (int)_cardSize.X, (int)_cardSize.Y);
        var zones = ZoneMath.ComputeZones(layout, area, 1, (int)zoneGap, 0);
        var visuals = new CompositionSpriteShape[zones.Length];
        var screenZones = new RectI[zones.Length];
        for (int z = 0; z < zones.Length; z++)
        {
            var r = zones[z];
            var (zone, shape, _) = Shapes.RoundedRect(C, new Vector2(r.Left, r.Top), new Vector2(r.Width, r.Height), Px(4), ZoneFill(suggestion, z), Theme.ZoneStroke, stroke);
            _cardLayer!.Children.InsertAtTop(zone);
            visuals[z] = shape;
            screenZones[z] = r.Offset(_origin.X, _origin.Y);
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
        var surface = icon == 0 ? null : _surfaces.Icon(icon, (int)size);
        if (owned && icon != 0)
        {
            DestroyIcon(icon);
        }

        if (surface is not null)
        {
            AddSurfaceTo(_cardLayer!, surface, new Vector2(MathF.Round(zone.Left + ((zone.Width - size) / 2)), MathF.Round(zone.Top + ((zone.Height - size) / 2))), new Vector2(size, size));
        }
    }

    private Color ZoneFill(SnapSuggestion? suggestion, int zone) =>
        suggestion is not null && zone > 0 ? Theme.ZoneAppFill : Theme.ZoneFill;

    private void SetHover((int Card, int Zone) hit)
    {
        if (hit == _hover)
        {
            return;
        }

        if (_hover.Card >= 0 && _hover.Card < _cards.Count)
        {
            Paint(_hover, hovered: false);
        }

        _hover = hit;
        if (hit.Card >= 0)
        {
            Paint(hit, hovered: true);
        }

        if (Monitor is not null)
        {
            // A suggestion previews where this window goes (zone 0); the other windows follow it there.
            var card = hit.Card >= 0 ? _cards[hit.Card] : null;
            ZoneHovered?.Invoke(Monitor, card?.Layout, card?.Suggestion is null ? hit.Zone : 0);
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

    private sealed record Tab(RectI Rect, Vector2 Offset, float Width, CompositionSpriteShape Background, SpriteVisual Normal, SpriteVisual Selected);
}
