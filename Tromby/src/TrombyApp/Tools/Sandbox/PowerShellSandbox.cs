using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tromby.Tools.Sandbox;

/// <summary>
/// Runs PowerShell commands with whitelist enforcement and optional confirmation prompts.
/// </summary>
public sealed class PowerShellSandbox : IShellSandbox
{
    private readonly ILogger<PowerShellSandbox> _logger;
    private readonly HashSet<string> _whitelist;
    private readonly HashSet<string> _requireConfirm;
    private readonly TimeSpan _executionTimeout;
    private readonly int _ioBudgetBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="PowerShellSandbox"/> class.
    /// </summary>
    public PowerShellSandbox(ILogger<PowerShellSandbox> logger, IConfiguration configuration)
    {
        _logger = logger;
        var actionsSection = configuration.GetSection("policy:actions");
        _whitelist = actionsSection.GetSection("whitelist").Get<HashSet<string>>() ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _requireConfirm = actionsSection.GetSection("requireConfirm").Get<HashSet<string>>() ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var timeoutSeconds = configuration.GetValue<int?>("policy:limits:sandboxTimeoutSeconds") ?? 10;
        _executionTimeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 300));
        _ioBudgetBytes = Math.Max(1024, configuration.GetValue<int?>("policy:limits:ioBytesPerMinute") ?? 1024 * 1024);
    }

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string script, bool requireConfirmation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_whitelist.Contains(script))
        {
            throw new InvalidOperationException("Script not in whitelist.");
        }

        if (_requireConfirm.Contains(script) && !requireConfirmation)
        {
            throw new InvalidOperationException("Explicit confirmation required for this script.");
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(_executionTimeout);

        using var powerShell = PowerShell.Create();
        powerShell.AddScript(script);

        _logger.LogInformation("Executing sandboxed script {Script} with timeout {Timeout}s and IO cap {IoBudget} bytes.", script, _executionTimeout.TotalSeconds, _ioBudgetBytes);

        var invocationTask = Task.Run(() => powerShell.Invoke(), CancellationToken.None);
        Task completedTask;
        try
        {
            completedTask = await Task.WhenAny(invocationTask, Task.Delay(Timeout.Infinite, linkedCts.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            powerShell.Stop();
            throw;
        }
        catch (OperationCanceledException)
        {
            powerShell.Stop();
            throw new TimeoutException($"Script execution exceeded {_executionTimeout.TotalSeconds} seconds.");
        }

        if (completedTask != invocationTask)
        {
            powerShell.Stop();
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"Script execution exceeded {_executionTimeout.TotalSeconds} seconds.");
        }

        var results = await invocationTask.ConfigureAwait(false);

        if (powerShell.Streams.Error.Count > 0)
        {
            var errorPayload = string.Join(Environment.NewLine, powerShell.Streams.Error.Select(e => e.ToString()));
            throw new InvalidOperationException($"PowerShell reported errors: {errorPayload}");
        }

        var output = string.Join(Environment.NewLine, results);
        var sanitized = EnforceIoBudget(output);
        if (!ReferenceEquals(output, sanitized))
        {
            _logger.LogWarning("Output truncated to {Limit} bytes for script {Script}.", _ioBudgetBytes, script);
        }

        return sanitized;
    }

    private string EnforceIoBudget(string output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return output;
        }

        var bytes = Encoding.UTF8.GetBytes(output);
        if (bytes.Length <= _ioBudgetBytes)
        {
            return output;
        }

        return Encoding.UTF8.GetString(bytes, 0, _ioBudgetBytes);
    }
}
