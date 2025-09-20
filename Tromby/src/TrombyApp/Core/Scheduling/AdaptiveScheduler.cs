using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tromby.Infra.Metrics;

namespace Tromby.Core.Scheduling;

/// <summary>
/// Scheduler that adapts provider selection based on budgets, metrics, and configured mode.
/// </summary>
public sealed class AdaptiveScheduler : IScheduler
{
    private readonly IMetricsCollector _metricsCollector;
    private readonly ILogger<AdaptiveScheduler> _logger;
    private readonly object _sync = new();

    private SchedulerMode _configuredMode;
    private TimeSpan _qualityStart;
    private TimeSpan _qualityEnd;
    private int _overrideMinutes;
    private DateTimeOffset? _overrideUntilUtc;
    private bool? _overrideForceQuality;
    private bool? _lastEffectiveQuality;

    /// <summary>
    /// Initializes a new instance of the scheduler.
    /// </summary>
    public AdaptiveScheduler(IConfiguration configuration, IMetricsCollector metricsCollector, ILogger<AdaptiveScheduler> logger)
    {
        _metricsCollector = metricsCollector;
        _logger = logger;

        _qualityStart = ParseTime(configuration["scheduler:qualityStart"]) ?? TimeSpan.FromHours(8);
        _qualityEnd = ParseTime(configuration["scheduler:qualityEnd"]) ?? TimeSpan.FromHours(18);
        _overrideMinutes = Math.Max(0, configuration.GetValue<int?>("scheduler:overrideMinutes") ?? 15);
        _configuredMode = ParseMode(configuration["budgets:defaultMode"]);
    }

    /// <inheritdoc />
    public string ActiveMode
    {
        get
        {
            lock (_sync)
            {
                return GetModeLabel(_configuredMode);
            }
        }
    }

    /// <inheritdoc />
    public TimeSpan QualityStart
    {
        get
        {
            lock (_sync)
            {
                return _qualityStart;
            }
        }
        set
        {
            lock (_sync)
            {
                _qualityStart = NormalizeTime(value);
                _lastEffectiveQuality = null;
            }
        }
    }

    /// <inheritdoc />
    public TimeSpan QualityEnd
    {
        get
        {
            lock (_sync)
            {
                return _qualityEnd;
            }
        }
        set
        {
            lock (_sync)
            {
                _qualityEnd = NormalizeTime(value);
                _lastEffectiveQuality = null;
            }
        }
    }

    /// <inheritdoc />
    public int OverrideMinutes
    {
        get
        {
            lock (_sync)
            {
                return _overrideMinutes;
            }
        }
        set
        {
            lock (_sync)
            {
                _overrideMinutes = Math.Max(0, value);
            }
        }
    }

    /// <inheritdoc />
    public void ManualOverride(int minutes)
    {
        lock (_sync)
        {
            if (minutes <= 0)
            {
                _overrideUntilUtc = null;
                _overrideForceQuality = null;
                _lastEffectiveQuality = null;
                _logger.LogInformation("Manual override cleared.");
                return;
            }

            var baseQuality = DetermineBaseQualityLocked(DateTime.Now);
            _overrideForceQuality = !baseQuality;
            _overrideUntilUtc = DateTimeOffset.UtcNow.AddMinutes(minutes);
            _lastEffectiveQuality = null;
            _logger.LogInformation(
                "Manual override set for {Minutes} minutes forcing {Mode}.",
                minutes,
                _overrideForceQuality.Value ? "Qualité" : "Éco");
        }
    }

    /// <inheritdoc />
    public bool IsInQualityWindow(TimeSpan start, TimeSpan end, DateTime now)
    {
        var normalizedStart = NormalizeTime(start);
        var normalizedEnd = NormalizeTime(end);
        var time = NormalizeTime(now.TimeOfDay);

        if (normalizedStart == normalizedEnd)
        {
            return true;
        }

        if (normalizedStart < normalizedEnd)
        {
            return time >= normalizedStart && time < normalizedEnd;
        }

        return time >= normalizedStart || time < normalizedEnd;
    }

    /// <inheritdoc />
    public bool ComputeEffectiveMode(bool userPrefQuality, TimeSpan start, TimeSpan end, DateTime now)
    {
        lock (_sync)
        {
            return ComputeEffectiveModeLocked(userPrefQuality, start, end, now);
        }
    }

