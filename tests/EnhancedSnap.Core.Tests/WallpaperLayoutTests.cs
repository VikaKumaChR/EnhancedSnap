using EnhancedSnap.Core.Displays;
using EnhancedSnap.Core.Geometry;

namespace EnhancedSnap.Core.Tests;

/// <summary>Suggestion01_01 / 01_02: the previews show the wallpaper the way Windows fits it.</summary>
public class WallpaperLayoutTests
{
    private const double W = 2560; // a 16:10 monitor
    private const double H = 1600;

    /// <summary>A 3 × 2 picture stored the way a camera would for each EXIF orientation; upright it reads 1 2 3 / 4 5 6.</summary>
    public static TheoryData<int, int, int, int[]> StoredFrames => new()
    {
        { 1, 3, 2, new[] { 1, 2, 3, 4, 5, 6 } },
        { 2, 3, 2, new[] { 3, 2, 1, 6, 5, 4 } },
        { 3, 3, 2, new[] { 6, 5, 4, 3, 2, 1 } },
        { 4, 3, 2, new[] { 4, 5, 6, 1, 2, 3 } },
        { 5, 2, 3, new[] { 1, 4, 2, 5, 3, 6 } },
        { 6, 2, 3, new[] { 3, 6, 2, 5, 1, 4 } }, // row 0 is the picture's right side: shown turned 90° clockwise
        { 7, 2, 3, new[] { 6, 3, 5, 2, 4, 1 } },
        { 8, 2, 3, new[] { 4, 1, 5, 2, 6, 3 } }, // row 0 is the picture's left side: shown turned 90° counter-clockwise
    };

    [Fact]
    public void Fill_covers_the_monitor_and_crops_the_overflow_evenly()
    {
        var r = WallpaperLayout.Place(WallpaperFit.Fill, W, H, 3840, 2160); // 16:9 image on 16:10
        Assert.Equal(H, r.Height, 6);
        Assert.Equal(3840 * (H / 2160), r.Width, 6);
        Assert.Equal(-(r.Width - W) / 2, r.X, 6);
        Assert.Equal(0, r.Y, 6);
    }

    [Fact]
    public void Fit_shows_the_whole_image_with_bars()
    {
        var r = WallpaperLayout.Place(WallpaperFit.Fit, W, H, 3840, 2160);
        Assert.Equal(W, r.Width, 6);
        Assert.Equal(1440, r.Height, 6);
        Assert.Equal(80, r.Y, 6);
    }

    [Fact]
    public void Stretch_ignores_the_aspect_ratio()
    {
        Assert.Equal(new WallpaperRect(0, 0, W, H), WallpaperLayout.Place(WallpaperFit.Stretch, W, H, 640, 640));
    }

    [Fact]
    public void Center_keeps_the_image_at_its_own_size()
    {
        Assert.Equal(new WallpaperRect(960, 480, 640, 640), WallpaperLayout.Place(WallpaperFit.Center, W, H, 640, 640));
    }

    [Fact]
    public void Tile_starts_at_the_top_left()
    {
        Assert.Equal(new WallpaperRect(0, 0, 300, 200), WallpaperLayout.Place(WallpaperFit.Tile, W, H, 300, 200));
    }

    [Fact]
    public void Span_fills_the_box_of_all_monitors_and_each_monitor_shows_its_part()
    {
        // Two 2560 x 1600 monitors side by side; the primary is the left one.
        var desktop = new WallpaperRect(0, 0, 5120, 1600);
        var left = WallpaperLayout.Place(WallpaperFit.Span, W, H, 5120, 1600, new WallpaperRect(0, 0, W, H), desktop);
        var right = WallpaperLayout.Place(WallpaperFit.Span, W, H, 5120, 1600, new WallpaperRect(2560, 0, W, H), desktop);
        Assert.Equal(new WallpaperRect(0, 0, 5120, 1600), left);
        Assert.Equal(new WallpaperRect(-2560, 0, 5120, 1600), right);
        // Without the desktop's layout it falls back to Fill.
        Assert.Equal(WallpaperLayout.Place(WallpaperFit.Fill, W, H, 5120, 1600), WallpaperLayout.Place(WallpaperFit.Span, W, H, 5120, 1600));
    }

