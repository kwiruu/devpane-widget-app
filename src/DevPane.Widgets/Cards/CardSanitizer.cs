using System.Buffers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DevPane.Widgets.Cards;

/// <summary>
/// The fields a template uses in ways that matter for safety, as data paths such as "sections.items.title".
/// </summary>
/// <param name="Text">Shown as TextBlock text, which the Widgets Board reads as markdown.</param>
/// <param name="Links">Opened by an Action.OpenUrl, as the start of its address.</param>
internal sealed record TemplateFields(IReadOnlySet<string> Text, IReadOnlySet<string> Links);

/// <summary>
/// Cleans card data on its way to the Widgets Board. Much of what the cards show is written by other people: pull
/// request and issue titles, commit messages, build logs, Jira summaries. So whatever card it's in and wherever it
/// came from, data is held to two rules before it's sent:
/// <list type="bullet">
/// <item>Text shows exactly as written. The board reads every TextBlock as markdown, so unescaped, a pull request titled
/// <c>[Get the fix](https://…)</c> would be a link on the card, and <c>![](https://…)</c> an image the board fetches from
/// wherever it points.</item>
/// <item>Links are web pages. A link that isn't http or https, such as a file or another app's protocol, is dropped
/// rather than handed to Windows to open.</item>
/// </list>
/// </summary>
/// <remarks>
/// Only TextBlock text is markdown. Choice titles in compact ChoiceSets, action titles, alt text and input values are
/// shown as plain text and pass through as they are.
/// </remarks>
internal static partial class CardSanitizer
{
    // Inline markdown can start at any of these, anywhere in a line: links and images ([ ]), autolinks (< >), emphasis,
    // code and strikethrough (* _ ` ~), tables (|), entities (&), headings (#), the board's {{DATE()}} functions ({ }),
    // and the backslash itself, so text can't cancel an escape. Each one, backslashed, shows as itself.
    private static readonly SearchValues<char> Inline = SearchValues.Create("\\[]<>*_`~|&#{}");

    /// <exception cref="InvalidOperationException">The template binds something these rules can't follow.</exception>
    public static TemplateFields FindFields(string template)
    {
        var text = new HashSet<string>(StringComparer.Ordinal);
        var links = new HashSet<string>(StringComparer.Ordinal);
        Collect(JsonNode.Parse(template), scope: string.Empty, text, links);

        // Escaping would break an address, so one field can't be both.
        if (text.Intersect(links).FirstOrDefault() is { } both)
        {
            throw new InvalidOperationException($"\"{both}\" is shown as text and opened as a link. Use a field for each.");
        }

        return new TemplateFields(text, links);
    }

    /// <summary>Escapes the strings in <paramref name="data"/> shown as text, and drops links that aren't web pages.</summary>
    public static string Clean(string data, TemplateFields fields)
    {
        if (JsonNode.Parse(data) is not JsonObject root)
        {
            return data;
        }

        Clean(root, path: string.Empty, fields);
        return root.ToJsonString();
    }

    /// <summary>Backslashes whatever markdown would read as formatting, so <paramref name="text"/> shows as written.</summary>
    public static string EscapeMarkdown(string text)
    {
        var builder = new StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            if (Inline.Contains(c))
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        // Some markers only count at the start of a line: list bullets and heading underlines, and numbered lists.
        string escaped = BlockMarker().Replace(builder.ToString(), @"$1\$2");
        return NumberedListMarker().Replace(escaped, @"$1$2\$3");
    }

    public static bool IsWebLink(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static void Collect(JsonNode? node, string scope, HashSet<string> text, HashSet<string> links)
    {
        if (node is JsonArray items)
        {
            foreach (var item in items)
            {
                Collect(item, scope, text, links);
            }

            return;
        }

        if (node is not JsonObject element)
        {
            return;
        }

        // "$data" repeats or rescopes the element, so its own bindings and its children's are relative to that field.
        if (StringAt(element, "$data") is { } repeat)
        {
            var bindings = Binding().Matches(repeat);
            if (bindings.Count != 1 || bindings[0].Value != repeat.Trim())
            {
                throw new InvalidOperationException($"Can't follow \"$data\": \"{repeat}\". Bind a single field.");
            }

            scope = FieldPath(bindings[0].Groups[1].Value, scope);
        }

        switch (StringAt(element, "type"))
        {
            case "TextBlock" when StringAt(element, "text") is { } shown:
                foreach (Match binding in Binding().Matches(shown))
                {
                    text.Add(FieldPath(binding.Groups[1].Value, scope));
                }

                break;

            // Only the start of an address decides what opens it. "https://github.com/${login}" is always a web page.
            case "Action.OpenUrl" when StringAt(element, "url") is { } url && Binding().Match(url) is { Success: true, Index: 0 } start:
                links.Add(FieldPath(start.Groups[1].Value, scope));
                break;
        }

        foreach (var (_, child) in element)
        {
            if (child is JsonObject or JsonArray)
            {
                Collect(child, scope, text, links);
            }
        }
    }

    private static string FieldPath(string expression, string scope)
    {
        expression = expression.Trim();
        if (!Field().IsMatch(expression))
        {
            // Anything cleverer than a field could build a value these rules can't see, and it would go out unchecked.
            throw new InvalidOperationException($"Can't tell which field \"${{{expression}}}\" uses. Bind a plain field instead.");
        }

        const string Root = "$root.";
        return expression.StartsWith(Root, StringComparison.Ordinal) ? expression[Root.Length..]
            : scope.Length == 0 ? expression
            : $"{scope}.{expression}";
    }

    private static void Clean(JsonObject element, string path, TemplateFields fields)
    {
        // Snapshot the properties: replacing a value while enumerating would invalidate the enumerator.
        foreach (var (name, value) in element.ToList())
        {
            string field = path.Length == 0 ? name : $"{path}.{name}";
            switch (value)
            {
                case JsonValue text when text.TryGetValue(out string? s) && fields.Text.Contains(field):
                    element[name] = EscapeMarkdown(s);
                    break;
                case JsonValue link when link.TryGetValue(out string? address) && fields.Links.Contains(field) && !IsWebLink(address):
                    Log.Info($"Dropped a link in {field} that wasn't a web address");
                    element[name] = string.Empty;
                    break;
                case JsonObject child:
                    Clean(child, field, fields);
                    break;
                case JsonArray items:
                    // "$data" over an array binds each item in turn, so items share the array's path.
                    foreach (var item in items.OfType<JsonObject>())
                    {
                        Clean(item, field, fields);
                    }

                    break;
            }
        }
    }

    private static string? StringAt(JsonObject element, string name) =>
        element[name] is JsonValue value && value.TryGetValue(out string? s) ? s : null;

    [GeneratedRegex(@"\$\{([^}]*)\}")]
    private static partial Regex Binding();

    [GeneratedRegex(@"^(\$root\.)?[A-Za-z_]\w*(\.[A-Za-z_]\w*)*$")]
    private static partial Regex Field();

    [GeneratedRegex(@"^([ \t]*)([-+=])", RegexOptions.Multiline)]
    private static partial Regex BlockMarker();

    [GeneratedRegex(@"^([ \t]*)(\d{1,9})([.)])", RegexOptions.Multiline)]
    private static partial Regex NumberedListMarker();
}
