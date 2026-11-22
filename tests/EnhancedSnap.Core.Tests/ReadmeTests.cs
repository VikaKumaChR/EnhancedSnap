using System.Text.RegularExpressions;

namespace EnhancedSnap.Core.Tests;

/// <summary>
/// The three READMEs (English, 正體中文, 简体中文) say the same things: the same sections and table rows, links to each
/// other and to files that exist. The Simplified Chinese one is written in the words of Windows' own Simplified Chinese
/// interface (贴靠, 布局, 任务栏…), not Traditional characters or 「对齐」 for snapping.
/// </summary>
public partial class ReadmeTests
{
    private static readonly string[] Readmes = ["README.md", "README.zh-TW.md", "README.zh-CN.md"];

    [Fact]
    public void The_readmes_have_the_same_sections_and_rows()
    {
        var texts = Readmes.ToDictionary(name => name, Read);

        int Headings(string text) => text.Split('\n').Count(static line => line.StartsWith("## ", StringComparison.Ordinal));
        int Rows(string text) => text.Split('\n').Count(static line => line.StartsWith('|'));
        int Bullets(string text) => text.Split('\n').Count(static line => line.StartsWith("- ", StringComparison.Ordinal));

        Assert.All(texts, readme =>
        {
            Assert.Equal(Headings(texts["README.md"]), Headings(readme.Value));
            Assert.Equal(Rows(texts["README.md"]), Rows(readme.Value));
            Assert.Equal(Bullets(texts["README.md"]), Bullets(readme.Value));
        });
    }

    [Fact]
    public void Each_readme_links_to_the_other_two_and_only_to_files_that_exist()
    {
        string root = FindRepositoryRoot();
        foreach (string name in Readmes)
        {
            string text = Read(name);
            var targets = LinkTarget().Matches(text).Select(static m => m.Groups["target"].Value)
                .Where(static t => !t.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !t.StartsWith('#'))
                .ToList();

            Assert.All(Readmes.Where(other => other != name), other => Assert.Contains(other, targets));
            Assert.All(targets, target => Assert.True(File.Exists(Path.Combine(root, target)), $"{name} links to {target}, which doesn't exist"));
        }
    }

    [Fact]
    public void The_simplified_chinese_readme_uses_windows_simplified_chinese_words()
    {
        // The language links name themselves in their own script.
        string text = Read("README.zh-CN.md").Replace("正體中文", string.Empty).Replace("繁體中文", string.Empty);

        Assert.DoesNotContain("对齐", text);
        // Windows' own words, and the app's own names where the README uses them (template names, the setting's title).
        foreach (string word in new[] { "贴靠", "布局", "任务栏", "鼠标", "图标", "默认", "设置", "替代 Windows 贴靠", "左右平分", "中央焦点" })
        {
            Assert.Contains(word, text);
        }

        // Characters that only Traditional Chinese writes this way (their Simplified forms are used instead).
        const string traditional = "貼齊視圖顯設選單區間與為這個開關體鍵輸時會應頁預標準";
        Assert.All(traditional, c => Assert.DoesNotContain(c.ToString(), text));
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(FindRepositoryRoot(), name)).Replace("\r\n", "\n");

    // href="…", src="…" (the icon and the demo GIF) and ](…) links.
    [GeneratedRegex("""(?:href="|src="|\]\()(?<target>[^")#\s]+)""")]
    private static partial Regex LinkTarget();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "EnhancedSnap.slnx")))
        {
            directory = directory.Parent ?? throw new DirectoryNotFoundException("EnhancedSnap.slnx is not above the test output");
        }

        return directory.FullName;
    }
}
