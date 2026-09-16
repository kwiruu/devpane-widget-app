using System.Globalization;

namespace DevPane.Widgets.Cards;

/// <summary>
/// Turns raw numbers into the short strings shown on cards, using the user's number format.
/// </summary>
internal static class Format
{
    public const string Missing = "—";

    private const double Megabyte = 1024d * 1024;
    private const double Gigabyte = Megabyte * 1024;

    public static string Percent(double? value) =>
        value is { } v ? string.Create(CultureInfo.CurrentCulture, $"{Math.Round(v):0}%") : Missing;

    /// <summary>Gigabytes without the unit, such as "13.9" or "212".</summary>
    public static string GigabytesNumber(ulong bytes) => Scaled(bytes / Gigabyte, decimalsBelow: 100);

    /// <summary>"1.8 GB", or "410 MB" below a gigabyte.</summary>
    public static string Memory(ulong bytes) =>
        bytes >= Gigabyte
            ? $"{GigabytesNumber(bytes)} GB"
            : string.Create(CultureInfo.CurrentCulture, $"{bytes / Megabyte:0} MB");

    public static string Megabits(double? bytesPerSecond) =>
        bytesPerSecond is { } b ? Scaled(b * 8 / 1_000_000, decimalsBelow: 10) : Missing;

    public static string MegabytesPerSecond(double? bytesPerSecond) =>
        bytesPerSecond is { } b ? Scaled(b / Megabyte, decimalsBelow: 10) : Missing;

    /// <summary>"just now", "5 min ago", or a time of day for anything older than an hour.</summary>
    public static string RelativeTime(DateTimeOffset time)
    {
        var elapsed = DateTimeOffset.Now - time;
        if (elapsed < TimeSpan.FromSeconds(45))
        {
            return "just now";
        }

        return elapsed < TimeSpan.FromHours(1)
            ? $"{Math.Max(1, (int)Math.Round(elapsed.TotalMinutes))} min ago"
            : $"at {time.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}";
    }

    /// <summary>US dollars, such as "$0.42", "$12.40", or "$1.2K".</summary>
    public static string Dollars(decimal amount) => amount >= 1000m
        ? string.Create(CultureInfo.CurrentCulture, $"${amount / 1000m:0.0}K")
        : string.Create(CultureInfo.CurrentCulture, $"${amount:0.00}");

    /// <summary>A token count such as "812", "12.4K", "4.2M", or "1.1B".</summary>
    public static string TokenCount(long tokens) => tokens switch
    {
        >= 1_000_000_000 => string.Create(CultureInfo.CurrentCulture, $"{tokens / 1e9:0.0}B"),
        >= 1_000_000 => string.Create(CultureInfo.CurrentCulture, $"{tokens / 1e6:0.0}M"),
        >= 1_000 => string.Create(CultureInfo.CurrentCulture, $"{tokens / 1e3:0.0}K"),
        _ => tokens.ToString(CultureInfo.CurrentCulture),
    };

    /// <summary>Time left, such as "2h 14m" or "38m".</summary>
    public static string Countdown(TimeSpan remaining) => remaining switch
    {
        { TotalMinutes: < 1 } => "<1m",
        { TotalHours: < 1 } => $"{(int)remaining.TotalMinutes}m",
        _ => $"{(int)remaining.TotalHours}h {remaining.Minutes}m",
    };

    /// <summary>A short age for list rows: "now", "5m", "3h", "2d", "3w", "4mo", or "2y".</summary>
    public static string Age(DateTimeOffset time)
    {
        var elapsed = DateTimeOffset.Now - time;
        return elapsed switch
        {
            { TotalMinutes: < 1 } => "now",
            { TotalHours: < 1 } => $"{(int)elapsed.TotalMinutes}m",
            { TotalDays: < 1 } => $"{(int)elapsed.TotalHours}h",
            { TotalDays: < 7 } => $"{(int)elapsed.TotalDays}d",
            { TotalDays: < 30 } => $"{(int)(elapsed.TotalDays / 7)}w",
            { TotalDays: < 365 } => $"{(int)(elapsed.TotalDays / 30)}mo",
            _ => $"{(int)(elapsed.TotalDays / 365)}y",
        };
    }

    // One decimal for small values, none for large ones, so strings stay short.
    private static string Scaled(double value, double decimalsBelow) =>
        value < decimalsBelow
            ? string.Create(CultureInfo.CurrentCulture, $"{value:0.0}")
            : string.Create(CultureInfo.CurrentCulture, $"{value:0}");
}
