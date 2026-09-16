namespace DevPane.Integrations.LocalDev;

/// <summary>
/// Decides which processes count as developer tools. Process names have no ".exe" and match case-insensitively.
/// </summary>
internal static class DevCatalog
{
    // Process name → the group name shown on the card.
    private static readonly Dictionary<string, string> Groups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["node"] = "node",
        ["bun"] = "bun",
        ["deno"] = "deno",
        ["python"] = "python",
        ["python3"] = "python",
        ["pythonw"] = "python",
        ["dotnet"] = "dotnet",
        ["java"] = "java",
        ["javaw"] = "java",
        ["ruby"] = "ruby",
        ["php"] = "php",
        ["php-cgi"] = "php",
        ["go"] = "go",
        ["gopls"] = "go",
        ["cargo"] = "rust",
        ["rustc"] = "rust",
        ["rust-analyzer"] = "rust",
        ["postgres"] = "postgres",
        ["mysqld"] = "mysql",
        ["mariadbd"] = "mysql",
        ["redis-server"] = "redis",
        ["mongod"] = "mongodb",
        ["nginx"] = "nginx",
        ["httpd"] = "apache",
        ["caddy"] = "caddy",
        ["docker"] = "Docker",
        ["com.docker.backend"] = "Docker",
        ["Docker Desktop"] = "Docker",
        ["MSBuild"] = "MSBuild",
        ["VBCSCompiler"] = "MSBuild",
        ["Code"] = "VS Code",
        ["Code - Insiders"] = "VS Code",
        ["Cursor"] = "Cursor",
        ["devenv"] = "Visual Studio",
        ["rider64"] = "Rider",
        ["idea64"] = "IntelliJ IDEA",
        // Both the Claude desktop app and the Claude Code CLI run as claude.exe.
        ["claude"] = "Claude",
    };

    // Editors and build tools open ports for their own plumbing, so they never count as dev servers.
    private static readonly HashSet<string> NonServerGroups = new(StringComparer.Ordinal)
    {
        "MSBuild", "VS Code", "Cursor", "Visual Studio", "Rider", "IntelliJ IDEA", "Claude",
    };

    public static string? GroupOf(string processName) => Groups.GetValueOrDefault(processName);

    /// <summary>The label for a listening port's owner, or null when the owner isn't a dev server.</summary>
    public static string? ServerLabel(string processName)
    {
        // Ports forwarded out of WSL are owned by wslrelay.
        if (processName.Equals("wslrelay", StringComparison.OrdinalIgnoreCase))
        {
            return "WSL";
        }

        return GroupOf(processName) is { } group && !NonServerGroups.Contains(group) ? group : null;
    }

    /// <summary>The processes that stand in for WSL 2 and Docker Desktop virtual machines.</summary>
    public static bool IsVirtualMachine(string processName) =>
        processName.Equals("vmmem", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("VmmemWSL", StringComparison.OrdinalIgnoreCase);
}
