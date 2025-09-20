using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tromby.Core.Scheduling;
using Tromby.Infra.Metrics;
using Tromby.Tools.Sandbox;
using Tromby.Infra;
using Xunit;

namespace Tromby.Tests;

public class OrchestratorTests
{
    [Fact]
    public void Scheduler_OverMidnight_WindowDetection_Works()
    {
        var scheduler = CreateScheduler();
        var start = new TimeSpan(23, 0, 0);
        var end = new TimeSpan(6, 0, 0);

        Assert.True(scheduler.IsInQualityWindow(start, end, new DateTime(2025, 1, 1, 23, 30, 0)));
        Assert.True(scheduler.IsInQualityWindow(start, end, new DateTime(2025, 1, 2, 5, 59, 0)));
        Assert.False(scheduler.IsInQualityWindow(start, end, new DateTime(2025, 1, 2, 12, 0, 0)));
    }

    [Fact]
    public async Task PowerShellSandbox_Enforces_Whitelist_And_Confirmation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["policy:actions:whitelist:0"] = "Get-ChildItem",
                ["policy:actions:whitelist:1"] = "Get-Process",
                ["policy:actions:requireConfirm:0"] = "Set-Content",
                ["policy:limits:sandboxTimeoutSeconds"] = "5",
                ["policy:limits:ioBytesPerMinute"] = "2048"
            })
            .Build();

        var sandbox = new PowerShellSandbox(new TestLogger<PowerShellSandbox>(), configuration);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sandbox.ExecuteAsync("Remove-Item", true, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sandbox.ExecuteAsync("Set-Content", false, CancellationToken.None));

        if (OperatingSystem.IsWindows())
        {
            var output = await sandbox.ExecuteAsync("Get-ChildItem", true, CancellationToken.None);
            Assert.NotNull(output);
        }
    }

    [Fact]
    public void BudgetService_SoftAndHardCaps_Work()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp,
                "{""Mode"":{""Qualite"":{""PerDay"":{""Tokens"":100,""Cost"":1.0},""PerConversation"":{""Tokens"":50,""Cost"":0.5},""SoftCapPercent"":80},""Eco"":{""PerDay"":{""Tokens"":50,""Cost"":0.5},""PerConversation"":{""Tokens"":25,""Cost"":0.25},""SoftCapPercent"":75}}}");

            var service = new BudgetService(tmp);
            var conversation = service.BeginConversation();

            Assert.True(service.TryConsume(QualityMode.Qualite, conversation, 60, 0.2m, out var snap1, out var soft1));
            Assert.Equal(60, snap1.DayTokensUsed);
            Assert.False(soft1);

            Assert.True(service.TryConsume(QualityMode.Qualite, conversation, 20, 0.2m, out var snap2, out var soft2));
            Assert.True(soft2);
            Assert.Equal(80, snap2.DayPercent);

            Assert.False(service.TryConsume(QualityMode.Qualite, conversation, 30, 0.8m, out _, out _));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    private static AdaptiveScheduler CreateScheduler()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["scheduler:qualityStart"] = "23:00:00",
                ["scheduler:qualityEnd"] = "06:00:00",
                ["scheduler:overrideMinutes"] = "30",
                ["budgets:defaultMode"] = "Qualite"
            })
            .Build();

        return new AdaptiveScheduler(configuration, new NoopMetricsCollector(), new TestLogger<AdaptiveScheduler>());
    }

    private sealed class NoopMetricsCollector : IMetricsCollector
    {
        public void TrackCompletion(string providerName) { }
        public void TrackModeSwitch(string mode) { }
        public void TrackBudgetUsed(string mode, int tokens, decimal cost, int percent, bool softCap, bool hardCap) { }
        public void TrackThrottle(string scope, string reason) { }
        public void TrackToolInvocation(string toolName) { }
        public void TrackToolCancelled(string toolName) { }
    }

    private sealed class TestLogger<T> : ILogger<T>, IDisposable
    {
        public IDisposable BeginScope<TState>(TState state) => this;

        public void Dispose()
        {
        }

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
