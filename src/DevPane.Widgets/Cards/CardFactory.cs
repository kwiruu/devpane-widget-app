using Microsoft.Windows.Widgets.Providers;

namespace DevPane.Widgets.Cards;

/// <summary>
/// Maps a widget definition ID from Package.appxmanifest to the card class that implements it.
/// </summary>
internal static class CardFactory
{
    public static CardBase? Create(WidgetContext context, string? customState) => context.DefinitionId switch
    {
        SystemCard.DefinitionId => new SystemCard(context, customState),
        LocalDevCard.DefinitionId => new LocalDevCard(context, customState),
        GitHubCard.DefinitionId => new GitHubCard(context, customState),
        ContributionsCard.DefinitionId => new ContributionsCard(context, customState),
        ClaudeCard.DefinitionId => new ClaudeCard(context, customState),
        VercelCard.DefinitionId => new VercelCard(context, customState),
        _ => null,
    };
}
