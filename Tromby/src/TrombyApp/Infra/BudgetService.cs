using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Tromby.Infra;

/// <summary>
/// Represents supported quality modes for budget tracking.
/// </summary>
public enum QualityMode
{
    /// <summary>
    /// Premium quality mode using remote providers.
    /// </summary>
    Qualite,

    /// <summary>
    /// Eco mode prioritising local execution.
    /// </summary>
    Eco
}

/// <summary>
/// Strongly typed representation of budgets.json configuration.
/// </summary>
public sealed class BudgetConfig
{
    public required ModeConfig Mode { get; init; }

    public sealed class ModeConfig
    {
        public required Limits Qualite { get; init; }
        public required Limits Eco { get; init; }
    }

    public sealed class Limits
    {
        public required Cap PerDay { get; init; }
        public required Cap PerConversation { get; init; }
        public int SoftCapPercent { get; init; } = 80;
    }

    public sealed class Cap
    {
        public int Tokens { get; init; }
        public decimal Cost { get; init; }
    }
}

/// <summary>
/// Immutable snapshot returned to UI displaying usage and remaining capacity.
/// </summary>
public sealed class BudgetSnapshot
{
    public int DayTokensUsed { get; init; }
    public decimal DayCostUsed { get; init; }
    public int DayTokensCap { get; init; }
    public decimal DayCostCap { get; init; }
    public int ConversationTokensUsed { get; init; }
    public decimal ConversationCostUsed { get; init; }
    public int ConversationTokensCap { get; init; }
    public decimal ConversationCostCap { get; init; }
    public int SoftCapPercent { get; init; }

    public int DayPercent => DayTokensCap > 0 ? (int)Math.Clamp(Math.Round((double)DayTokensUsed / DayTokensCap * 100, MidpointRounding.AwayFromZero), 0, 100) : 0;
    public int ConversationPercent => ConversationTokensCap > 0 ? (int)Math.Clamp(Math.Round((double)ConversationTokensUsed / ConversationTokensCap * 100, MidpointRounding.AwayFromZero), 0, 100) : 0;

    public bool SoftCapReached => SoftCapPercent > 0 && (DayPercent >= SoftCapPercent || ConversationPercent >= SoftCapPercent);

    public bool IsHardCapReached =>
        DayTokensUsed >= DayTokensCap ||
        DayCostUsed >= DayCostCap ||
        ConversationTokensUsed >= ConversationTokensCap ||
        ConversationCostUsed >= ConversationCostCap;
}

/// <summary>
/// Central budget tracker enforcing per-day and per-conversation limits with soft/hard caps.
/// </summary>
public sealed class BudgetService
{
    private sealed class Usage
    {
        public int Tokens;
        public decimal Cost;
    }

    private readonly BudgetConfig _config;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Usage> _conversations = new();
    private DateTime _currentDayUtc = DateTime.UtcNow.Date;
    private Usage _daily = new();

    public BudgetService(string budgetsJsonPath)
    {
        if (string.IsNullOrWhiteSpace(budgetsJsonPath))
        {
            throw new ArgumentException("Budget path required", nameof(budgetsJsonPath));
        }

        if (!File.Exists(budgetsJsonPath))
        {
            throw new FileNotFoundException("Budget configuration not found.", budgetsJsonPath);
        }

        var json = File.ReadAllText(budgetsJsonPath);
        var config = JsonSerializer.Deserialize<BudgetConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        _config = config ?? throw new InvalidOperationException("Invalid budgets.json format.");
    }

    /// <summary>
    /// Begins tracking for a conversation scope and returns its identifier.
    /// </summary>
    public Guid BeginConversation()
    {
        lock (_sync)
        {
            EnsureDayLocked(DateTime.UtcNow);
            var id = Guid.NewGuid();
            _conversations[id] = new Usage();
            return id;
        }
    }

    /// <summary>
    /// Releases all tracking data for a conversation.
    /// </summary>
    public void EndConversation(Guid conversationId)
    {
        lock (_sync)
        {
            _conversations.Remove(conversationId);
        }
    }