    [Fact]
    public void Span_lays_one_picture_over_monitors_of_different_sizes()
    {
        // A 2560 × 1600 primary and a 1920 × 1080 monitor on its right, bottoms aligned: together 4480 × 1600. A 2:1
        // picture fills that box at 4480 × 2240, cut evenly top and bottom, and each monitor shows its own part of it.
        var primary = new RectI(0, 0, 2560, 1600);
        var side = new RectI(2560, 520, 4480, 1600);
        var desktop = primary.Union(side);
        AssertNear(new WallpaperRect(0, -320, 4480, 2240), PlaceOn(WallpaperFit.Span, primary, desktop, 1600, 800));
        AssertNear(new WallpaperRect(-2560, -840, 4480, 2240), PlaceOn(WallpaperFit.Span, side, desktop, 1600, 800));

        // The side monitor's preview is decoded from where that monitor's top-left cuts the picture (2560 of 4480 across,
        // 840 of 2240 down), at most a pixel before it, and covers all of that monitor.
        var plan = PlanFor(WallpaperFit.Span, side, desktop, 1600, 800);
        Assert.InRange(plan.Bounds.Left, (2560.0 / 4480 * plan.ScaledWidth) - 1, 2560.0 / 4480 * plan.ScaledWidth);
        Assert.InRange(plan.Bounds.Top, (840.0 / 2240 * plan.ScaledHeight) - 1, 840.0 / 2240 * plan.ScaledHeight);
        Assert.True(Covers(plan.Placement, new WallpaperRect(0, 0, 1, 1)), $"{plan.Placement}");
    }

    [Fact]
    public void Monitors_left_of_and_above_the_primary_display_have_negative_origins()
    {
        // A 1920 × 1080 monitor left of the 2560 × 1600 primary, 200 px higher: the desktop runs from (-1920, -200). A
        // picture of the desktop's own size spans it 1:1, so the primary shows it from (1920, 200), at half size in its
        // 1280 × 800 preview, and the left monitor from its top-left.
        var primary = new RectI(0, 0, 2560, 1600);
        var left = new RectI(-1920, -200, 0, 880);
        var desktop = primary.Union(left);
        Assert.Equal(
            new WallpaperPlan(2240, 900, new RectI(960, 100, 2240, 900), 1280, 800, new WallpaperRect(0, 0, 1, 1), false),
            PlanFor(WallpaperFit.Span, primary, desktop, 4480, 1800));
        var onLeft = PlanFor(WallpaperFit.Span, left, desktop, 4480, 1800);
        Assert.Equal((0, 0), (onLeft.Bounds.Left, onLeft.Bounds.Top));

        // Every other fit depends on the monitor's size only.
        var atOrigin = new RectI(0, 0, 1920, 1080);
        foreach (var fit in new[] { WallpaperFit.Fill, WallpaperFit.Fit, WallpaperFit.Center, WallpaperFit.Stretch, WallpaperFit.Tile })
        {
            Assert.Equal(PlanFor(fit, atOrigin, atOrigin, 4480, 1800), PlanFor(fit, left, desktop, 4480, 1800));
        }
    }

    [Theory]
    [InlineData(2560, 1600, 1280, 800)]
    [InlineData(3840, 2160, 1280, 720)]
    [InlineData(7680, 4320, 1280, 720)]  // 8K: the preview never needs more than 1280 px
    [InlineData(1080, 1920, 720, 1280)]  // portrait
    [InlineData(1024, 768, 1024, 768)]   // smaller than the cap: not enlarged
    public void Preview_size_keeps_the_aspect_ratio_within_the_cap(double w, double h, int expectedW, int expectedH)
    {
        Assert.Equal((expectedW, expectedH), WallpaperLayout.PreviewSize(w, h));
    }

