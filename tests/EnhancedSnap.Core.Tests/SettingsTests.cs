using System.Text.Json;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Core.Settings;

namespace EnhancedSnap.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void Json_round_trip_keeps_values_and_uses_readable_enums()
    {
        var settings = new AppSettings
        {
            Gap = 12,
            Activation = ActivationMode.Always,
            Corners = CornerStyle.RoundSmall,
            ExcludedApps = ["Game.exe"],
            CustomLayouts = [new CustomLayout { Id = "custom-1", Name = "Wide", Tracks = [new LayoutTrack { Weight = 3 }] }],
        };
        settings.Monitors["\\\\?\\display#abc"] = new MonitorSettings { LayoutId = "thirds", Name = "Dell" };

        string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        Assert.Contains("\"activation\": \"Always\"", json);
        Assert.Contains("\"corners\": \"RoundSmall\"", json);

        var back = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;
        Assert.Equal(12, back.Gap);
        Assert.Equal(ActivationMode.Always, back.Activation);
        Assert.Equal("thirds", back.Monitors["\\\\?\\display#abc"].LayoutId);
        Assert.Equal(3, back.CustomLayouts[0].Tracks[0].Weight);
    }

    [Fact]
    public void Normalize_clamps_hand_edited_values()
    {
        var settings = new AppSettings
        {
            Gap = 500,
            Margin = -3,
            ExcludedApps = [" a.exe ", "", "A.EXE", "b"],
            CustomLayouts = [new CustomLayout { Id = "x", Tracks = [new LayoutTrack { Weight = double.NaN, Splits = 99 }] }, new CustomLayout { Id = "" }],
        };
        settings.Normalize();

        Assert.Equal(AppSettings.MaxSpacing, settings.Gap);
        Assert.Equal(0, settings.Margin);
        Assert.Equal(["a.exe", "b"], settings.ExcludedApps);
        Assert.Single(settings.CustomLayouts);
        Assert.Equal(1, settings.CustomLayouts[0].Tracks[0].Weight);
        Assert.Equal(CustomLayout.MaxSplits, settings.CustomLayouts[0].Tracks[0].Splits);
    }

    [Fact]
    public void Unknown_properties_and_comments_are_tolerated()
    {
        const string json = """
        {
          // hand edited
          "gap": 4,
          "futureSetting": true,
        }
        """;
        var settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;
        Assert.Equal(4, settings.Gap);
        Assert.True(settings.Enabled);
    }

    [Theory]
    [InlineData("notepad.exe", "notepad", true)]
    [InlineData("notepad++.exe", "notepad", true)]
    [InlineData("notepad++.exe", "notepad.exe", false)]
    [InlineData("NOTEPAD.EXE", "notepad.exe", true)]
    [InlineData("code.exe", "notepad", false)]
    public void Exclusion_rules_match_like_fancyzones(string process, string rule, bool expected) =>
        Assert.Equal(expected, AppExclusion.IsExcluded(process, [rule]));

    [Fact]
    public void Every_translation_key_exists_in_english()
    {
        foreach (string language in Strings.Supported)
        {
            var strings = Strings.Use(language);
            foreach (var property in typeof(Strings).GetProperties().Where(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0))
            {
                if (property.Name is nameof(Strings.Language))
                {
                    continue;
                }

                string value = (string)property.GetValue(strings)!;
                Assert.False(string.IsNullOrWhiteSpace(value), $"{language}: {property.Name} is empty");
                Assert.NotEqual(property.Name, value); // a key echoed back means it's missing even in English
            }
        }

        Strings.Use(Strings.English);
    }

    [Fact]
    public void Language_resolution_accepts_supported_tags_only()
    {
        Assert.Equal(Strings.TraditionalChinese, Strings.Resolve("zh-Hant"));
        Assert.Equal(Strings.SimplifiedChinese, Strings.Resolve("zh-Hans"));
        Assert.Contains(Strings.Resolve("xx-unknown"), Strings.Supported);
    }
}
