namespace Tromby.Infra.Metrics;

/// <summary>
/// Collects telemetry for latency, mode changes, throttling, and resource usage.
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    /// Records completion for a provider.
    /// </summary>
    void TrackCompletion(string providerName);

    /// <summary>
    /// Records a mode switch event.
    /// </summary>
    void TrackModeSwitch(string mode);

    /// <summary>
    /// Records consumption of a budget.
    /// </summary>
    /// <param name="mode">Quality mode used for the operation.</param>
    /// <param name="tokens">Tokens consumed.</param>
    /// <param name="cost">Estimated euro cost.</param>
    /// <param name="percent">Percent of daily budget consumed.</param>
    /// <param name="softCap">Indicates whether the soft cap has been reached.</param>
    /// <param name="hardCap">Indicates whether the hard cap has been reached.</param>
    void TrackBudgetUsed(string mode, int tokens, decimal cost, int percent, bool softCap, bool hardCap);

    /// <summary>
    /// Records a throttling event.
    /// </summary>
    /// <param name="scope">Scope affected by throttling.</param>
    /// <param name="reason">Reason of the throttling.</param>
    void TrackThrottle(string scope, string reason);

    /// <summary>
    /// Records a tool invocation.
    /// </summary>
    /// <param name="toolName">Name of the tool invoked.</param>
    void TrackToolInvocation(string toolName);

    /// <summary>
    /// Records cancellation of a tool invocation.
    /// </summary>
    /// <param name="toolName">Name of the tool cancelled.</param>
    void TrackToolCancelled(string toolName);
}
