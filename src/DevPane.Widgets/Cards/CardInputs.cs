using System.Text.Json.Nodes;

namespace DevPane.Widgets.Cards;

internal static class CardInputs
{
    /// <summary>The text values of a card's inputs, which arrive with Action.Execute as a JSON object keyed by input ID.</summary>
    public static Dictionary<string, string> Read(string data)
    {
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(data) || JsonNode.Parse(data) is not JsonObject values)
        {
            return inputs;
        }

        foreach (var (key, value) in values)
        {
            if (value is JsonValue jsonValue && jsonValue.TryGetValue(out string? text))
            {
                inputs[key] = text;
            }
        }

        return inputs;
    }
}
