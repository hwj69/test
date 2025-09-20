using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Tromby.Infra.Metrics;

/// <summary>
/// In-memory metrics collector with TODO hooks for exporting to Application Insights.
/// </summary>
public sealed class MetricsCollector : IMetricsCollector
{
    private readonly ILogger<MetricsCollector> _logger;
    private readonly ConcurrentDictionary<string, int> _completionCounters = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricsCollector"/> class.
    /// </summary>
    public MetricsCollector(ILogger<MetricsCollector> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void TrackCompletion(string providerName)
    {
        _completionCounters.AddOrUpdate(providerName, 1, (_, current) => current + 1);
        _logger.LogDebug("Completion tracked for {Provider}.", providerName);
    }

    /// <inheritdoc />
    public void TrackModeSwitch(string mode)
    {
        _logger.LogInformation("Mode switched to {Mode}.", mode);
    }

    /// <inheritdoc />
    public void TrackBudgetUsed(string mode, int tokens, decimal cost, int percent, bool softCap, bool hardCap)
    {
        _logger.LogInformation(
            "Budget used in mode {Mode}: tokens={Tokens} cost={Cost} percent={Percent} softCap={SoftCap} hardCap={HardCap}.",
            mode,
            tokens,
            cost,
            percent,
            softCap,
            hardCap);
    }

    /// <inheritdoc />
    public void TrackThrottle(string scope, string reason)
    {
        _logger.LogWarning("Throttle triggered for {Scope}: {Reason}.", scope, reason);
    }

    /// <inheritdoc />
    public void TrackToolInvocation(string toolName)
    {
        _logger.LogDebug("Tool invoked: {Tool}.", toolName);
    }

    /// <inheritdoc />
    public void TrackToolCancelled(string toolName)
    {
        _logger.LogWarning("Tool cancelled: {Tool}.", toolName);
    }
}
