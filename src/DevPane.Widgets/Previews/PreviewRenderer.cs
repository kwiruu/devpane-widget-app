using System.Text.Json.Nodes;
using DevPane.Widgets.Cards;
using Microsoft.Windows.Widgets;

namespace DevPane.Widgets.Previews;

/// <summary>
/// Writes what each card would send to the Widgets Board, at medium size in light and dark, from the made-up data in
/// <see cref="PreviewSamples"/>. tools\render-previews.ps1 draws the widget picker's preview images from these files,
/// so the previews come from the cards' own code and can't drift from what a pinned card looks like.
/// </summary>
/// <remarks>Compiled into Debug builds only: the Store build has no render-previews command.</remarks>
internal static class PreviewRenderer
{
    public const string CommandArgument = "render-previews";

    // The picker shows each card at medium size, so that's the only size drawn.
    private static readonly (string DefinitionId, Func<CardBase> Create)[] Cards =
    [
        (SystemCard.DefinitionId, () => Sampled(new SystemCard(IdFor(SystemCard.DefinitionId), WidgetSize.Medium, null), PreviewSamples.System)),
        (LocalDevCard.DefinitionId, () => Sampled(new LocalDevCard(IdFor(LocalDevCard.DefinitionId), WidgetSize.Medium, null), PreviewSamples.LocalDev)),
        (GitHubCard.DefinitionId, () => Sampled(new GitHubCard(IdFor(GitHubCard.DefinitionId), WidgetSize.Medium, null), PreviewSamples.GitHub)),
        (ContributionsCard.DefinitionId, () => Sampled(new ContributionsCard(IdFor(ContributionsCard.DefinitionId), WidgetSize.Medium, null), PreviewSamples.Contributions)),
        (ClaudeCard.DefinitionId, () => Sampled(new ClaudeCard(IdFor(ClaudeCard.DefinitionId), WidgetSize.Medium, null), PreviewSamples.Claude)),
        (VercelCard.DefinitionId, () => Sampled(new VercelCard(IdFor(VercelCard.DefinitionId), WidgetSize.Medium, null), PreviewSamples.Vercel)),
        (JiraCard.DefinitionId, () => Sampled(new JiraCard(IdFor(JiraCard.DefinitionId), WidgetSize.Medium, null), PreviewSamples.Jira)),
    ];

    /// <summary>Writes <c>{definition}.{light|dark}.json</c> files into <paramref name="folder"/>.</summary>
    /// <returns>The process exit code: 0 on success.</returns>
    public static int Run(string folder)
    {
        // Before anything touches a session, or the first touch would load the saved account.
        PreviewMode.Enter();
        PreviewMode.GitHub = PreviewSamples.GitHubAccount;
        PreviewMode.Vercel = PreviewSamples.VercelAccount;
        PreviewMode.Jira = PreviewSamples.JiraAccount;
        PreviewMode.JiraClient = PreviewSamples.JiraClient;

        Directory.CreateDirectory(folder);
        try
        {
            foreach (bool light in new[] { false, true })
            {
                PreviewMode.Light = light;
                string theme = light ? "light" : "dark";
                foreach (var (definitionId, create) in Cards)
                {
                    using var card = create();
                    var (template, data) = card.Render();
                    var file = new JsonObject
                    {
                        ["definition"] = definitionId,
                        ["theme"] = theme,
                        ["template"] = JsonNode.Parse(template),
                        ["data"] = JsonNode.Parse(data),
                    };

                    File.WriteAllText(Path.Combine(folder, $"{definitionId}.{theme}.json"), file.ToJsonString());
                }
            }

            return 0;
        }
        catch (Exception e)
        {
            // The provider is a windowed app with no console, so the script reads the failure from this file.
            File.WriteAllText(Path.Combine(folder, "error.txt"), e.ToString());
            return 1;
        }
    }

    private static string IdFor(string definitionId) => $"preview-{definitionId}";

    private static CardBase Sampled<TSnapshot>(PollingCard<TSnapshot> card, TSnapshot sample)
        where TSnapshot : class
    {
        card.UseSample(sample);
        return card;
    }
}
