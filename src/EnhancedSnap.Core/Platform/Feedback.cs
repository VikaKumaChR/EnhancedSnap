using Microsoft.Win32;

namespace EnhancedSnap.Core.Platform;

/// <summary>
/// "Send feedback": a new GitHub issue from the repository's issue form (.github/ISSUE_TEMPLATE/feedback.yml) with three
/// of its fields filled in through the URL. Only Enhanced Snap's version, where it was installed from and the Windows
/// build: no user name, path, log or setting. Nothing is sent until the user submits the form on GitHub, which needs a
/// GitHub account. The link carries no labels or assignees: those need rights in the repository, and GitHub answers
/// such a link with 404 for everyone else.
/// </summary>
public static class Feedback
{
    public const string TemplateFile = "feedback.yml";

    public static string IssueUrl() => IssueUrl(AppInfo.Version, Edition(), WindowsBuild());

    public static string IssueUrl(string version, string edition, string windows) =>
        $"{AppInfo.RepositoryUrl}/issues/new?template={TemplateFile}"
        + $"&version={Uri.EscapeDataString(version)}"
        + $"&edition={Uri.EscapeDataString(edition)}"
        + $"&windows={Uri.EscapeDataString(windows)}";

    /// <summary>Microsoft Store, installer (the MSI installs into %LOCALAPPDATA%\Programs\Enhanced Snap) or ZIP.</summary>
    public static string Edition()
    {
        if (AppInfo.IsPackaged)
        {
            return "Microsoft Store";
        }

        string installer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppInfo.ProductName);
        return string.Equals(Path.TrimEndingDirectorySeparator(AppInfo.BaseDirectory), installer, StringComparison.OrdinalIgnoreCase)
            ? "Installer (MSI)"
            : "ZIP";
    }

    /// <summary>For example 10.0.26300.7171 (the last part is the update build revision).</summary>
    public static string WindowsBuild()
    {
        var version = Environment.OSVersion.Version;
        object? revision = null;
        try
        {
            revision = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR", null);
        }
        catch (Exception)
        {
            // the revision is only a detail
        }

        return revision is int ubr ? $"{version.Major}.{version.Minor}.{version.Build}.{ubr}" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