    [Fact]
    public void A_plan_decodes_inside_the_scaled_picture_and_covers_what_the_monitor_shows()
    {
        (RectI Monitor, RectI Desktop)[] screens =
        [
            (new RectI(0, 0, 2560, 1600), new RectI(0, 0, 2560, 1600)),
            (new RectI(0, 0, 1366, 768), new RectI(-1920, -312, 1366, 768)),  // a 1920 × 1080 monitor left of it, higher
            (new RectI(0, 0, 1080, 1920), new RectI(0, 0, 3640, 1920)),       // portrait, a 2560 × 1440 monitor on its right
            (new RectI(-1920, 0, 0, 1080), new RectI(-1920, 0, 2560, 1600)), // the left one of two, at a negative origin
        ];
        (int W, int H)[] images = [(3840, 2160), (640, 640), (1000, 3000), (5000, 1200), (1, 1), (2561, 1601), (333, 777)];
        foreach (var (monitor, desktop) in screens)
        {
            var (canvasW, canvasH) = WallpaperLayout.PreviewSize(monitor.Width, monitor.Height);
            foreach (var fit in Enum.GetValues<WallpaperFit>())
            {
                foreach (var (imageW, imageH) in images)
                {
                    for (int orientation = 1; orientation <= 8; orientation++)
                    {
                        // Stored so that, turned upright, it is imageW × imageH.
                        bool swap = WallpaperLayout.SwapsSides(orientation);
                        var plan = PlanFor(fit, monitor, desktop, swap ? imageH : imageW, swap ? imageW : imageH, orientation);
                        string context = $"{fit} {imageW}x{imageH} EXIF {orientation} on {monitor}: {plan}";

                        // A part of the scaled stored frame, about one canvas at most, the bitmap's size once upright.
                        var b = plan.Bounds;
                        Assert.True(!b.IsEmpty && b.Left >= 0 && b.Top >= 0 && b.Right <= plan.ScaledWidth && b.Bottom <= plan.ScaledHeight, context);
                        Assert.True((swap ? (b.Height, b.Width) : (b.Width, b.Height)) == (plan.Width, plan.Height), context);
                        Assert.True(plan.Width <= canvasW + 2 && plan.Height <= canvasH + 2, context);

                        // The stored frame scaled to the picture's size in the preview.
                        var place = PlaceOn(fit, monitor, desktop, imageW, imageH);
                        var (scaledW, scaledH) = swap ? (plan.ScaledHeight, plan.ScaledWidth) : (plan.ScaledWidth, plan.ScaledHeight);
                        Assert.True(Math.Abs(scaledW - Math.Max(1, place.Width * canvasW / monitor.Width)) <= 0.5 + 1e-9, context);
                        Assert.True(Math.Abs(scaledH - Math.Max(1, place.Height * canvasH / monitor.Height)) <= 0.5 + 1e-9, context);

                        if (fit == WallpaperFit.Tile)
                        {
                            // One tile at that scale, cut to the canvas, repeated over the whole monitor.
                            Assert.True(plan.Tiled && plan.Placement == new WallpaperRect(0, 0, 1, 1), context);
                            Assert.True((plan.Width, plan.Height) == (Math.Min(scaledW, canvasW), Math.Min(scaledH, canvasH)), context);
                            continue;
                        }

                        // Shown inside the picture, and over all of the picture that the monitor shows.
                        var picture = new WallpaperRect(place.X / monitor.Width, place.Y / monitor.Height, place.Width / monitor.Width, place.Height / monitor.Height);
                        var visible = picture.Intersect(new WallpaperRect(0, 0, 1, 1));
                        Assert.False(plan.Tiled, context);
                        Assert.True(Covers(plan.Placement, visible), $"{context}: needs {visible}");
                        Assert.True(Covers(picture, plan.Placement), $"{context}: outside {picture}");
                    }
                }
            }
        }
    }

