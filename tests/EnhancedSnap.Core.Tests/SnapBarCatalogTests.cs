using System.Text.Json;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Layouts;
using EnhancedSnap.Core.Settings;

namespace EnhancedSnap.Core.Tests;

/// <summary>The snap bar's pop down: its sets (suggestion00), their order and how they wrap.</summary>
public class SnapBarCatalogTests
{
    private static string[] Ids(IEnumerable<LayoutDefinition> layouts) => [.. layouts.Select(static l => l.Id)];

    [Fact]
    public void The_classic_set_is_what_Windows_Snap_offers_for_the_screen()
    {
        // 1920 × 1032 at 150 %: 1280 effective pixels wide, the four layouts of suggestion00.
        Assert.Equal(
            [LayoutCatalog.Halves, LayoutCatalog.LeftTwoThirds, LayoutCatalog.HalfStack, LayoutCatalog.Quarters],
            Ids(SnapBarCatalog.Classic(1920, 1032, 1.5)));
        Assert.Equal(6, SnapBarCatalog.Classic(2560, 1392, 1.0).Count); // wide: six
        Assert.Equal(LayoutCatalog.CenterFocus, SnapBarCatalog.Classic(3440, 1400, 1.0)[0].Id); // ultrawide
        Assert.Equal([LayoutCatalog.Rows, LayoutCatalog.Halves, LayoutCatalog.Quarters], Ids(SnapBarCatalog.Classic(1080, 1872, 1.0))); // portrait
    }

    [Fact]
    public void The_classic_set_puts_the_monitors_own_layout_first()
    {
        var own = LayoutCatalog.FindBuiltIn(LayoutCatalog.MainStack);
        Assert.Equal(
            [LayoutCatalog.MainStack, LayoutCatalog.Halves, LayoutCatalog.LeftTwoThirds, LayoutCatalog.HalfStack],
            Ids(SnapBarCatalog.Classic(1920, 1032, 1.5, own)));
        // Already one of them, or a single zone: unchanged.
        Assert.Equal(4, SnapBarCatalog.Classic(1920, 1032, 1.5, LayoutCatalog.FindBuiltIn(LayoutCatalog.Quarters)).Count);
        Assert.Equal(LayoutCatalog.Halves, SnapBarCatalog.Classic(1920, 1032, 1.5, LayoutCatalog.FindBuiltIn(LayoutCatalog.Full))[0].Id);
    }

    [Fact]
    public void The_horizontal_set_is_top_and_bottom_splits()
    {
        Assert.Equal(
            [LayoutCatalog.Rows, LayoutCatalog.TopTwoThirds, LayoutCatalog.TopMain, LayoutCatalog.ThreeRows],
            Ids(SnapBarCatalog.Horizontal));
        foreach (var layout in SnapBarCatalog.Horizontal)
        {
            // Every one has a zone across the whole width: windows wider than tall on a landscape screen.
            Assert.Contains(layout.Zones, z => z.X == 0 && Math.Abs(z.W - 1) < 1e-9);
        }
    }

