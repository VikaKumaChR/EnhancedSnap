namespace EnhancedSnap.Core.Geometry;

/// <summary>Integer point in physical (device) pixels.</summary>
public readonly record struct PointI(int X, int Y);

/// <summary>Integer rectangle in physical pixels using Win32 semantics (Right/Bottom exclusive).</summary>
public readonly record struct RectI(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public PointI Center => new(Left + Width / 2, Top + Height / 2);

    public double CenterX => Left + Width / 2.0;

    public double CenterY => Top + Height / 2.0;

    public static RectI FromSize(int x, int y, int width, int height) => new(x, y, x + width, y + height);

    public bool Contains(PointI p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

    /// <summary>True when the interiors overlap (touching edges do not count).</summary>
    public bool Overlaps(RectI other) =>
        Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

    public RectI Union(RectI other) => new(
        Math.Min(Left, other.Left),
        Math.Min(Top, other.Top),
        Math.Max(Right, other.Right),
        Math.Max(Bottom, other.Bottom));

    public RectI Intersect(RectI other)
    {
        var r = new RectI(
            Math.Max(Left, other.Left),
            Math.Max(Top, other.Top),
            Math.Min(Right, other.Right),
            Math.Min(Bottom, other.Bottom));
        return r.IsEmpty ? default : r;
    }

    public long Area => IsEmpty ? 0 : (long)Width * Height;

    public RectI Offset(int dx, int dy) => new(Left + dx, Top + dy, Right + dx, Bottom + dy);

    public RectI Inflate(int left, int top, int right, int bottom) =>
        new(Left - left, Top - top, Right + right, Bottom + bottom);

    public RectI Deflate(int all) => new(Left + all, Top + all, Right - all, Bottom - all);

    /// <summary>Squared distance from a point to the closest point of the rectangle (0 when inside).</summary>
    public long DistanceSquaredTo(PointI p)
    {
        long dx = p.X < Left ? Left - p.X : p.X >= Right ? p.X - (Right - 1) : 0;
        long dy = p.Y < Top ? Top - p.Y : p.Y >= Bottom ? p.Y - (Bottom - 1) : 0;
        return dx * dx + dy * dy;
    }

    /// <summary>Equality with a small tolerance, used to check whether a window still sits in its zone.</summary>
    public bool NearlyEquals(RectI other, int tolerance = 2) =>
        Math.Abs(Left - other.Left) <= tolerance &&
        Math.Abs(Top - other.Top) <= tolerance &&
        Math.Abs(Right - other.Right) <= tolerance &&
        Math.Abs(Bottom - other.Bottom) <= tolerance;

    public override string ToString() => $"[{Left},{Top} {Width}x{Height}]";
}
