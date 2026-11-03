using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Engine.Snapping;
using Windows.System;

namespace EnhancedSnap.Engine.Overlay;

/// <summary>
/// Creates overlays lazily (a user who never drags with zones never pays for them), keeps them in sync
/// with monitors/palette, and routes show/target/hide calls to the right monitor.
/// </summary>
internal sealed class OverlayManager : IDisposable
{
    private readonly CompositionHost _host;
    private readonly Dictionary<string, ZoneOverlay> _overlays = [];
    private IReadOnlyList<MonitorZones> _monitors = [];
    private OverlayPalette? _palette;
    private DispatcherQueueTimer? _previewTimer;

    public OverlayManager(CompositionHost host)
    {
        _host = host;
    }

    public bool Animate => _palette?.Animate ?? false;

    public bool IsPreviewing { get; private set; }

    /// <summary>Apply new monitors/zones/palette. Existing overlays are updated in place; stale ones destroyed.</summary>
    public void Sync(IReadOnlyList<MonitorZones> monitors, OverlayPalette palette)
    {
        _monitors = monitors;
        _palette = palette;
        foreach (var id in _overlays.Keys.ToList())
        {
            var monitor = monitors.FirstOrDefault(m => m.Id == id);
            if (monitor is null || monitor.Display.Handle != _overlays[id].Monitor.Display.Handle)
            {
                _overlays[id].Dispose();
                _overlays.Remove(id);
            }
            else
            {
                _overlays[id].Update(monitor, palette);
            }
        }
    }

    /// <summary>Only the drop target (no zone tiles), in Windows Snap's neutral style: edges, a group's empty space, flyout previews.</summary>
    public void ShowTarget(MonitorZones monitor, RectI? target, nint insertAfter)
    {
        StopPreview();
        foreach (var (id, other) in _overlays)
        {
            if (id != monitor.Id)
            {
                other.Hide(Animate);
            }
        }

        if (target is null)
        {
            if (_overlays.TryGetValue(monitor.Id, out var current))
            {
                current.Hide(Animate);
            }

            return;
        }

        var overlay = Get(monitor);
        if (overlay is null)
        {
            return;
        }

        overlay.SetTilesVisible(false);
        overlay.SetNeutralTarget(true);
        overlay.Show(insertAfter, Animate);
        overlay.SetTarget(target, Animate);
    }

    public void ShowFor(MonitorZones active, bool allMonitors, nint draggedWindow)
    {
        StopPreview();
        foreach (var monitor in _monitors)
        {
            if (monitor.Id == active.Id || allMonitors)
            {
                var overlay = Get(monitor);
                overlay?.SetTilesVisible(true);
                overlay?.SetNeutralTarget(false);
                overlay?.Show(draggedWindow, Animate);
            }
            else if (_overlays.TryGetValue(monitor.Id, out var other))
            {
                other.SetTarget(null, animate: false);
                other.Hide(Animate);
            }
        }
    }

    public void SetTarget(MonitorZones? monitor, RectI? target)
    {
        foreach (var (id, overlay) in _overlays)
        {
            overlay.SetTarget(monitor is not null && id == monitor.Id ? target : null, Animate);
        }
    }

    public void HideAll(bool animate)
    {
        foreach (var overlay in _overlays.Values)
        {
            overlay.Hide(animate && Animate);
        }
    }

    /// <summary>Flash the zones of one monitor (null = all), e.g. when a layout is picked in Settings.</summary>
    public void Preview(MonitorZones? only, TimeSpan duration, nint insertAfter = 0, RectI? target = null)
    {
        StopPreview();
        foreach (var monitor in _monitors)
        {
            if (only is not null && monitor.Id != only.Id)
            {
                continue;
            }

            var overlay = Get(monitor);
            overlay?.SetTilesVisible(true);
            overlay?.SetNeutralTarget(false);
            overlay?.Show(insertAfter, Animate);
            overlay?.SetTarget(target, animate: false);
        }

        IsPreviewing = true;
        _previewTimer ??= CreatePreviewTimer();
        _previewTimer.Interval = duration;
        _previewTimer.Start();
    }

    public void Dispose()
    {
        _previewTimer?.Stop();
        foreach (var overlay in _overlays.Values)
        {
            overlay.Dispose();
        }

        _overlays.Clear();
    }

    private void StopPreview()
    {
        if (IsPreviewing)
        {
            _previewTimer?.Stop();
            IsPreviewing = false;
        }
    }

    private DispatcherQueueTimer CreatePreviewTimer()
    {
        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            IsPreviewing = false;
            HideAll(animate: true);
        };
        return timer;
    }

    private ZoneOverlay? Get(MonitorZones monitor)
    {
        if (_overlays.TryGetValue(monitor.Id, out var overlay))
        {
            return overlay;
        }

        if (_palette is null)
        {
            return null;
        }

        try
        {
            overlay = new ZoneOverlay(_host, monitor, _palette);
            _overlays[monitor.Id] = overlay;
            return overlay;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not create the overlay for {monitor.Display.GdiDeviceName}", ex);
            return null;
        }
    }
}
