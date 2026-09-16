using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevPane.Integrations.Claude;

public enum StatusLineSetupResult
{
    Added,
    AlreadyAdded,
    OtherStatusLineInUse,
    Failed,
}

/// <summary>
/// Live plan limits through Claude Code's status line. Claude Code runs the status line command after each reply and
/// passes it session data as JSON, including <c>rate_limits</c> for Pro and Max plans
/// (https://code.claude.com/docs/en/statusline). Dev Pane's command saves those limits for the card, and prints a short
/// status line for Claude Code to show.
/// </summary>
public static class ClaudeStatusLine
{
    /// <summary>The argument that makes Dev Pane's executable act as the status line command.</summary>
    public const string CommandArgument = "claude-statusline";

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    // A packaged app's writes under LocalAppData go to its private package storage, which the widget provider (the same
    // package) reads through the same path.
    private static string DataFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevPane", "claude-rate-limits.json");

    private static string SettingsFile => Path.Combine(ClaudeConfig.Directory, "settings.json");

    /// <summary>
    /// Runs as Claude Code's status line: saves the plan limits from the session data and returns the line to print,
    /// such as "Opus 5 · session 23% · week 41%".
    /// </summary>
    public static string Run(Stream input)
    {
        using var document = JsonDocument.Parse(input);
        var root = document.RootElement;

        var parts = new List<string>();
        if (root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object
            && model.TryGetProperty("display_name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            parts.Add(name.GetString()!);
        }

        if (root.TryGetProperty("rate_limits", out var limits) && limits.ValueKind == JsonValueKind.Object)
        {
            Save(limits);
            AddPercent(parts, limits, "five_hour", "session");
            AddPercent(parts, limits, "seven_day", "week");
        }
        else if (root.TryGetProperty("context_window", out var context) && context.ValueKind == JsonValueKind.Object
            && context.TryGetProperty("used_percentage", out var used) && used.ValueKind == JsonValueKind.Number)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{used.GetDouble():0}% context"));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>True when Claude Code's status line runs Dev Pane's command.</summary>
    public static bool IsInstalled() => IsDevPaneCommand(ReadSettings()?["statusLine"]);

    /// <summary>True once the status line has saved limits at least once.</summary>
    public static bool HasReported() => File.Exists(DataFile);

    /// <summary>
    /// Sets Claude Code's status line to Dev Pane's command, unless another status line is already set up. Other settings
    /// are kept as they are.
    /// </summary>
    /// <param name="executable">The full path of Dev Pane's command, such as its app execution alias.</param>
    public static StatusLineSetupResult Install(string executable)
    {
        try
        {
            var settings = File.Exists(SettingsFile) ? ReadSettings() : new JsonObject();
            if (settings is null)
            {
                return StatusLineSetupResult.Failed;
            }

            if (settings["statusLine"] is JsonNode existing)
            {
                return IsDevPaneCommand(existing) ? StatusLineSetupResult.AlreadyAdded : StatusLineSetupResult.OtherStatusLineInUse;
            }

            // Forward slashes and quotes work in every shell Claude Code runs the command in, even with spaces in the path.
            settings["statusLine"] = new JsonObject
            {
                ["type"] = "command",
                ["command"] = $"\"{executable.Replace('\\', '/')}\" {CommandArgument}",
            };

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            WriteAtomically(SettingsFile, settings.ToJsonString(IndentedJson) + Environment.NewLine);
            return StatusLineSetupResult.Added;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return StatusLineSetupResult.Failed;
        }
    }

    /// <summary>The limits the status line saved last, or null if it hasn't run.</summary>
    internal static ClaudePlanSnapshot? Read()
    {
        try
        {
            using var stream = new FileStream(DataFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (!root.TryGetProperty("savedAtMs", out var saved) || !saved.TryGetInt64(out long savedMs)
                || !root.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new ClaudePlanSnapshot(
                DateTimeOffset.FromUnixTimeMilliseconds(savedMs),
                ReadLimit(limits, "five_hour"),
                ReadLimit(limits, "seven_day"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void Save(JsonElement limits)
    {
        var file = new JsonObject
        {
            ["savedAtMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["rate_limits"] = JsonNode.Parse(limits.GetRawText()),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(DataFile)!);
        WriteAtomically(DataFile, file.ToJsonString());
    }

    private static void AddPercent(List<string> parts, JsonElement limits, string window, string label)
    {
        if (limits.TryGetProperty(window, out var limit) && limit.ValueKind == JsonValueKind.Object
            && limit.TryGetProperty("used_percentage", out var used) && used.ValueKind == JsonValueKind.Number)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{label} {used.GetDouble():0}%"));
        }
    }

    // The status line reports percentages from 0 to 100 and reset times in Unix seconds.
    private static ClaudePlanLimit? ReadLimit(JsonElement limits, string window)
    {
        if (!limits.TryGetProperty(window, out var limit) || limit.ValueKind != JsonValueKind.Object
            || !limit.TryGetProperty("used_percentage", out var used) || used.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        DateTimeOffset? resetsAt = limit.TryGetProperty("resets_at", out var resets) && resets.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)(resets.GetDouble() * 1000))
            : null;
        return new ClaudePlanLimit((int)Math.Round(Math.Clamp(used.GetDouble(), 0, 100)), resetsAt);
    }

    private static bool IsDevPaneCommand(JsonNode? statusLine) =>
        statusLine is JsonObject settings
        && settings["command"] is JsonValue command
        && command.TryGetValue(out string? text)
        && text.Contains(CommandArgument, StringComparison.Ordinal);

    private static JsonObject? ReadSettings()
    {
        try
        {
            return File.Exists(SettingsFile)
                ? JsonNode.Parse(File.ReadAllText(SettingsFile), documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                }) as JsonObject
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // Readers never see a half-written file: write alongside, then swap it in.
    private static void WriteAtomically(string path, string contents)
    {
        string temporary = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temporary, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }
}