    /// <summary>
    /// Attempts to reserve tokens/cost; returns false when a hard cap would be exceeded.
    /// </summary>
    public bool TryConsume(QualityMode mode, Guid conversationId, int tokens, decimal cost, out BudgetSnapshot snapshot, out bool softCapTriggered)
    {
        lock (_sync)
        {
            EnsureDayLocked(DateTime.UtcNow);
            var limits = GetLimits(mode);

            if (!_conversations.TryGetValue(conversationId, out var conversation))
            {
                conversation = new Usage();
                _conversations[conversationId] = conversation;
            }

            var nextDailyTokens = checked(_daily.Tokens + tokens);
            var nextDailyCost = _daily.Cost + cost;
            var nextConversationTokens = conversation.Tokens + tokens;
            var nextConversationCost = conversation.Cost + cost;

            snapshot = new BudgetSnapshot
            {
                DayTokensUsed = nextDailyTokens,
                DayCostUsed = nextDailyCost,
                DayTokensCap = limits.PerDay.Tokens,
                DayCostCap = limits.PerDay.Cost,
                ConversationTokensUsed = nextConversationTokens,
                ConversationCostUsed = nextConversationCost,
                ConversationTokensCap = limits.PerConversation.Tokens,
                ConversationCostCap = limits.PerConversation.Cost,
                SoftCapPercent = limits.SoftCapPercent
            };

            softCapTriggered = snapshot.SoftCapReached;
            if (snapshot.IsHardCapReached)
            {
                return false;
            }

            _daily.Tokens = nextDailyTokens;
            _daily.Cost = nextDailyCost;
            conversation.Tokens = nextConversationTokens;
            conversation.Cost = nextConversationCost;
            return true;
        }
    }

    /// <summary>
    /// Refunds usage when a completion is discarded or retried.
    /// </summary>
    public void Refund(Guid conversationId, int tokens, decimal cost)
    {
        lock (_sync)
        {
            EnsureDayLocked(DateTime.UtcNow);
            _daily.Tokens = Math.Max(0, _daily.Tokens - tokens);
            _daily.Cost = Math.Max(0m, _daily.Cost - cost);

            if (_conversations.TryGetValue(conversationId, out var usage))
            {
                usage.Tokens = Math.Max(0, usage.Tokens - tokens);
                usage.Cost = Math.Max(0m, usage.Cost - cost);
            }
        }
    }

    /// <summary>
    /// Returns the latest snapshot for UI display.
    /// </summary>
    public BudgetSnapshot GetSnapshot(QualityMode mode, Guid? conversationId = null)
    {
        lock (_sync)
        {
            EnsureDayLocked(DateTime.UtcNow);
            var limits = GetLimits(mode);
            Usage conversation = new();
            if (conversationId.HasValue && _conversations.TryGetValue(conversationId.Value, out var scoped))
            {
                conversation = scoped;
            }

            return new BudgetSnapshot
            {
                DayTokensUsed = _daily.Tokens,
                DayCostUsed = _daily.Cost,
                DayTokensCap = limits.PerDay.Tokens,
                DayCostCap = limits.PerDay.Cost,
                ConversationTokensUsed = conversation.Tokens,
                ConversationCostUsed = conversation.Cost,
                ConversationTokensCap = limits.PerConversation.Tokens,
                ConversationCostCap = limits.PerConversation.Cost,
                SoftCapPercent = limits.SoftCapPercent
            };
        }
    }

    private BudgetConfig.Limits GetLimits(QualityMode mode) =>
        mode == QualityMode.Qualite ? _config.Mode.Qualite : _config.Mode.Eco;

    private void EnsureDayLocked(DateTime now)
    {
        if (now.Date == _currentDayUtc)
        {
            return;
        }

        _currentDayUtc = now.Date;
        _daily = new Usage();
        _conversations.Clear();
    }
}
