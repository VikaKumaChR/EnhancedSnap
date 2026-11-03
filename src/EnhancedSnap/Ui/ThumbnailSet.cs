using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Interop;
using EnhancedSnap.Engine.Native;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Ui;

/// <summary>
/// Live window previews via DWM thumbnails: DWM composes the source window into our popup on the GPU,
/// so previews stay live at no CPU cost to us or to the source app. Unregistered as soon as a popup hides.
/// </summary>
internal sealed unsafe class ThumbnailSet : IDisposable
{
    private readonly List<nint> _thumbnails = [];

    /// <summary>Shows <paramref name="source"/> inside <paramref name="area"/> (destination client pixels), aspect-fitted.
    /// Returns false for minimized windows (callers show the app icon instead).</summary>
    public bool Add(nint destination, nint source, RectI area)
    {
        if (IsIconic(source) || area.IsEmpty)
        {
            return false;
        }

        if (DwmRegisterThumbnail(destination, source, out nint thumbnail) != 0)
        {
            return false;
        }

        DwmQueryThumbnailSourceSize(thumbnail, out POINT size);
        var target = Fit(area, size.X, size.Y);
        var properties = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = RECT.From(target),
            opacity = 255,
            fVisible = 1,
            fSourceClientAreaOnly = 0,
        };
        DwmUpdateThumbnailProperties(thumbnail, &properties);
        _thumbnails.Add(thumbnail);
        return true;
    }

    public void Clear()
    {
        foreach (nint thumbnail in _thumbnails)
        {
            DwmUnregisterThumbnail(thumbnail);
        }

        _thumbnails.Clear();
    }

    public void Dispose() => Clear();

    private static RectI Fit(RectI area, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return area;
        }

        double scale = Math.Min(area.Width / (double)width, area.Height / (double)height);
        int w = (int)Math.Round(width * scale);
        int h = (int)Math.Round(height * scale);
        int left = area.Left + ((area.Width - w) / 2);
        int top = area.Top + ((area.Height - h) / 2);
        return new RectI(left, top, left + w, top + h);
    }
}
