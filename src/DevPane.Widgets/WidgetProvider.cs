using DevPane.Widgets.Cards;
using Microsoft.Windows.Widgets.Providers;

namespace DevPane.Widgets;

/// <summary>
/// The object the Widgets Board talks to. Each call is routed to the card that owns the widget ID.
/// </summary>
internal sealed partial class WidgetProvider : IWidgetProvider, IWidgetProvider2
{
    /// <summary>
    /// COM class ID. Must match both <c>com:Class Id</c> and <c>CreateInstance ClassId</c> in Package.appxmanifest.
    /// </summary>
    public const string Clsid = "0486BD7C-EC81-47F7-80BB-289792540301";

    // Windows can create more than one provider object, so pinned cards live in one shared registry.
    private static readonly Dictionary<string, CardBase> Cards = new();
    private static readonly object Gate = new();
    private static readonly ManualResetEvent NoCardsLeftEvent = new(false);
    private static bool _restored;

    public WidgetProvider()
    {
        lock (Gate)
        {
            if (_restored)
            {
                return;
            }

            _restored = true;
            Guard("restore", () =>
            {
                // Cards pinned before this process started (after a restart or crash).
                foreach (var info in WidgetManager.GetDefault().GetWidgetInfos())
                {
                    var context = info.WidgetContext;
                    if (CardFactory.Create(context, info.CustomState) is { } card)
                    {
                        Cards[context.Id] = card;
                        Log.Info($"Restored {context.DefinitionId} ({context.Id})");
                    }
                }
            });
        }
    }

    /// <summary>Signaled when the user removes the last Dev Pane card.</summary>
    public static WaitHandle NoCardsLeft => NoCardsLeftEvent;

    public void CreateWidget(WidgetContext widgetContext) => Guard(nameof(CreateWidget), () =>
    {
        var card = CardFactory.Create(widgetContext, customState: null);
        if (card is null)
        {
            Log.Error($"No card for definition {widgetContext.DefinitionId}");
            return;
        }

        CardBase? previous;
        lock (Gate)
        {
            // The constructor's restore pass may already hold a card for this ID.
            Cards.Remove(widgetContext.Id, out previous);
            Cards[widgetContext.Id] = card;
            NoCardsLeftEvent.Reset();
        }

        previous?.Dispose();
        Log.Info($"Pinned {widgetContext.DefinitionId} ({widgetContext.Id}), size {widgetContext.Size}");
        card.Push(includeTemplate: true);
    });

    public void DeleteWidget(string widgetId, string customState) => Guard(nameof(DeleteWidget), () =>
    {
        CardBase? card;
        lock (Gate)
        {
            if (Cards.Remove(widgetId, out card) && Cards.Count == 0)
            {
                NoCardsLeftEvent.Set();
            }
        }

        card?.Dispose();
        Log.Info($"Removed {widgetId}");
    });

    public void Activate(WidgetContext widgetContext) => Guard(nameof(Activate), () =>
        Find(widgetContext.Id)?.Activate());

    public void Deactivate(string widgetId) => Guard(nameof(Deactivate), () =>
        Find(widgetId)?.Deactivate());

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs) => Guard(nameof(OnActionInvoked), () =>
        Find(actionInvokedArgs.WidgetContext.Id)?.OnAction(actionInvokedArgs.Verb, actionInvokedArgs.Data));

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs) => Guard(nameof(OnWidgetContextChanged), () =>
        Find(contextChangedArgs.WidgetContext.Id)?.OnContextChanged(contextChangedArgs.WidgetContext));

    public void OnCustomizationRequested(WidgetCustomizationRequestedArgs customizationRequestedArgs) => Guard(nameof(OnCustomizationRequested), () =>
        Find(customizationRequestedArgs.WidgetContext.Id)?.OnCustomizationRequested());

    private static CardBase? Find(string widgetId)
    {
        lock (Gate)
        {
            return Cards.GetValueOrDefault(widgetId);
        }
    }

    // An exception escaping into the Widgets Board can end the provider process, so log it instead.
    private static void Guard(string operation, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Log.Error($"{operation} failed", e);
        }
    }
}