    [Fact]
    public void A_tile_larger_than_the_monitor_shows_its_top_left_at_the_preview_s_scale()
    {
        // 3840 × 2160 tiled on 1920 × 1080: the preview is 1280 × 720 (scale 2/3), so the tile is decoded at 2560 × 1440
        // and only its top-left 1280 × 720 is cut — never squashed into the preview, and it never repeats.
        var monitor = new RectI(0, 0, 1920, 1080);
        Assert.Equal(
            new WallpaperPlan(2560, 1440, new RectI(0, 0, 1280, 720), 1280, 720, new WallpaperRect(0, 0, 1, 1), true),
            PlanFor(WallpaperFit.Tile, monitor, monitor, 3840, 2160));

        // Stored on its side (EXIF 6), the stored frame is scaled to 1440 × 2560 and the picture's top-left is the stored
        // frame's bottom-left.
        Assert.Equal(
            new WallpaperPlan(1440, 2560, new RectI(0, 1280, 720, 2560), 1280, 720, new WallpaperRect(0, 0, 1, 1), true),
            PlanFor(WallpaperFit.Tile, monitor, monitor, 2160, 3840, 6));

        // A small tile is decoded whole at the same scale (1/2 on 2560 × 1600), to be repeated.
        var wide = new RectI(0, 0, 2560, 1600);
        Assert.Equal(
            new WallpaperPlan(150, 100, new RectI(0, 0, 150, 100), 150, 100, new WallpaperRect(0, 0, 1, 1), true),
            PlanFor(WallpaperFit.Tile, wide, wide, 300, 200));
    }

    [Fact]
    public void Tiles_repeat_from_the_top_left_and_are_cut_at_the_edges()
    {
        // A 2 x 2 tile with four distinct pixels over a 5 x 3 canvas.
        byte[] tile = [1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4];
        byte[] canvas = new byte[5 * 3 * 4];
        WallpaperLayout.TileInto(canvas, 5, 3, tile, 2, 2);
        byte At(int x, int y) => canvas[((y * 5) + x) * 4];
        Assert.Equal([1, 2, 1, 2, 1], Enumerable.Range(0, 5).Select(x => At(x, 0)));
        Assert.Equal([3, 4, 3, 4, 3], Enumerable.Range(0, 5).Select(x => At(x, 1)));
        Assert.Equal([1, 2, 1, 2, 1], Enumerable.Range(0, 5).Select(x => At(x, 2)));
    }

    [Theory]
    [MemberData(nameof(StoredFrames))]
    public void Exif_orientation_shows_the_photo_upright(int orientation, int storedW, int storedH, int[] stored)
    {
        var (pixels, w, h) = WallpaperLayout.Orient(Pixels(stored), storedW, storedH, orientation);
        Assert.Equal((3, 2), (w, h));
        Assert.Equal(Values(Numbered(3, 2)), Values(pixels));
    }

    [Theory]
    [MemberData(nameof(StoredFrames))]
    public void A_part_found_in_the_stored_frame_turns_upright_into_the_same_part(int orientation, int storedW, int storedH, int[] stored)
    {
        // Every rectangle of the upright picture: cropping the stored frame where ToStoredFrame says and turning that crop
        // upright gives what turning the whole picture upright and cropping it gives.
        byte[] raw = Pixels(stored);
        byte[] upright = Numbered(3, 2);
        var parts =
            from y in Enumerable.Range(0, 2)
            from x in Enumerable.Range(0, 3)
            from h in Enumerable.Range(1, 2 - y)
            from w in Enumerable.Range(1, 3 - x)
            select RectI.FromSize(x, y, w, h);
        foreach (var part in parts)
        {
            var bounds = WallpaperLayout.ToStoredFrame(part, 3, 2, orientation);
            Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= storedW && bounds.Bottom <= storedH, $"{part} -> {bounds}");
            var (cropped, cw, ch) = WallpaperLayout.Crop(raw, storedW, storedH, bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            var (turned, tw, th) = WallpaperLayout.Orient(cropped, cw, ch, orientation);
            var (expected, _, _) = WallpaperLayout.Crop(upright, 3, 2, part.Left, part.Top, part.Width, part.Height);
            Assert.Equal((part.Width, part.Height), (tw, th));
            Assert.Equal(Values(expected), Values(turned));
        }
    }

