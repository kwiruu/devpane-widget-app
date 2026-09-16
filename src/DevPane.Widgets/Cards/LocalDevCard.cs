using System.Text.Json.Nodes;
using DevPane.Integrations.LocalDev;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace DevPane.Widgets.Cards;

/// <summary>
/// Dev servers listening on this machine, plus developer tool memory and WSL/Docker VM memory on the large size.
/// </summary>
internal sealed class LocalDevCard : PollingCard<LocalDevSnapshot>
{
    public const string DefinitionId = "LocalDev";

    private const int MaxProcessGroups = 3;

    public LocalDevCard(WidgetContext context, string? customState)
        : base(context, customState)
    {
    }

    protected override string TemplateName => DefinitionId;

    // Ports and processes change slowly, and a process snapshot costs more than a counter read.
    protected override TimeSpan Interval => TimeSpan.FromSeconds(10);

    protected override ValueTask<LocalDevSnapshot> TakeSampleAsync() => ValueTask.FromResult(LocalDevSampler.Sample());

    protected override JsonObject Describe(LocalDevSnapshot? snapshot)
    {
        IReadOnlyList<DevServer> servers = snapshot?.Servers ?? [];
        int maxServers = Size == WidgetSize.Small ? 2 : 4;

        var serverItems = new JsonArray();
        foreach (var server in servers.Take(maxServers))
        {
            serverItems.Add(new JsonObject
            {
                ["port"] = $":{server.Port}",
                ["name"] = server.Name,
                ["exposed"] = server.IsExposed,
            });
        }

        var processItems = new JsonArray();
        foreach (var group in (snapshot?.Processes ?? []).Take(MaxProcessGroups))
        {
            processItems.Add(new JsonObject
            {
                ["name"] = group.Count > 1 ? $"{group.Name} ×{group.Count}" : group.Name,
                ["memory"] = Format.Memory(group.MemoryBytes),
            });
        }

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
            ["moreServers"] = servers.Count > maxServers ? $"+{servers.Count - maxServers} more" : string.Empty,
            ["processes"] = processItems,
            ["hasProcesses"] = processItems.Count > 0,
            ["vm"] = snapshot?.VirtualMachineMemoryBytes is ulong vm ? Format.Memory(vm) : "Not running",
        };
    }
}
