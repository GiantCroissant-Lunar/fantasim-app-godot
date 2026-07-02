using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.World.Composition;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>
/// Stateful, asynchronous trigger that runs the crust-generation pipeline when the timeline
/// playhead enters or scrubs within the mobile-plate regime. Caches completed runs by
/// (graph revision, tick window) so repeated scrubbing does not re-run the pipeline, and
/// cancels in-flight work when the playhead leaves the regime.
/// </summary>
public sealed class CrustGenerationTrigger : IDisposable
{
    private readonly ITimelineController _timeline;
    private readonly CrustGenerationTriggerPolicy _policy;
    private readonly int _graphRevision;
    private readonly Func<long, int, CancellationToken, Task> _execute;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly HashSet<CrustGenerationTriggerKey> _completed = new();

    private CrustGenerationTriggerKey? _inFlightKey;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public CrustGenerationTrigger(
        ITimelineController timeline,
        CrustGenerationTriggerPolicy policy,
        int graphRevision,
        Func<long, int, CancellationToken, Task> execute,
        ILoggerFactory? loggerFactory = null)
    {
        _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _graphRevision = graphRevision;
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<CrustGenerationTrigger>();
    }

    /// <summary>
    /// Subscribe to timeline tick changes and evaluate the current playhead position immediately.
    /// </summary>
    public void Start()
    {
        ThrowIfDisposed();
        _timeline.TickChanged += OnTickChanged;
        OnTickChanged(_timeline.Tick);
    }

    /// <summary>
    /// Unsubscribe from timeline tick changes and cancel any in-flight generation.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _timeline.TickChanged -= OnTickChanged;
            CancelInFlightLocked();
        }
    }

    private void OnTickChanged(long tick)
    {
        var regime = _timeline.GeosphereSchedule.RegimeAt(tick);
        var decision = _policy.Evaluate(regime?.RegimeId, _graphRevision, tick);

        if (decision.ShouldCancel)
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                CancelInFlightLocked();
            }

            return;
        }

        if (!decision.ShouldRun || decision.Key is null)
            return;

        CancellationToken token;
        lock (_gate)
        {
            if (_disposed)
                return;

            if (_completed.Contains(decision.Key))
                return;

            if (_inFlightKey == decision.Key)
                return;

            _inFlightKey = decision.Key;
            CancelInFlightLocked();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
        }

        _logger.LogInformation(
            "Crust generation triggered: tick={Tick:N0}, canonicalTick={CanonicalTick:N0}, window={WindowIndex}, revision={Revision}",
            tick,
            decision.CanonicalTick,
            decision.Key.WindowIndex,
            decision.Key.GraphRevision);

        _ = RunAsync(decision.Key, decision.CanonicalTick, token);
    }

    private async Task RunAsync(CrustGenerationTriggerKey key, long canonicalTick, CancellationToken cancellationToken)
    {
        try
        {
            await _execute(canonicalTick, key.GraphRevision, cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                if (_disposed)
                    return;

                _completed.Add(key);
            }

            _logger.LogInformation(
                "Crust generation completed: canonicalTick={CanonicalTick:N0}, window={WindowIndex}, revision={Revision}",
                canonicalTick,
                key.WindowIndex,
                key.GraphRevision);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Crust generation cancelled: canonicalTick={CanonicalTick:N0}, window={WindowIndex}",
                canonicalTick,
                key.WindowIndex);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Crust generation failed: canonicalTick={CanonicalTick:N0}, window={WindowIndex}",
                canonicalTick,
                key.WindowIndex);
        }
        finally
        {
            lock (_gate)
            {
                if (_inFlightKey == key)
                    _inFlightKey = null;
            }
        }
    }

    private void CancelInFlightLocked()
    {
        if (_cts is not null)
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _cts.Dispose();
            _cts = null;
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CrustGenerationTrigger));
        }
    }
}
