using Microsoft.Windows.Widgets.Providers;

namespace DevPane.Widgets.Cards;

/// <summary>
/// One pinned card on the Widgets Board. Subclasses supply a template name and data; this class sends updates.
/// </summary>
internal abstract class CardBase : IDisposable
{
    private static readonly Dictionary<string, string> TemplateCache = new();

    protected CardBase(WidgetContext context, string? customState)
    {
        Id = context.Id;
        Size = context.Size;
        CustomState = customState ?? string.Empty;
    }

    public string Id { get; }

    protected WidgetSize Size { get; private set; }

    /// <summary>"small", "medium" or "large", for <c>$when</c> conditions in templates.</summary>
    protected string SizeName => Size.ToString().ToLowerInvariant();

    /// <summary>Saved by Windows with the card, and handed back after a provider restart.</summary>
    protected string CustomState { get; set; }

    protected bool IsActive { get; private set; }

    /// <summary>File name, without extension, in the Templates folder.</summary>
    protected abstract string TemplateName { get; }

    protected abstract string BuildData();

    /// <summary>The Widgets Board opened and this card is visible.</summary>
    public virtual void Activate()
    {
        IsActive = true;
        Push(includeTemplate: true);
    }

    /// <summary>The Widgets Board closed. Stop all background work.</summary>
    public virtual void Deactivate() => IsActive = false;

    public virtual void OnAction(string verb, string data)
    {
    }

    public virtual void OnContextChanged(WidgetContext context)
    {
        Size = context.Size;
        Push(includeTemplate: false);
    }

    public void Push(bool includeTemplate)
    {
        var options = new WidgetUpdateRequestOptions(Id)
        {
            Data = BuildData(),
            CustomState = CustomState,
        };

        if (includeTemplate)
        {
            options.Template = LoadTemplate(TemplateName);
        }

        WidgetManager.GetDefault().UpdateWidget(options);
    }

    public virtual void Dispose() => Deactivate();

    private static string LoadTemplate(string name)
    {
        lock (TemplateCache)
        {
            if (!TemplateCache.TryGetValue(name, out string? template))
            {
                template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Templates", name + ".json"));
                TemplateCache[name] = template;
            }

            return template;
        }
    }
}
