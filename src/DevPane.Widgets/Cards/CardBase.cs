using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace DevPane.Widgets.Cards;

/// <summary>
/// One pinned card on the Widgets Board. Subclasses supply a template name and data; this class sends updates.
/// </summary>
internal abstract class CardBase : IDisposable
{
    private static readonly Dictionary<string, Template> TemplateCache = new();

    protected CardBase(string id, WidgetSize size, string? customState)
    {
        Id = id;
        Size = size;
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

    /// <summary>The user chose Customize from the card's menu. Only called for definitions marked IsCustomizable.</summary>
    public virtual void OnCustomizationRequested()
    {
    }

    public virtual void OnContextChanged(WidgetContext context)
    {
        Size = context.Size;
        Push(includeTemplate: false);
    }

    public void Push(bool includeTemplate)
    {
        var (template, data) = Render();
        var options = new WidgetUpdateRequestOptions(Id)
        {
            Data = data,
            CustomState = CustomState,
        };

        if (includeTemplate)
        {
            options.Template = template;
        }

        WidgetManager.GetDefault().UpdateWidget(options);
    }

    /// <summary>The template and data this card would send, without sending them. Also draws the preview images.</summary>
    internal (string Template, string Data) Render()
    {
        string data = BuildData();
        var template = LoadTemplate(TemplateName);
        return (template.Json, CardSanitizer.Clean(data, template.Fields));
    }

    public virtual void Dispose() => Deactivate();

    private static Template LoadTemplate(string name)
    {
        lock (TemplateCache)
        {
            if (!TemplateCache.TryGetValue(name, out var template))
            {
                string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Templates", name + ".json"));
                template = new Template(json, CardSanitizer.FindFields(json));
                TemplateCache[name] = template;
            }

            return template;
        }
    }

    /// <param name="Fields">What <see cref="CardSanitizer"/> checks in the data sent with this template.</param>
    private sealed record Template(string Json, TemplateFields Fields);
}
