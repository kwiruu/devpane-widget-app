namespace DevPane.Integrations.Claude;

/// <summary>Token counts from one Claude reply, split the way they're priced.</summary>
public readonly record struct ClaudeTokens(long Input, long Output, long CacheWrite5m, long CacheWrite1h, long CacheRead)
{
    public long Total => Input + Output + CacheWrite5m + CacheWrite1h + CacheRead;

    public static ClaudeTokens operator +(ClaudeTokens a, ClaudeTokens b) => new(
        a.Input + b.Input,
        a.Output + b.Output,
        a.CacheWrite5m + b.CacheWrite5m,
        a.CacheWrite1h + b.CacheWrite1h,
        a.CacheRead + b.CacheRead);
}

/// <summary>Claude API list prices, for estimating what usage would cost at API rates.</summary>
public static class ClaudeModels
{
    // USD per million tokens: base input, output, and cache reads, from
    // https://platform.claude.com/docs/en/about-claude/pricing (September 2026). Cache writes cost 1.25x base input for
    // 5 minutes and 2x for 1 hour. Longer prefixes come first, so "claude-opus-4-1" isn't matched as "claude-opus-4".
    private static readonly (string Prefix, decimal Input, decimal Output, decimal CacheRead)[] Prices =
    [
        ("claude-fable-5-1", 10m, 50m, 0.25m),
        ("claude-mythos-5-1", 10m, 50m, 0.25m),
        ("claude-fable-5", 10m, 50m, 1m),
        ("claude-mythos-5", 10m, 50m, 1m),
        ("claude-opus-5", 5m, 25m, 0.50m),
        ("claude-opus-4-8", 5m, 25m, 0.50m),
        ("claude-opus-4-7", 5m, 25m, 0.50m),
        ("claude-opus-4-6", 5m, 25m, 0.50m),
        ("claude-opus-4-5", 5m, 25m, 0.50m),
        ("claude-opus-4-1", 15m, 75m, 1.50m),
        ("claude-opus-4", 15m, 75m, 1.50m),
        ("claude-sonnet-5", 2m, 10m, 0.20m),
        ("claude-sonnet-4-6", 3m, 15m, 0.30m),
        ("claude-sonnet-4-5", 3m, 15m, 0.30m),
        ("claude-sonnet-4", 3m, 15m, 0.30m),
        ("claude-3-7-sonnet", 3m, 15m, 0.30m),
        ("claude-haiku-4-5", 1m, 5m, 0.10m),
        ("claude-3-5-haiku", 0.80m, 4m, 0.08m),
    ];

    /// <summary>
    /// What the tokens would cost at API list prices, or null for a model without a known price.
    /// </summary>
    /// <param name="model">A model ID as Claude Code records it, including cloud forms like <c>us.anthropic.claude-…</c>.</param>
    /// <param name="fast">The reply used fast mode, which costs twice the standard rates.</param>
    /// <param name="usOnly">The reply used US-only inference, which costs 1.1x.</param>
    public static decimal? Cost(string model, ClaudeTokens tokens, bool fast, bool usOnly)
    {
        string id = Normalize(model);
        foreach (var price in Prices)
        {
            if (!id.StartsWith(price.Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            decimal cost = (tokens.Input * price.Input
                + tokens.CacheWrite5m * price.Input * 1.25m
                + tokens.CacheWrite1h * price.Input * 2m
                + tokens.CacheRead * price.CacheRead
                + tokens.Output * price.Output) / 1_000_000m;
            return cost * (fast ? 2m : 1m) * (usOnly ? 1.1m : 1m);
        }

        return null;
    }

    // "us.anthropic.claude-sonnet-4-5-20250929-v1:0" and "claude-sonnet-4-5@20250929" both become "claude-sonnet-4-5-…".
    private static string Normalize(string model)
    {
        string id = model.ToLowerInvariant();
        int start = id.IndexOf("claude-", StringComparison.Ordinal);
        return (start > 0 ? id[start..] : id).Replace('@', '-');
    }
}
