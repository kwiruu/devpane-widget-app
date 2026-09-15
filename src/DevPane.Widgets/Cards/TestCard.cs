using System.Globalization;
using System.Text.Json.Nodes;
using DevPane.Integrations.SystemStats;
using Microsoft.Windows.Widgets.Providers;

namespace DevPane.Widgets.Cards;

/// <summary>
/// M0 proof-of-concept card. It answers four questions: how often the board accepts updates,
/// whether button actions arrive, whether data-URI images render, and whether state survives a restart.
/// </summary>
internal sealed class TestCard : CardBase
{
    public const string DefinitionId = "TestCard";

    private const string IncrementVerb = "increment";
    private const int HistoryLength = 20;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private readonly CpuSampler _cpu = new();
    private readonly Queue<double> _history = new();
    private readonly object _gate = new();
    private Timer? _timer;
    private int _updates;
    private int _clicks;

    public TestCard(WidgetContext context, string? customState)
        : base(context, customState)
    {
        _ = int.TryParse(customState, NumberStyles.Integer, CultureInfo.InvariantCulture, out _clicks);
    }

    protected override string TemplateName => DefinitionId;

    public override void Activate()
    {
        base.Activate();
        lock (_gate)
        {
            _timer ??= new Timer(_ => Tick(), null, Interval, Interval);
        }
    }

    public override void Deactivate()
    {
        base.Deactivate();
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    public override void OnAction(string verb, string data)
    {
        if (verb != IncrementVerb)
        {
            return;
        }

        lock (_gate)
        {
            _clicks++;
            CustomState = _clicks.ToString(CultureInfo.InvariantCulture);
        }

        Push(includeTemplate: false);
    }

    protected override string BuildData()
    {
        lock (_gate)
        {
            return new JsonObject
            {
                ["size"] = SizeName,
                ["cpu"] = _history.Count > 0 ? Math.Round(_history.Last()) : 0,
                ["ram"] = MemoryStats.UsedPercent() ?? 0,
                ["updates"] = _updates,
                ["clicks"] = _clicks,
                ["time"] = DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                ["sparkline"] = Sparkline.ToDataUri(_history),
            }.ToJsonString();
        }
    }

    private void Tick()
    {
        try
        {
            lock (_gate)
            {
                _updates++;
                if (_cpu.Sample() is double cpu)
                {
                    _history.Enqueue(cpu);
                    if (_history.Count > HistoryLength)
                    {
                        _history.Dequeue();
                    }
                }
            }

            Push(includeTemplate: false);
        }
        catch (Exception e)
        {
            Log.Error("Test card update failed", e);
        }
    }
}
