namespace DevPane.Integrations.Claude;

/// <summary>Where Claude Code keeps its files for the current user.</summary>
internal static class ClaudeConfig
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string? Configured =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } configured
            ? Environment.ExpandEnvironmentVariables(configured.Trim())
            : null;

    /// <summary>Settings and session logs: CLAUDE_CONFIG_DIR when it's set, otherwise ".claude" in the home folder.</summary>
    public static string Directory => Configured ?? Path.Combine(Home, ".claude");

    /// <summary>.claude.json, Claude Code's saved state: in CLAUDE_CONFIG_DIR when it's set, otherwise in the home folder.</summary>
    public static string StateFile => Path.Combine(Configured ?? Home, ".claude.json");

    /// <summary>Folders that may hold session logs, including the ~/.config/claude location some installs use.</summary>
    public static IEnumerable<string> ProjectDirectories =>
        new[] { Directory, Path.Combine(Home, ".config", "claude") }
            .Select(root => Path.Combine(root, "projects"))
            .Where(System.IO.Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
