using System.Globalization;
using System.Text.Json.Nodes;
using DevPane.Integrations.GitHub;
using DevPane.Widgets.GitHub;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace DevPane.Widgets.Cards;

/// <summary>
/// The signed-in user's GitHub contribution calendar with streaks. Small shows only the calendar, medium adds stats
/// over the last 26 weeks, and large shows the whole year as two half-year grids.
/// </summary>
internal sealed class ContributionsCard : GitHubCardBase<ContributionsCard.Reading>
{
    public const string DefinitionId = "GitHubContributions";

    // The grid is stretched to the card's width, so the week count sets its height: fewer weeks make a taller grid.
    // The small card has about 92px below its header (measured from screenshots). After the template's 6px spacer and
    // the 8px gap below it, 26 weeks makes the grid about 71px tall; with fewer, the grid is too tall and the Widgets
    // Board draws nothing.
    private const int SmallWeeks = 26;
    private const int MediumWeeks = 26;
    private const int LargeHalfWeeks = 27;
    private const int LargeOlderWeeks = 26;

    // Contributions change slowly; reopening the board within this window reuses the last result.
    private static readonly TimeSpan MinimumRefreshAge = TimeSpan.FromMinutes(10);

    public ContributionsCard(WidgetContext context, string? customState)
        : base(context, customState)
    {
    }

    internal sealed record Reading(ContributionCalendar? Calendar, DateTimeOffset? UpdatedAt, string? Problem);

    protected override string TemplateName => DefinitionId;

    protected override TimeSpan Interval => TimeSpan.FromMinutes(15);

    protected override async ValueTask<Reading> TakeSampleAsync()
    {
        var latest = Latest;
        if (GitHubSession.Client is not { } client)
        {
            return new Reading(null, null, null);
        }

        if (!ConsumeRefreshRequest() && latest?.UpdatedAt is { } updatedAt && DateTimeOffset.Now - updatedAt < MinimumRefreshAge)
        {
            return latest;
        }

        try
        {
            var calendar = await client.GetContributionCalendarAsync(CancellationToken.None);
            GitHubSession.SetLogin(calendar.Login);
            return new Reading(calendar, DateTimeOffset.Now, null);
        }
        catch (GitHubUnauthorizedException e)
        {
            if (await GitHubSession.HandleUnauthorizedAsync(client, e))
            {
                return new Reading(null, null, null);
            }

            return (latest ?? new Reading(null, null, null)) with
            {
                Problem = "GitHub turned down this refresh. Trying again in a few minutes.",
            };
        }
        catch (Exception e) when (DescribeFailure(e, latest?.Calendar is not null) is { } problem)
        {
            Log.Error("GitHub contributions refresh failed", e);
            return (latest ?? new Reading(null, null, null)) with { Problem = problem };
        }
    }

    protected override JsonObject Describe(Reading? reading)
    {
        var session = GitHubSession.View;
        var data = DescribeSignIn(session);
        if (session.State != GitHubSignInState.SignedIn)
        {
            return data;
        }

        var calendar = reading?.Calendar;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var culture = CultureInfo.CurrentCulture;

        data["valueSize"] = Size == WidgetSize.Small ? "large" : "extraLarge";
        data["total"] = calendar?.TotalContributions.ToString("N0", culture) ?? Format.Missing;
        data["streak"] = calendar?.CurrentStreak(today).ToString(culture) ?? Format.Missing;
        data["longest"] = calendar?.LongestStreak().ToString(culture) ?? Format.Missing;
        data["today"] = calendar?.CountOn(today).ToString(culture) ?? Format.Missing;
        data["login"] = calendar?.Login ?? session.Login ?? string.Empty;
        data["profileUrl"] = calendar is { Login.Length: > 0 } ? $"https://github.com/{calendar.Login}" : "https://github.com";
        data["hasCalendar"] = calendar is not null;
        data["gridSpacing"] = Size == WidgetSize.Large ? "small" : "medium";
        data["gridCaption"] = Size switch
        {
            WidgetSize.Small => string.Empty,
            WidgetSize.Medium => string.Empty,
            _ => "Past year",
        };
        data["footer"] = DescribeFreshness(reading?.UpdatedAt, reading?.Problem);

        bool light = LightTheme;
        if (calendar is not null)
        {
            switch (Size)
            {
                case WidgetSize.Small:
                    data["grid"] = ContributionGrid.ToDataUri(calendar.Days, today, SmallWeeks, light);
                    break;
                case WidgetSize.Medium:
                    data["grid"] = ContributionGrid.ToDataUri(calendar.Days, today, MediumWeeks, light);
                    break;
                default:
                    // Two stacked half-year grids fit the tall large card better than one long, tiny row.
                    data["olderGrid"] = ContributionGrid.ToDataUri(calendar.Days, today, LargeOlderWeeks, light,
                        skipRecentWeeks: LargeHalfWeeks);
                    data["grid"] = ContributionGrid.ToDataUri(calendar.Days, today, LargeHalfWeeks, light);
                    break;
            }
        }

        return data;
    }
}
