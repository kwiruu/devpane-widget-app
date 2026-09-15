using Microsoft.Windows.Widgets.Providers;

namespace DevPane.Widgets.Cards;

/// <summary>
/// Maps a widget definition ID from Package.appxmanifest to the card class that implements it.
/// </summary>
internal static class CardFactory
{
    public static CardBase? Create(WidgetContext context, string? customState) => context.DefinitionId switch
    {
        TestCard.DefinitionId => new TestCard(context, customState),
        _ => null,
    };
}
