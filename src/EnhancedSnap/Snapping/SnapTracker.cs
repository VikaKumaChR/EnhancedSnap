using System.Collections.Concurrent;
using EnhancedSnap.Core.Geometry;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Snapping;

/// <summary>Where Enhanced Snap put a window. <see cref="Frame"/> is the visible frame we actually achieved.</summary>
internal sealed record SnapRecord(
    nint Hwnd,
    string MonitorId,
    string LayoutId,
    int[] Zones,
    RectI Frame,
    bool IsFill,
    int[]? PreviousZones,
    Guid GroupId = default);

/// <summary>Visible frame size a window had before it was first snapped (restored when unsnapping).</summary>
internal readonly record struct FloatingSize(int Width, int Height, int Dpi);

internal enum WindowSnapState
{
    Floating,
    Zoned,
    Fill,
    Maximized,
}

/// <summary>
/// Snap bookkeeping. Concurrent because the keyboard-hook thread asks "is this window zoned?" to decide
/// synchronously whether to swallow Win + Arrow. No per-window WinEvent subscriptions: dead windows are
/// pruned lazily, which keeps the engine silent between user actions.
/// </summary>
internal sealed class SnapTracker
{
    private readonly ConcurrentDictionary<nint, SnapRecord> _records = new();
    private readonly ConcurrentDictionary<nint, FloatingSize> _floating = new();

    public ICollection<SnapRecord> Records => _records.Values;

    public void Set(SnapRecord record) => _records[record.Hwnd] = record;

    public void SetGroup(nint hwnd, Guid groupId)
    {
        if (_records.TryGetValue(hwnd, out var record))
        {
            _records[hwnd] = record with { GroupId = groupId };
        }
    }

    /// <summary>The window was moved by Enhanced Snap itself (divider drag, joint resize).</summary>
    public void UpdateFrame(nint hwnd, RectI frame)
    {
        if (_records.TryGetValue(hwnd, out var record))
        {
            _records[hwnd] = record with { Frame = frame };
        }
    }

    public bool TryGet(nint hwnd, out SnapRecord record) => _records.TryGetValue(hwnd, out record!);

    /// <summary>Forget the window completely (it is floating again).</summary>
    public void Remove(nint hwnd)
    {
        _records.TryRemove(hwnd, out _);
        _floating.TryRemove(hwnd, out _);
    }

    /// <summary>Remember the floating size only once: re-snapping keeps the original size.</summary>
    public void RememberFloating(nint hwnd, FloatingSize size) => _floating.TryAdd(hwnd, size);

    public bool TryGetFloating(nint hwnd, out FloatingSize size) => _floating.TryGetValue(hwnd, out size);

    public static bool IsInPlace(SnapRecord record)
    {
        if (!IsWindow(record.Hwnd) || IsIconic(record.Hwnd) || IsZoomed(record.Hwnd))
        {
            return false;
        }

        var frame = WindowPlacer.GetFrame(record.Hwnd);
        return frame is { } f && f.NearlyEquals(record.Frame, 2);
    }

    public WindowSnapState GetState(nint hwnd)
    {
        if (IsZoomed(hwnd))
        {
            return WindowSnapState.Maximized;
        }

        if (TryGet(hwnd, out var record) && IsInPlace(record))
        {
            return record.IsFill ? WindowSnapState.Fill : WindowSnapState.Zoned;
        }

        return WindowSnapState.Floating;
    }

    public void Prune()
    {
        foreach (var hwnd in _records.Keys)
        {
            if (!IsWindow(hwnd))
            {
                Remove(hwnd);
            }
        }

        foreach (var hwnd in _floating.Keys)
        {
            if (!IsWindow(hwnd))
            {
                _floating.TryRemove(hwnd, out _);
            }
        }
    }
}
