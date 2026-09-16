namespace DevPane.Integrations.Claude;

/// <summary>Claude Code usage on this PC, totaled from its session logs.</summary>
/// <param name="LogsFound">False when Claude Code has never run on this PC for this user.</param>
/// <param name="LastSevenDays">One entry per day, oldest first, ending today.</param>
/// <param name="Session">The current five-hour session, or null when none is running.</param>
/// <param name="Week">The current weekly limit period, or the last seven days when its reset time is unknown.</param>
/// <param name="ProjectsToday">Today's usage by project folder, largest first.</param>
public sealed record ClaudeUsageSummary(
    bool LogsFound,
    ClaudeUsageTotals Today,
    IReadOnlyList<ClaudeDailyUsage> LastSevenDays,
    ClaudeUsagePeriod? Session,
    ClaudeUsagePeriod Week,
    IReadOnlyList<ClaudeProjectUsage> ProjectsToday);

/// <param name="Cost">What the usage would cost at API list prices. Models without a known price add nothing.</param>
/// <param name="Replies">Claude replies, each one API request.</param>
public sealed record ClaudeUsageTotals(decimal Cost, long Tokens, int Replies, int Sessions);

public sealed record ClaudeDailyUsage(DateOnly Date, decimal Cost, long Tokens);

/// <summary>
/// A plan limit period: Claude's usage limits reset every five hours (a session) and every week. Cost and tokens count
/// only Claude Code on this PC, while the limits also count usage elsewhere, like claude.ai.
/// </summary>
/// <param name="ResetsAt">When the limit resets, or null when unknown.</param>
/// <param name="ResetIsEstimate">True when the reset time was worked out from this PC's logs rather than from Claude.</param>
/// <param name="Percent">How much of the limit is used, as Claude last reported it; null when Claude hasn't reported this period.</param>
/// <param name="PercentAsOf">When Claude reported <paramref name="Percent"/>.</param>
public sealed record ClaudeUsagePeriod(
    DateTimeOffset Start,
    DateTimeOffset? ResetsAt,
    bool ResetIsEstimate,
    decimal Cost,
    long Tokens,
    int? Percent,
    DateTimeOffset? PercentAsOf);

public sealed record ClaudeProjectUsage(string Name, decimal Cost, long Tokens);
