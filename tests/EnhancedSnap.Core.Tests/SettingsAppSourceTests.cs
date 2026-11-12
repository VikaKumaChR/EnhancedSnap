namespace EnhancedSnap.Core.Tests;

/// <summary>Rules for the settings app's sources: WinUI can't run here, and these mistakes don't show in normal use.</summary>
public class SettingsAppSourceTests
{
    // A plain clickable SettingsCard works with mouse and keyboard, but screen readers and voice access can't press it:
    // its Invoke pattern fails with E_NOINTERFACE. ClickableSettingsCard, the one file allowed to set this, explains why.
    [Fact]
    public void Clickable_cards_are_ClickableSettingsCard()
    {
        string settingsApp = Path.Combine(FindRepositoryRoot(), "src", "EnhancedSnap.Settings");

        string[] offenders = Directory.EnumerateFiles(settingsApp, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(settingsApp, path))
            .Where(path => Path.GetExtension(path) is ".xaml" or ".cs")
            .Where(path => !path.StartsWith("bin" + Path.DirectorySeparatorChar) && !path.StartsWith("obj" + Path.DirectorySeparatorChar))
            .Where(path => Path.GetFileName(path) != "ClickableSettingsCard.cs")
            .Where(path => File.ReadAllText(Path.Combine(settingsApp, path)).Contains("IsClickEnabled"))
            .ToArray();

        Assert.Empty(offenders);
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
