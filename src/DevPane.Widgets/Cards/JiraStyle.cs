using DevPane.Integrations.Jira;

namespace DevPane.Widgets.Cards;

/// <summary>
/// Jira's look for the Jira card, in the colors of Atlassian's design system: issue type icons, priority arrows, status
/// lozenges, and the blue button, drawn with <see cref="CardStyle"/>. The icons are drawn for Dev Pane in Jira's colors,
/// not copied from Jira, and there's no Atlassian logo.
/// </summary>
internal static class JiraStyle
{
    // Atlassian's brand blue for buttons: darker on light cards, lighter on dark cards.
    private const string BlueOnLight = "#0C66E4";
    private const string BlueOnDark = "#579DFF";

    // Keep in sync with the template's lozenge columns: 6px ends, 20px high.
    private const int LozengeEndWidth = 6;
    private const int LozengeHeight = 20;
    private const int LozengeRadius = 3;

    private static readonly (string Left, string Fill, string Right) ButtonOnLight = CardStyle.Button(BlueOnLight);
    private static readonly (string Left, string Fill, string Right) ButtonOnDark = CardStyle.Button(BlueOnDark);

    /// <summary>The icon on the connect screen: a blue tile with a white check, like a Jira task.</summary>
    public static string Mark { get; } = CardStyle.DataUri(
        "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40' viewBox='0 0 40 40'><rect width='40' height='40' rx='9' fill='#1868DB'/><path d='M12 20.5l5.5 5.5 10.5-11' fill='none' stroke='#FFFFFF' stroke-width='3.4' stroke-linecap='round' stroke-linejoin='round'/></svg>");

    public static (string Left, string Fill, string Right) Button(bool light) => light ? ButtonOnLight : ButtonOnDark;

    /// <summary>The button label's color: white on the darker blue, dark on the lighter blue.</summary>
    public static string ButtonTextColor(bool light) => light ? "light" : "dark";

    /// <summary>
    /// A 16px icon for an issue type, in Jira's colors: a red bug, a purple epic, a green story, and a blue task or subtask.
    /// </summary>
    public static string IssueTypeIcon(JiraIssue issue)
    {
        string name = issue.IssueType.ToLowerInvariant();
        var (color, glyph) = (issue.HierarchyLevel, name) switch
        {
            (>= 1, _) => ("#904EE2", "<path d='M9.2 3.5 5.5 8.6h2.8L6.8 12.5l3.9-5.3H7.9z' fill='#FFFFFF'/>"),
            _ when name.Contains("bug", StringComparison.Ordinal) => ("#E5493A", "<circle cx='8' cy='8' r='3' fill='#FFFFFF'/>"),
            _ when name.Contains("story", StringComparison.Ordinal) => ("#63BA3C", "<path d='M5.5 4h5v8L8 10.3 5.5 12z' fill='#FFFFFF'/>"),
            (< 0, _) => ("#4BADE8", "<rect x='4.5' y='4.5' width='3' height='3' rx='0.5' fill='#FFFFFF'/><rect x='8.5' y='8.5' width='3' height='3' rx='0.5' fill='#FFFFFF'/>"),
            _ => ("#4BADE8", "<path d='M5 8.2l2 2 4-4.2' fill='none' stroke='#FFFFFF' stroke-width='1.6' stroke-linecap='round' stroke-linejoin='round'/>"),
        };

        return CardStyle.DataUri($"<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 16 16'><rect width='16' height='16' rx='3' fill='{color}'/>{glyph}</svg>");
    }

    /// <summary>
    /// A 14px priority icon in Jira's style: red arrows up for the highest priorities, orange lines for medium, and blue
    /// arrows down for low. Empty when the site doesn't use priorities.
    /// </summary>
    public static string PriorityIcon(string? priority)
    {
        string? shape = priority?.ToLowerInvariant() switch
        {
            "highest" or "blocker" or "critical" =>
                "<path d='M3 7.5 7 3.5l4 4M3 11.5l4-4 4 4' fill='none' stroke='#FF5630' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'/>",
            "high" or "major" =>
                "<path d='M3 9 7 5l4 4' fill='none' stroke='#FF7452' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'/>",
            "medium" =>
                "<path d='M3 5.5h8M3 8.5h8' stroke='#FFAB00' stroke-width='1.8' stroke-linecap='round'/>",
            "low" or "minor" =>
                "<path d='M3 5l4 4 4-4' fill='none' stroke='#2684FF' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'/>",
            "lowest" or "trivial" =>
                "<path d='M3 2.5l4 4 4-4M3 6.5l4 4 4-4' fill='none' stroke='#0065FF' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'/>",
            _ => null,
        };

        return shape is null
            ? string.Empty
            : CardStyle.DataUri($"<svg xmlns='http://www.w3.org/2000/svg' width='14' height='14' viewBox='0 0 14 14'>{shape}</svg>");
    }

    /// <summary>
    /// A status lozenge's background, like Jira's: gray for to do, blue for in progress, green for done. Its end images are
    /// 6px wide with Jira's small 3px corners.
    /// </summary>
    public static (string Left, string Fill, string Right) Lozenge(JiraStatusCategory category, bool light)
    {
        string paint = (category, light) switch
        {
            (JiraStatusCategory.InProgress, true) => "fill='#E9F2FF'",
            (JiraStatusCategory.InProgress, false) => "fill='#1C2B41'",
            (JiraStatusCategory.Done, true) => "fill='#DCFFF1'",
            (JiraStatusCategory.Done, false) => "fill='#1C3329'",
            (_, true) => "fill='#091E42' fill-opacity='0.06'",
            (_, false) => "fill='#A1BDD9' fill-opacity='0.08'",
        };

        return (
            CardStyle.EndImage(paint, LozengeEndWidth, LozengeHeight, left: true, LozengeRadius),
            CardStyle.FillImage(paint),
            CardStyle.EndImage(paint, LozengeEndWidth, LozengeHeight, left: false, LozengeRadius));
    }

    /// <summary>The Adaptive Cards text color closest to a lozenge's text.</summary>
    public static string LozengeTextColor(JiraStatusCategory category) => category switch
    {
        JiraStatusCategory.InProgress => "accent",
        JiraStatusCategory.Done => "good",
        _ => "default",
    };
}