    [Theory]
    [MemberData(nameof(StoredFrames))]
    public void The_plan_decodes_only_the_part_of_a_turned_photo_the_monitor_shows(int orientation, int storedW, int storedH, int[] stored)
    {
        // Centered at its own size on a 1 × 1 monitor, the picture shows its middle column.
        var center = PlanFor(WallpaperFit.Center, new RectI(0, 0, 1, 1), new RectI(0, 0, 1, 1), storedW, storedH, orientation);
        Assert.Equal([2, 5], Execute(center, Pixels(stored), storedW, storedH, orientation));

        // Spanned over a 1 × 2 monitor and the 2 × 2 primary on its right (the desktop starts at x = -1), the primary
        // shows the right two columns.
        var span = PlanFor(WallpaperFit.Span, new RectI(0, 0, 2, 2), new RectI(-1, 0, 2, 2), storedW, storedH, orientation);
        Assert.Equal([2, 3, 5, 6], Execute(span, Pixels(stored), storedW, storedH, orientation));
    }

    [Fact]
    public void Crop_takes_a_clamped_rectangle()
    {
        var (pixels, w, h) = WallpaperLayout.Crop(Numbered(3, 2), 3, 2, 1, 0, 5, 5);
        Assert.Equal((2, 2), (w, h));
        Assert.Equal([2, 3, 5, 6], Values(pixels));
    }

