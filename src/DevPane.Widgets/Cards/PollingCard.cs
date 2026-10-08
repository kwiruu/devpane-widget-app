using System.Text.Json.Nodes;
using Microsoft.Windows.Widgets;

namespace DevPane.Widgets.Cards;

/// <summary>
/// A card that samples data on a timer while the Widgets Board is open, and stops when it closes.
/// The latest sample is kept, so reopening the board shows it immediately.
/// </summary>
internal abstract class PollingCard<TSnapshot> : CardBase
    where TSnapshot : class
{
    private readonly object _gate = new();
    private Timer? _timer;
    private TSnapshot? _latest;
    private int _sampling;

    protected PollingCard(string id, WidgetSize size, string? customState)
        : base(id, size, customState)
    {
    }

    protected abstract TimeSpan Interval { get; }

    /// <summary>The most recent sample, or null before the first one.</summary>
    protected TSnapshot? Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    /// <summary>Collects fresh data. Never runs twice at once.</summary>
    protected abstract ValueTask<TSnapshot> TakeSampleAsync();

    /// <summary>Builds the template data. <paramref name="snapshot"/> is null before the first sample.</summary>
    protected abstract JsonObject Describe(TSnapshot? snapshot);

    public override void Activate()
    {
        base.Activate();
        lock (_gate)
        {
            _timer ??= new Timer(_ => _ = SampleAsync(), null, TimeSpan.Zero, Interval);
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

    /// <summary>Shows <paramref name="snapshot"/> as if it had just been sampled. For drawing preview images.</summary>
    internal void UseSample(TSnapshot snapshot)
    {
        lock (_gate)
        {
            _latest = snapshot;
        }
    }

    /// <summary>Samples right away instead of waiting for the next tick, for example when the user asks to refresh.</summary>
    protected void RefreshNow() => _ = SampleAsync();

    /// <summary>False to skip updating the card after a sample, for example while the user types in its inputs.</summary>
    protected virtual bool PushAfterSample => true;

    /// <summary>Runs after each sample is stored, just before the card is updated.</summary>
    protected virtual void OnSampleTaken()
    {
    }

    protected sealed override string BuildData()
    {
        var data = Describe(Latest);
        data["size"] = SizeName;
        return data.ToJsonString();
    }

    private async Task SampleAsync()
    {
        // Skip if the previous sample is still running.
        if (Interlocked.Exchange(ref _sampling, 1) == 1)
        {
            return;
        }

        try
        {
            var snapshot = await TakeSampleAsync().ConfigureAwait(false);
            lock (_gate)
            {
                _latest = snapshot;
            }

            OnSampleTaken();
            if (IsActive && PushAfterSample)
            {
                Push(includeTemplate: false);
            }
        }
        catch (Exception e)
        {
            Log.Error($"{GetType().Name} sample failed", e);
        }
        finally
        {
            Volatile.Write(ref _sampling, 0);
        }
    }
}
