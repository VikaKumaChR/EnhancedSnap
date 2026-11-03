using EnhancedSnap.Core.Displays;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Interop;
using EnhancedSnap.Core.Layouts;
using EnhancedSnap.Core.Settings;

namespace EnhancedSnap.Engine.Snapping;

/// <summary>A monitor with its resolved layout and zone rectangles (physical pixels, screen coordinates).</summary>
internal sealed class MonitorZones(DisplayInfo display, LayoutDefinition layout, RectI[] zones)
{
    public DisplayInfo Display { get; } = display;

    public LayoutDefinition Layout { get; } = layout;

    public RectI[] Zones { get; } = zones;

    public string Id => Display.Id;
}

/// <summary>Monitors and their zones. Rebuilt on settings, display or work-area changes — never polled.</summary>
internal sealed class MonitorModel
{
    private List<MonitorZones> _monitors = [];

    public IReadOnlyList<MonitorZones> Monitors => _monitors;

    public void Rebuild(AppSettings settings)
    {
        var displays = DisplayEnumerator.GetDisplays();
        _monitors = displays.Select(d =>
        {
            var layout = LayoutCatalog.ResolveForMonitor(settings, d.Id, d.WorkArea.Width, d.WorkArea.Height);
            var zones = ZoneMath.ComputeZones(layout, d.WorkArea, d.Scale, settings.Gap, settings.Margin);
            return new MonitorZones(d, layout, zones);
        }).ToList();
    }

    public MonitorZones? FindById(string id) => _monitors.FirstOrDefault(m => m.Id == id);

    public MonitorZones? FromHandle(nint monitor) => _monitors.FirstOrDefault(m => m.Display.Handle == monitor);

    public MonitorZones? FromPoint(PointI point) =>
        FromHandle(Win32.MonitorFromPoint(new POINT(point.X, point.Y), Win32.MONITOR_DEFAULTTONEAREST)) ?? Nearest(point);

    public MonitorZones? FromWindow(nint hwnd) =>
        FromHandle(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST)) ?? _monitors.FirstOrDefault();

    public MonitorZones? FromRect(RectI rect) =>
        FromHandle(Win32.MonitorFromRect(RECT.From(rect), Win32.MONITOR_DEFAULTTONEAREST)) ?? Nearest(rect.Center);

    /// <summary>Adjacent monitor in a direction (for Win + Arrow at the edge of a display).</summary>
    public MonitorZones? Neighbor(MonitorZones current, Direction direction)
    {
        var bounds = _monitors.Select(static m => m.Display.Bounds).ToList();
        int index = ZoneMath.FindNeighbor(bounds, current.Display.Bounds, direction, tolerance: 8);
        return index >= 0 ? _monitors[index] : null;
    }

    private MonitorZones? Nearest(PointI point) =>
        _monitors.OrderBy(m => m.Display.Bounds.DistanceSquaredTo(point)).FirstOrDefault();
}
