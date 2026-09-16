using System.Buffers;
using System.Text.Json;

namespace DevPane.Integrations.Claude;

/// <summary>
/// Totals Claude Code usage from its session logs: one JSON object per line in <c>.claude\projects\&lt;project&gt;\*.jsonl</c>,
/// where each Claude reply records its model and token usage. Each refresh reads only the lines added since the last one.
/// </summary>
public sealed class ClaudeCodeLogs
{
    private const int ReadChunkBytes = 1 << 20;

    private static readonly TimeSpan SessionLength = TimeSpan.FromHours(5);
    private static readonly TimeSpan WeekLength = TimeSpan.FromDays(7);

    // Enough history for the seven-day chart in any time zone.
    private static readonly TimeSpan Retention = TimeSpan.FromDays(8);

    private readonly object _gate = new();
    private readonly Dictionary<string, LogFile> _files = new(StringComparer.OrdinalIgnoreCase);

    // Claude Code logs a reply once per content block, and resumed sessions copy earlier replies into the new log, so
    // replies are keyed by message and request ID and counted once.
    private readonly Dictionary<string, Reply> _replies = new(StringComparer.Ordinal);

    private readonly ClaudePlanCache _planCache = new();

    public static ClaudeCodeLogs Shared { get; } = new();

    public ClaudeUsageSummary Refresh()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.Now;
            var cutoff = now - Retention;
            var directories = ClaudeConfig.ProjectDirectories.ToList();
            var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string directory in directories)
            {
                foreach (var info in EnumerateLogs(directory, cutoff))
                {
                    current.Add(info.FullName);
                    if (!_files.TryGetValue(info.FullName, out var file))
                    {
                        file = new LogFile(ProjectFolderOf(directory, info.FullName));
                        _files[info.FullName] = file;
                    }

                    try
                    {
                        ReadNewLines(file, info, cutoff);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        // Deleted or locked while reading: the next refresh continues from the last complete line.
                    }
                }
            }

            foreach (string path in _files.Keys.Where(path => !current.Contains(path)).ToList())
            {
                _files.Remove(path);
            }

            foreach (string key in _replies.Where(pair => pair.Value.Time < cutoff).Select(pair => pair.Key).ToList())
            {
                _replies.Remove(key);
            }

            return Summarize(directories.Count > 0, now);
        }
    }

    private ClaudeUsageSummary Summarize(bool logsFound, DateTimeOffset now)
    {
        var replies = _replies.Values.OrderBy(reply => reply.Time).ToList();
        var today = LocalDate(now);
        var todays = replies.Where(reply => LocalDate(reply.Time) == today).ToList();

        var byDay = replies
            .GroupBy(reply => LocalDate(reply.Time))
            .ToDictionary(group => group.Key, group => (Cost: group.Sum(reply => reply.Cost ?? 0m), Tokens: group.Sum(reply => reply.Tokens.Total)));
        var lastSevenDays = Enumerable.Range(-6, 7)
            .Select(offset => today.AddDays(offset))
            .Select(date => byDay.TryGetValue(date, out var day)
                ? new ClaudeDailyUsage(date, day.Cost, day.Tokens)
                : new ClaudeDailyUsage(date, 0m, 0))
            .ToList();

        var projects = todays
            .GroupBy(reply => reply.File.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ClaudeProjectUsage(group.Key, group.Sum(reply => reply.Cost ?? 0m), group.Sum(reply => reply.Tokens.Total)))
            .OrderByDescending(project => project.Cost)
            .ThenByDescending(project => project.Tokens)
            .ToList();

        // Claude reports plan limits through the status line after every reply, and through the usage check Claude Code
        // saves; the newest report wins.
        var plans = new[] { ClaudeStatusLine.Read(), _planCache.Read() }
            .OfType<ClaudePlanSnapshot>()
            .OrderByDescending(plan => plan.FetchedAt)
            .ToList();

        return new ClaudeUsageSummary(
            logsFound,
            new ClaudeUsageTotals(
                todays.Sum(reply => reply.Cost ?? 0m),
                todays.Sum(reply => reply.Tokens.Total),
                todays.Count,
                todays.Select(reply => reply.SessionId).Distinct(StringComparer.Ordinal).Count()),
            lastSevenDays,
            CurrentSession(replies, plans, now),
            CurrentWeek(replies, plans, now),
            projects);
    }

    private static ClaudeUsagePeriod? CurrentSession(List<Reply> replies, List<ClaudePlanSnapshot> plans, DateTimeOffset now)
    {
        // Claude's own reset time is exact while its session is still running.
        foreach (var plan in plans)
        {
            if (plan.Session is { ResetsAt: { } resetsAt } limit && resetsAt > now)
            {
                var start = resetsAt - SessionLength;
                bool reportedThisSession = plan.FetchedAt >= start;
                return Period(replies, start, resetsAt, resetIsEstimate: false,
                    reportedThisSession ? limit.Percent : null, reportedThisSession ? plan.FetchedAt : null);
            }
        }

        // Otherwise estimate it: a session lasts five hours from the first message after the previous session ended.
        DateTimeOffset? sessionStart = null;
        foreach (var reply in replies)
        {
            if (sessionStart is null || reply.Time >= sessionStart.Value + SessionLength)
            {
                sessionStart = new DateTimeOffset(reply.Time.Ticks - reply.Time.Ticks % TimeSpan.TicksPerMinute, reply.Time.Offset);
            }
        }

        return sessionStart is { } estimatedStart && now < estimatedStart + SessionLength
            ? Period(replies, estimatedStart, estimatedStart + SessionLength, resetIsEstimate: true, percent: null, percentAsOf: null)
            : null;
    }

    private static ClaudeUsagePeriod CurrentWeek(List<Reply> replies, List<ClaudePlanSnapshot> plans, DateTimeOffset now)
    {
        if (plans.FirstOrDefault(plan => plan.Week?.ResetsAt is not null) is { Week: { ResetsAt: { } lastKnownReset } limit } report)
        {
            // Weekly limits reset at the same time every week, so an old reset time still gives the next one.
            var resetsAt = lastKnownReset;
            if (resetsAt <= now)
            {
                long weeksPassed = (now - resetsAt).Ticks / WeekLength.Ticks + 1;
                resetsAt += TimeSpan.FromTicks(WeekLength.Ticks * weeksPassed);
            }

            var start = resetsAt - WeekLength;
            bool reportedThisWeek = report.FetchedAt >= start;
            return Period(replies, start, resetsAt, resetIsEstimate: resetsAt != lastKnownReset,
                reportedThisWeek ? limit.Percent : null, reportedThisWeek ? report.FetchedAt : null);
        }

        return Period(replies, now - WeekLength, resetsAt: null, resetIsEstimate: false, percent: null, percentAsOf: null);
    }

    private static ClaudeUsagePeriod Period(
        List<Reply> replies,
        DateTimeOffset start,
        DateTimeOffset? resetsAt,
        bool resetIsEstimate,
        int? percent,
        DateTimeOffset? percentAsOf)
    {
        var inPeriod = replies.Where(reply => reply.Time >= start).ToList();
        return new ClaudeUsagePeriod(
            start.ToLocalTime(),
            resetsAt?.ToLocalTime(),
            resetIsEstimate,
            inPeriod.Sum(reply => reply.Cost ?? 0m),
            inPeriod.Sum(reply => reply.Tokens.Total),
            percent,
            percentAsOf?.ToLocalTime());
    }

    private void ReadNewLines(LogFile file, FileInfo info, DateTimeOffset cutoff)
    {
        if (info.Length < file.Offset)
        {
            // The log was replaced. Replies already counted are skipped by their IDs.
            file.Offset = 0;
        }

        if (info.Length == file.Offset)
        {
            return;
        }

        using var stream = new FileStream(info.FullName, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.SequentialScan,
            BufferSize = 0,
        });
        stream.Position = file.Offset;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(ReadChunkBytes);
        int carried = 0;
        try
        {
            while (true)
            {
                if (carried == buffer.Length)
                {
                    // One line is longer than the buffer, such as a large tool result: make room for the rest of it.
                    byte[] larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, carried).CopyTo(larger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = larger;
                }

                int read = stream.Read(buffer, carried, buffer.Length - carried);
                if (read == 0)
                {
                    break;
                }

                int end = carried + read;
                int start = 0;
                int newline;
                while ((newline = buffer.AsSpan(start, end - start).IndexOf((byte)'\n')) >= 0)
                {
                    ReadLine(file, buffer.AsSpan(start, newline), cutoff);
                    start += newline + 1;
                }

                // An unfinished last line stays for the next refresh, since Claude Code may still be writing it.
                file.Offset += start;
                carried = end - start;
                buffer.AsSpan(start, carried).CopyTo(buffer);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void ReadLine(LogFile file, ReadOnlySpan<byte> line, DateTimeOffset cutoff)
    {
        // Most lines are prompts and tool output; only Claude's replies carry usage.
        if (line.IndexOf("\"type\":\"assistant\""u8) < 0 || line.IndexOf("\"usage\""u8) < 0)
        {
            return;
        }

        JsonDocument document;
        try
        {
            var reader = new Utf8JsonReader(line);
            document = JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || GetString(root, "type") != "assistant"
                || !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("timestamp", out var timestamp) || timestamp.ValueKind != JsonValueKind.String
                || !timestamp.TryGetDateTimeOffset(out var time))
            {
                return;
            }

            NoteWorkingDirectory(file, GetString(root, "cwd"));

            // Claude Code logs its own error messages as replies from "<synthetic>", with no real usage.
            string model = GetString(message, "model");
            if (time < cutoff || model.Length == 0 || model.StartsWith('<'))
            {
                return;
            }

            long cacheWrites = GetLong(usage, "cache_creation_input_tokens");
            long oneHourWrites = usage.TryGetProperty("cache_creation", out var creation) && creation.ValueKind == JsonValueKind.Object
                ? Math.Min(cacheWrites, GetLong(creation, "ephemeral_1h_input_tokens"))
                : 0;
            var tokens = new ClaudeTokens(
                GetLong(usage, "input_tokens"),
                GetLong(usage, "output_tokens"),
                cacheWrites - oneHourWrites,
                oneHourWrites,
                GetLong(usage, "cache_read_input_tokens"));
            var reply = new Reply(
                time,
                model,
                GetString(root, "sessionId"),
                file,
                tokens,
                ClaudeModels.Cost(model, tokens, fast: GetString(usage, "speed") == "fast", usOnly: GetString(usage, "inference_geo") == "us"));

            string messageId = GetString(message, "id");
            string key = messageId.Length > 0
                ? $"{messageId}:{GetString(root, "requestId")}"
                : $"{reply.SessionId}:{time.UtcTicks}";

            // Keep the most complete copy: a reply logged while still streaming can show fewer output tokens.
            if (!_replies.TryGetValue(key, out var existing) || existing.Tokens.Output < tokens.Output)
            {
                _replies[key] = reply;
            }
        }
    }

    // Claude Code names each project's log folder after the project's path, with every character other than a letter or
    // digit replaced by "-". The working directory that matches gives the folder's real name, like "web-portfolio".
    private static void NoteWorkingDirectory(LogFile file, string workingDirectory)
    {
        if (file.ProjectName is not null || workingDirectory.Length == 0)
        {
            return;
        }

        string name = Path.GetFileName(workingDirectory.TrimEnd('\\', '/'));
        file.FirstFolderName ??= name;

        string encoded = string.Create(workingDirectory.Length, workingDirectory, (span, path) =>
        {
            for (int i = 0; i < path.Length; i++)
            {
                span[i] = char.IsAsciiLetterOrDigit(path[i]) ? path[i] : '-';
            }
        });

        if (encoded.Equals(file.ProjectFolder, StringComparison.OrdinalIgnoreCase))
        {
            file.ProjectName = name;
        }
    }

    private static IEnumerable<FileInfo> EnumerateLogs(string directory, DateTimeOffset cutoff)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        try
        {
            // Logs untouched since the cutoff can't hold replies recent enough to show.
            return new DirectoryInfo(directory)
                .EnumerateFiles("*.jsonl", options)
                .Where(info => info.LastWriteTimeUtc >= cutoff.UtcDateTime)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Logs sit in a project's folder directly, or deeper for subagents; the first folder below "projects" is the project.
    private static string ProjectFolderOf(string directory, string path) =>
        Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar)[0];

    private static DateOnly LocalDate(DateTimeOffset time) => DateOnly.FromDateTime(time.LocalDateTime);

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static long GetLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)
            ? number
            : 0;

    private sealed class LogFile(string projectFolder)
    {
        /// <summary>The log's folder under "projects", named after the project's path.</summary>
        public string ProjectFolder { get; } = projectFolder;

        /// <summary>Bytes read so far, always ending after a complete line.</summary>
        public long Offset { get; set; }

        /// <summary>The project's folder name, once a reply's working directory matches <see cref="ProjectFolder"/>.</summary>
        public string? ProjectName { get; set; }

        /// <summary>The folder of the first working directory seen, in case none matches.</summary>
        public string? FirstFolderName { get; set; }

        public string DisplayName => ProjectName ?? FirstFolderName ?? ProjectFolder;
    }

    private sealed record Reply(DateTimeOffset Time, string Model, string SessionId, LogFile File, ClaudeTokens Tokens, decimal? Cost);
}
