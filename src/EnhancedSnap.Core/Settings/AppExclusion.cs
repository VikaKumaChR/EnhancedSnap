namespace EnhancedSnap.Core.Settings;

/// <summary>Excluded-app matching, same rules as FancyZones so existing habits carry over.</summary>
public static class AppExclusion
{
    /// <param name="processFileName">Executable file name, e.g. "notepad++.exe".</param>
    /// <param name="rules">"notepad" matches any process containing it; "notepad.exe" matches exactly.</param>
    public static bool IsExcluded(string? processFileName, IReadOnlyList<string> rules)
    {
        if (string.IsNullOrEmpty(processFileName) || rules.Count == 0)
        {
            return false;
        }

        foreach (string rule in rules)
        {
            if (rule.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(processFileName, rule, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (processFileName.Contains(rule, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
