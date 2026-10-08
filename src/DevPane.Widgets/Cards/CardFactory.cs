using Microsoft.Windows.Widgets.Providers;

namespace DevPane.Widgets.Cards;

/// <summary>
/// Maps a widget definition ID from Package.appxmanifest to the card class that implements it.
/// </summary>
internal static class CardFactory
{
    public static CardBase? Create(WidgetContext context, string? customState) => context.DefinitionId switch
    {
        SystemCard.DefinitionId => new SystemCard(context.Id, context.Size, customState),
        LocalDevCard.DefinitionId => new LocalDevCard(context.Id, context.Size, customState),
        GitHubCard.DefinitionId => new GitHubCard(context.Id, context.Size, customState),
        ContributionsCard.DefinitionId => new ContributionsCard(context.Id, context.Size, customState),
        ClaudeCard.DefinitionId => new ClaudeCard(context.Id, context.Size, customState),
        VercelCard.DefinitionId => new VercelCard(context.Id, context.Size, customState),
        JiraCard.DefinitionId => new JiraCard(context.Id, context.Size, customState),
        _ => null,
    };
}