    [Theory]
    [InlineData(LayoutCatalog.TopTwoThirds, 2)]
    [InlineData(LayoutCatalog.TopMain, 3)]
    [InlineData(LayoutCatalog.ThreeRows, 3)]
    public void New_layouts_tile_the_screen_without_overlaps(string id, int zones)
    {
        var layout = LayoutCatalog.FindBuiltIn(id)!;
        Assert.Equal(zones, layout.Zones.Count);
        Assert.Equal(1, layout.Zones.Sum(static z => z.W * z.H), 9);
        for (int i = 0; i < layout.Zones.Count; i++)
        {
            for (int j = i + 1; j < layout.Zones.Count; j++)
            {
                var (a, b) = (layout.Zones[i], layout.Zones[j]);
                double overlap = Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X)) * Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y));
                Assert.True(overlap < 1e-9, $"{id}: zones {i} and {j} overlap");
            }
        }

        // Fractions, so the same layout fits any work area at any scale.
        foreach (var (work, scale) in new[] { (RectI.FromSize(0, 0, 1920, 1032), 1.5), (RectI.FromSize(0, 0, 1080, 1872), 1.0), (RectI.FromSize(-3440, 0, 3440, 1392), 1.25) })
        {
            Assert.All(ZoneMath.ComputeZones(layout, work, scale, 8, 8), r => Assert.False(r.IsEmpty));
        }
    }

    [Fact]
    public void Sets_are_offered_classic_always_horizontal_unless_off_custom_once_it_has_layouts()
    {
        var settings = new AppSettings();
        Assert.Equal([SnapBarSet.Classic, SnapBarSet.Horizontal], SnapBarCatalog.Groups(settings, 1920, 1032, 1.5).Select(static g => g.Set));

        settings.SnapBarLayouts.CustomLayoutIds = [LayoutCatalog.MainStack];
        Assert.Equal([SnapBarSet.Classic, SnapBarSet.Horizontal, SnapBarSet.Custom], SnapBarCatalog.Groups(settings, 1920, 1032, 1.5).Select(static g => g.Set));

        settings.SnapBarLayouts.ShowHorizontal = false;
        Assert.Equal([SnapBarSet.Classic, SnapBarSet.Custom], SnapBarCatalog.Groups(settings, 1920, 1032, 1.5).Select(static g => g.Set));
    }

    [Fact]
    public void The_custom_set_keeps_the_users_order_and_skips_layouts_that_are_gone()
    {
        var settings = new AppSettings
        {
            CustomLayouts = [new CustomLayout { Id = "custom-a", Name = "Code", Tracks = [new LayoutTrack { Weight = 2 }, new LayoutTrack { Splits = 2 }] }],
        };
        settings.SnapBarLayouts.CustomLayoutIds = ["custom-a", "custom-deleted", LayoutCatalog.Rows, LayoutCatalog.Full];

        var custom = SnapBarCatalog.Custom(settings);
        Assert.Equal(["custom-a", LayoutCatalog.Rows], Ids(custom));
        Assert.True(custom[0].IsCustom);
        Assert.Equal("Code", custom[0].CustomName);
    }

    [Fact]
    public void The_pop_down_opens_on_the_wanted_set_or_else_the_classic_one()
    {
        var settings = new AppSettings();
        var groups = SnapBarCatalog.Groups(settings, 1920, 1032, 1.5);
        Assert.Equal(1, SnapBarCatalog.IndexOf(groups, SnapBarSet.Horizontal));
        Assert.Equal(0, SnapBarCatalog.IndexOf(groups, SnapBarSet.Custom)); // empty custom set: not offered
    }

    [Theory]
    [InlineData(4, 6, 4, 1)]
    [InlineData(6, 6, 6, 1)]
    [InlineData(7, 6, 4, 2)]
    [InlineData(12, 6, 6, 2)]
    [InlineData(5, 3, 3, 2)]
    [InlineData(3, 0, 1, 3)]
    [InlineData(0, 6, 0, 0)]
    public void Many_layouts_wrap_into_even_rows(int count, int fit, int columns, int rows) =>
        Assert.Equal((columns, rows), SnapBarCatalog.Grid(count, fit));

    [Fact]
    public void Cards_that_fit_follow_the_screen_width()
    {
        Assert.Equal(6, SnapBarCatalog.FitColumns(1248, 72, 13)); // a 1280 epx screen: capped at six
        Assert.Equal(4, SnapBarCatalog.FitColumns(400, 72, 13)); // 4 × 72 + 3 × 13 = 327 ≤ 400 < 412
        Assert.Equal(1, SnapBarCatalog.FitColumns(40, 72, 13));
    }

    [Fact]
    public void Snap_bar_settings_are_stored_as_layout_ids_and_ratios_not_pixels()
    {
        var settings = new AppSettings
        {
            CustomLayouts = [new CustomLayout { Id = "custom-a", Name = "Code", Tracks = [new LayoutTrack { Weight = 2 }, new LayoutTrack { Weight = 1, Splits = 2 }] }],
        };
        settings.SnapBarLayouts.StartSet = SnapBarSet.Custom;
        settings.SnapBarLayouts.ShowHorizontal = false;
        settings.SnapBarLayouts.CustomLayoutIds = ["custom-a", LayoutCatalog.TopMain];

        string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        Assert.Contains("\"startSet\": \"Custom\"", json);
        Assert.Contains("\"showHorizontal\": false", json);
        Assert.Contains("\"customLayoutIds\": [", json);

        var back = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;
        back.Normalize();
        Assert.Equal(SnapBarSet.Custom, back.SnapBarLayouts.StartSet);
        Assert.False(back.SnapBarLayouts.ShowHorizontal);
        Assert.Equal(["custom-a", LayoutCatalog.TopMain], back.SnapBarLayouts.CustomLayoutIds);

        // The custom layout is relative: 2 : 1 columns, the second split in two, on any screen.
        var zones = LayoutCatalog.FromCustom(back.CustomLayouts[0]).Zones;
        Assert.Equal(2.0 / 3, zones[0].W, 9);
        Assert.Equal(0.5, zones[1].H, 9);
    }

    [Fact]
    public void Normalize_cleans_a_hand_edited_snap_bar_list()
    {
        var settings = new AppSettings { CustomLayouts = [new CustomLayout { Id = "custom-a" }] };
        settings.SnapBarLayouts.StartSet = (SnapBarSet)42;
        settings.SnapBarLayouts.CustomLayoutIds =
        [
            " custom-a ", "custom-gone", "", LayoutCatalog.Full, "HALVES", LayoutCatalog.Halves,
            .. LayoutCatalog.BuiltIns.Select(static l => l.Id),
        ];
        settings.Normalize();

        Assert.Equal(SnapBarSet.Classic, settings.SnapBarLayouts.StartSet);
        var ids = settings.SnapBarLayouts.CustomLayoutIds;
        Assert.Equal(SnapBarSettings.MaxCustomLayouts, ids.Count);
        Assert.Equal("custom-a", ids[0]);
        Assert.Equal("HALVES", ids[1]); // the first spelling of a duplicate stays
        Assert.DoesNotContain("custom-gone", ids);
        Assert.DoesNotContain(LayoutCatalog.Full, ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void A_settings_file_from_before_has_the_classic_and_horizontal_sets()
    {
        var settings = JsonSerializer.Deserialize("""{ "gap": 8 }""", SettingsJsonContext.Default.AppSettings)!;
        settings.Normalize();
        Assert.Equal(SnapBarSet.Classic, settings.SnapBarLayouts.StartSet);
        Assert.True(settings.SnapBarLayouts.ShowHorizontal);
        Assert.Empty(settings.SnapBarLayouts.CustomLayoutIds);
    }
}
