using System.Text.Json;

namespace DevPane.Integrations.Claude;

/// <summary>A plan limit as Claude reported it: percent used and when it resets.</summary>
public sealed record ClaudePlanLimit(int Percent, DateTimeOffset? ResetsAt);

/// <param name="FetchedAt">When Claude Code last checked the limits.</param>
public sealed record ClaudePlanSnapshot(DateTimeOffset FetchedAt, ClaudePlanLimit? Session, ClaudePlanLimit? Week);

/// <summary>
/// Reads the plan limits Claude Code saves in <c>.claude.json</c> each time it checks usage, for example when you run
/// /usage. Nothing is requested from Claude: the percentages are only as recent as Claude Code's last check.
/// </summary>
/// <remarks>
/// <c>.claude.json</c> also holds account details. Only the usage entry is read, and nothing from the file is stored or
/// sent anywhere. The entry isn't a documented format, so anything unexpected is treated as missing.
/// </remarks>
internal sealed class ClaudePlanCache
{
    private string? _path;
    private DateTime _lastWriteUtc;
    private ClaudePlanSnapshot? _snapshot;

    public ClaudePlanSnapshot? Read()
    {
        string path = ClaudeConfig.StateFile;
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return null;
        }

        if (path == _path && info.LastWriteTimeUtc == _lastWriteUtc)
        {
            return _snapshot;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            _snapshot = Parse(document.RootElement);
            _path = path;
            _lastWriteUtc = info.LastWriteTimeUtc;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Claude Code may be rewriting the file; try again on the next refresh.
        }

        return _snapshot;
    }

    private static ClaudePlanSnapshot? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("cachedUsageUtilization", out var cache) || cache.ValueKind != JsonValueKind.Object
            || !cache.TryGetProperty("fetchedAtMs", out var fetched) || !fetched.TryGetInt64(out long fetchedMs)
            || !cache.TryGetProperty("utilization", out var utilization) || utilization.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new ClaudePlanSnapshot(
            DateTimeOffset.FromUnixTimeMilliseconds(fetchedMs),
            ReadLimit(utilization, "five_hour"),
            ReadLimit(utilization, "seven_day"));
    }

    private static ClaudePlanLimit? ReadLimit(JsonElement utilization, string name)
    {
        if (!utilization.TryGetProperty(name, out var limit) || limit.ValueKind != JsonValueKind.Object
            || !limit.TryGetProperty("utilization", out var percent) || percent.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        DateTimeOffset? resetsAt = limit.TryGetProperty("resets_at", out var resets)
            && resets.ValueKind == JsonValueKind.String
            && resets.TryGetDateTimeOffset(out var time)
                ? time
                : null;
        return new ClaudePlanLimit((int)Math.Round(Math.Clamp(percent.GetDouble(), 0, 100)), resetsAt);
    }
}