    [Fact]
    public void Backdrop_averages_the_picture_and_shows_the_color_around_it()
    {
        // A 4 x 2 picture, left half blue 200, right half blue 100, placed on the right half of the desktop (Fit-like);
        // the desktop color around it is blue 10.
        byte[] picture = [.. Enumerable.Range(0, 8).SelectMany(i => new byte[] { (byte)(i % 4 < 2 ? 200 : 100), 0, 0, 255 })];
        uint background = 0xFF00000A;
        byte[] backdrop = WallpaperLayout.Backdrop(picture, 4, 2, new WallpaperRect(0.5, 0, 0.5, 1), background, 4, 1);

        Assert.Equal([10, 10, 200, 100], Values(backdrop));            // color, color, then the picture's two halves
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(255, backdrop[(i * 4) + 3]));
        // A cell half covered by the picture is half picture, half color.
        byte[] half = WallpaperLayout.Backdrop(picture, 4, 2, new WallpaperRect(0.25, 0, 0.75, 1), background, 2, 1);
        Assert.InRange(half[0], 104, 106);                              // (10 + 200) / 2
    }

    [Fact]
    public void Backdrop_keeps_each_row_of_the_picture_where_it_is()
    {
        // A 2 x 2 picture, top row blue 200 and bottom row blue 40, on the bottom half of the desktop; the color above it
        // is blue 10. Four rows of backdrop: the color twice, then the picture's two rows.
        byte[] backdrop = WallpaperLayout.Backdrop(Pixels([200, 200, 40, 40]), 2, 2, new WallpaperRect(0, 0.5, 1, 0.5), 0xFF00000A, 1, 4);
        Assert.Equal([10, 10, 200, 40], Values(backdrop));
    }

    [Fact]
    public void Backdrop_shows_the_desktop_color_through_a_transparent_picture()
    {
        // A half-transparent pixel, premultiplied (blue 100, alpha 128), over an opaque desktop color (blue 30, green 20,
        // red 10), as the preview draws it: 100 + 30 × 127/255, 20 × 127/255, 10 × 127/255, and opaque.
        byte[] backdrop = WallpaperLayout.Backdrop([100, 0, 0, 128], 1, 1, new WallpaperRect(0, 0, 1, 1), 0xFF0A141E, 1, 1);
        Assert.Equal(new byte[] { 115, 10, 5, 255 }, backdrop);
    }

    [Fact]
    public void Intersect_clips_to_the_overlap()
    {
        var a = new WallpaperRect(-100, -50, 400, 300);
        Assert.Equal(new WallpaperRect(0, 0, 300, 250), a.Intersect(new WallpaperRect(0, 0, 1000, 1000)));
        Assert.True(a.Intersect(new WallpaperRect(500, 500, 10, 10)).IsEmpty);
    }

    /// <summary>BGRA pixels whose blue channel holds <paramref name="values"/>, opaque.</summary>
    private static byte[] Pixels(int[] values) => [.. values.SelectMany(n => new byte[] { (byte)n, 0, 0, 255 })];

    /// <summary>A <paramref name="w"/> × <paramref name="h"/> picture whose pixels are numbered from 1 row by row.</summary>
    private static byte[] Numbered(int w, int h) => Pixels([.. Enumerable.Range(1, w * h)]);

    private static int[] Values(byte[] pixels) => [.. pixels.Where((_, i) => i % 4 == 0).Select(b => (int)b)];

    /// <summary>Where Windows draws the picture on <paramref name="monitor"/>; both rectangles in desktop coordinates.</summary>
    private static WallpaperRect PlaceOn(WallpaperFit fit, RectI monitor, RectI desktop, double imageW, double imageH) =>
        WallpaperLayout.Place(
            fit,
            monitor.Width,
            monitor.Height,
            imageW,
            imageH,
            new WallpaperRect(monitor.Left - desktop.Left, monitor.Top - desktop.Top, monitor.Width, monitor.Height),
            new WallpaperRect(0, 0, desktop.Width, desktop.Height));

    /// <summary>The plan for the monitor's preview at the size the settings app uses.</summary>
    private static WallpaperPlan PlanFor(WallpaperFit fit, RectI monitor, RectI desktop, int storedW, int storedH, int orientation = 1)
    {
        var (canvasW, canvasH) = WallpaperLayout.PreviewSize(monitor.Width, monitor.Height);
        var plan = WallpaperLayout.Plan(fit, monitor, desktop, storedW, storedH, orientation, canvasW, canvasH);
        Assert.True(plan.HasValue, $"{fit} on {monitor}: nothing of the picture is on the monitor");
        return plan.GetValueOrDefault();
    }

    /// <summary>What the settings app does with a plan, here at the picture's own size: crop the stored frame, turn the crop upright.</summary>
    private static int[] Execute(WallpaperPlan plan, byte[] stored, int storedW, int storedH, int orientation)
    {
        Assert.Equal((storedW, storedH), (plan.ScaledWidth, plan.ScaledHeight));
        var (cropped, w, h) = WallpaperLayout.Crop(stored, storedW, storedH, plan.Bounds.Left, plan.Bounds.Top, plan.Bounds.Width, plan.Bounds.Height);
        Assert.Equal((plan.Bounds.Width, plan.Bounds.Height), (w, h));
        var (turned, tw, th) = WallpaperLayout.Orient(cropped, w, h, orientation);
        Assert.Equal((plan.Width, plan.Height), (tw, th));
        return Values(turned);
    }

    /// <summary>True when <paramref name="outer"/> contains <paramref name="inner"/>, give or take rounding.</summary>
    private static bool Covers(WallpaperRect outer, WallpaperRect inner, double tolerance = 1e-6) =>
        outer.X <= inner.X + tolerance && outer.Y <= inner.Y + tolerance && outer.Right >= inner.Right - tolerance && outer.Bottom >= inner.Bottom - tolerance;

    private static void AssertNear(WallpaperRect expected, WallpaperRect actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
        Assert.Equal(expected.Width, actual.Width, 6);
        Assert.Equal(expected.Height, actual.Height, 6);
    }
}
