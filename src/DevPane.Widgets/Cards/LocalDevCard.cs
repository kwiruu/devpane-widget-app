using System.Globalization;
using System.Text.Json.Nodes;
using DevPane.Integrations.LocalDev;
using Microsoft.Windows.Widgets;

namespace DevPane.Widgets.Cards;

/// <summary>
/// Dev servers listening on this machine, plus developer tool memory and WSL/Docker VM memory on the large size.
/// </summary>
internal sealed class LocalDevCard : PollingCard<LocalDevSnapshot>
{
    public const string DefinitionId = "LocalDev";

    private const int MaxProcessGroups = 3;

    // The end columns of a server row, and the row's height with and without the address under the port.
    private const int RowEndWidth = 8;
    private const int RowHeightWithUrl = 46;
    private const int RowHeightPortOnly = 30;

    public LocalDevCard(string id, WidgetSize size, string? customState)
        : base(id, size, customState)
    {
    }

    protected override string TemplateName => DefinitionId;

    // Ports and processes change slowly, and a process snapshot costs more than a counter read.
    protected override TimeSpan Interval => TimeSpan.FromSeconds(10);

    protected override ValueTask<LocalDevSnapshot> TakeSampleAsync() => ValueTask.FromResult(LocalDevSampler.Sample());

    protected override JsonObject Describe(LocalDevSnapshot? snapshot)
    {
        IReadOnlyList<DevServer> servers = snapshot?.Servers ?? [];
        bool light = WindowsTheme.IsLight();

        // The small size has room for the ports alone; the taller ones also show what to open.
        bool showUrls = Size != WidgetSize.Small;
        int maxServers = Size == WidgetSize.Small ? 2 : 4;
        int rowHeight = showUrls ? RowHeightWithUrl : RowHeightPortOnly;

        var serverItems = new JsonArray();
        foreach (var server in servers.Take(maxServers))
        {
            serverItems.Add(new JsonObject
            {
                ["port"] = $":{server.Port}",
                ["name"] = server.Name,
                ["exposed"] = server.IsExposed,
                ["url"] = $"http://localhost:{server.Port}",
            });
        }

        // The memory bars are drawn as two columns whose widths are weights, so they read as a share of the largest.
        var groups = (snapshot?.Processes ?? []).Take(MaxProcessGroups).ToList();
        ulong largest = groups.Count > 0 ? groups.Max(group => group.MemoryBytes) : 0;

        var processItems = new JsonArray();
        foreach (var group in groups)
        {
            int share = largest > 0 ? Math.Max(4, (int)(group.MemoryBytes * 100 / largest)) : 0;
            processItems.Add(new JsonObject
            {
                ["name"] = group.Count > 1 ? $"{group.Name} ×{group.Count}" : group.Name,
                ["memory"] = Format.Memory(group.MemoryBytes),
                ["share"] = share.ToString(CultureInfo.InvariantCulture),
                ["rest"] = (100 - share).ToString(CultureInfo.InvariantCulture),
            });
        }

        var row = CardStyle.Surface(LabelTone.Neutral, light, RowEndWidth, rowHeight);
        string accent = CardStyle.ToneColor(LabelTone.Accent, light);

        return new JsonObject
        {
            ["summary"] = snapshot is null
                ? "Looking for dev servers…"
                : servers.Count switch
                {
                    0 => "No dev servers running",
                    1 => "1 dev server running",
                    var count => $"{count} dev servers running",
                },
            ["servers"] = serverItems,
            ["showUrls"] = showUrls,
            ["rowHeight"] = $"{rowHeight}px",
            ["rowLeft"] = row.Left,
            ["rowFill"] = row.Fill,
            ["rowRight"] = row.Right,
            ["dot"] = CardStyle.DataUri(
                $"<svg xmlns='http://www.w3.org/2000/svg' width='6' height='6'><circle cx='3' cy='3' r='3' fill='{CardStyle.ToneColor(LabelTone.Success, light)}'/></svg>"),
            ["barFill"] = CardStyle.FillImage($"fill='{accent}'"),
            ["barTrack"] = CardStyle.FillImage(light ? "fill='#1F2328' fill-opacity='0.10'" : "fill='#FFFFFF' fill-opacity='0.12'"),
            ["moreServers"] = servers.Count > maxServers ? $"+{servers.Count - maxServers} more" : string.Empty,
            ["processes"] = processItems,
            ["hasProcesses"] = processItems.Count > 0,
            ["vm"] = snapshot?.VirtualMachineMemoryBytes is ulong vm ? Format.Memory(vm) : "Not running",
        };
    }
}
