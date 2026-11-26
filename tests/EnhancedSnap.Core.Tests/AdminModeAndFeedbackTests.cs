using System.Diagnostics;
using System.Text.Json;
using EnhancedSnap.Core.Platform;
using EnhancedSnap.Core.Settings;

namespace EnhancedSnap.Core.Tests;

/// <summary>Administrator mode's checks (T2), the feedback link (T5) and the app theme setting (T4).</summary>
public class AdminModeAndFeedbackTests
{
    [Fact]
    public void Process_checks_read_this_process()
    {
        int pid = Environment.ProcessId;
        Assert.Equal(Environment.IsPrivilegedProcess, AdminMode.IsElevated(pid));
        Assert.Equal(Process.GetCurrentProcess().MainModule!.FileName, AdminMode.ImagePath(pid), ignoreCase: true);
    }

    [Fact]
    public void Process_checks_answer_null_for_a_process_that_does_not_exist()
    {
        Assert.Null(AdminMode.IsElevated(0));
        Assert.Null(AdminMode.ImagePath(0));
    }

    [Fact]
    public void Another_executable_is_not_this_engine()
    {
        Assert.False(AdminMode.IsThisEngine(null));
        Assert.False(AdminMode.IsThisEngine(@"C:\Elsewhere\EnhancedSnap.exe"));
        Assert.True(AdminMode.IsThisEngine(AppInfo.EngineExePath.ToUpperInvariant()));
    }

    [Fact]
    public void The_task_is_per_user() => Assert.EndsWith(" - " + Environment.UserName, AdminMode.TaskName);

    [Fact]
    public void Feedback_link_opens_the_issue_form_with_only_version_edition_and_build()
    {
        string url = Feedback.IssueUrl("1.0.2", "Installer (MSI)", "10.0.26300.7171");

        Assert.Equal(
            "https://github.com/VikaKumaChR/EnhancedSnap/issues/new?template=feedback.yml&version=1.0.2&edition=Installer%20%28MSI%29&windows=10.0.26300.7171",
            url);
    }

    [Fact]
    public void Feedback_link_from_this_machine_carries_no_personal_details()
    {
        string url = Feedback.IssueUrl();
        string query = new Uri(url).Query;

        Assert.DoesNotContain(Environment.UserName, Uri.UnescapeDataString(query), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, Uri.UnescapeDataString(query), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("labels=", query); // needs repository rights: GitHub answers 404 for everyone else
        Assert.DoesNotContain("assignees=", query);
        Assert.Equal(["template", "version", "edition", "windows"], query.TrimStart('?').Split('&').Select(p => p.Split('=')[0]));
    }

    [Fact]
    public void Issue_form_has_the_fields_the_link_fills_in()
    {
        string form = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "ISSUE_TEMPLATE", Feedback.TemplateFile));

        foreach (string id in new[] { "version", "edition", "windows" })
        {
            Assert.Contains($"id: {id}\n", form.ReplaceLineEndings("\n"));
        }
    }

    [Fact]
    public void Theme_follows_windows_by_default_and_round_trips()
    {
        Assert.Equal(AppTheme.System, new AppSettings().Theme);

        string json = JsonSerializer.Serialize(new AppSettings { Theme = AppTheme.Dark }, SettingsJsonContext.Default.AppSettings);
        Assert.Contains("\"theme\": \"Dark\"", json);
        Assert.Equal(AppTheme.Dark, JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!.Theme);
    }

    [Fact]
    public void Settings_from_before_the_theme_keep_their_language_and_follow_windows()
    {
        const string older = """{ "schemaVersion": 1, "language": "zh-Hant", "runAsAdministrator": true }""";
        var settings = JsonSerializer.Deserialize(older, SettingsJsonContext.Default.AppSettings)!;

        Assert.Equal("zh-Hant", settings.Language);
        Assert.True(settings.RunAsAdministrator);
        Assert.Equal(AppTheme.System, settings.Theme);
    }

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