    /// <inheritdoc />
    public int OverrideMsRemaining()
    {
        lock (_sync)
        {
            if (_overrideUntilUtc is { } until && _overrideForceQuality.HasValue)
            {
                var remaining = until - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    return (int)Math.Clamp(remaining.TotalMilliseconds, 0, int.MaxValue);
                }

                _overrideUntilUtc = null;
                _overrideForceQuality = null;
            }

            return 0;
        }
    }

    /// <inheritdoc />
    public Task<string> ResolveTargetTierAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool effectiveQuality;
        bool modeChanged;
        lock (_sync)
        {
            effectiveQuality = ComputeEffectiveModeLocked(_configuredMode != SchedulerMode.Eco, _qualityStart, _qualityEnd, DateTime.Now);
            modeChanged = _lastEffectiveQuality != effectiveQuality;
            _lastEffectiveQuality = effectiveQuality;
        }

        if (modeChanged)
        {
            var modeLabel = effectiveQuality ? "Qualité" : "Éco";
            _metricsCollector.TrackModeSwitch(modeLabel);
            _logger.LogInformation("Effective scheduler mode switched to {Mode}.", modeLabel);
        }

        var tier = effectiveQuality ? "premium" : "local";
        return Task.FromResult(tier);
    }

    /// <inheritdoc />
    public void SwitchMode(string mode)
    {
        SchedulerMode newMode;
        bool changed;
        lock (_sync)
        {
            newMode = ParseMode(mode);
            changed = newMode != _configuredMode;
            if (changed)
            {
                _configuredMode = newMode;
                _overrideUntilUtc = null;
                _overrideForceQuality = null;
                _lastEffectiveQuality = null;
            }
        }

        if (changed)
        {
            var label = GetModeLabel(newMode);
            _metricsCollector.TrackModeSwitch(label);
            _logger.LogInformation("Configured scheduler mode changed to {Mode}.", label);
        }
    }

    private bool ComputeEffectiveModeLocked(bool userPrefQuality, TimeSpan start, TimeSpan end, DateTime now)
    {
        var baseQuality = DetermineBaseQualityLocked(userPrefQuality, start, end, now);
        if (_overrideUntilUtc is { } until && _overrideForceQuality.HasValue)
        {
            if (until > DateTimeOffset.UtcNow)
            {
                return _overrideForceQuality.Value;
            }

            _overrideUntilUtc = null;
            _overrideForceQuality = null;
        }

        return baseQuality;
    }

    private bool DetermineBaseQualityLocked(DateTime now)
    {
        return DetermineBaseQualityLocked(_configuredMode != SchedulerMode.Eco, _qualityStart, _qualityEnd, now);
    }

    private bool DetermineBaseQualityLocked(bool userPrefQuality, TimeSpan start, TimeSpan end, DateTime now)
    {
        var normalizedStart = NormalizeTime(start);
        var normalizedEnd = NormalizeTime(end);

        return _configuredMode switch
        {
            SchedulerMode.Auto => IsInQualityWindow(normalizedStart, normalizedEnd, now),
            SchedulerMode.Eco => false,
            SchedulerMode.Quality => true,
            _ => userPrefQuality
        };
    }

    private static TimeSpan NormalizeTime(TimeSpan value)
    {
        var totalMinutes = value.TotalMinutes % (24 * 60);
        if (totalMinutes < 0)
        {
            totalMinutes += 24 * 60;
        }

        return TimeSpan.FromMinutes(totalMinutes);
    }

    private static SchedulerMode ParseMode(string? mode)
    {
        if (string.Equals(mode, "Éco", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "Eco", StringComparison.OrdinalIgnoreCase))
        {
            return SchedulerMode.Eco;
        }

        if (string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return SchedulerMode.Auto;
        }

        return SchedulerMode.Quality;
    }

    private static string GetModeLabel(SchedulerMode mode) => mode switch
    {
        SchedulerMode.Eco => "Éco",
        SchedulerMode.Auto => "Auto",
        _ => "Qualité"
    };

    private static TimeSpan? ParseTime(string? value)
    {
        if (TimeSpan.TryParse(value, out var parsed))
        {
            return NormalizeTime(parsed);
        }

        return null;
    }

    private enum SchedulerMode
    {
        Quality,
        Eco,
        Auto
    }
}
