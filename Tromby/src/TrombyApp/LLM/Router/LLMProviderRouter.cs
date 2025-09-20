using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tromby.Core.Scheduling;
using Tromby.Infra.Config;
using Tromby.Infra.Metrics;
using Tromby.LLM.Abstractions;

namespace Tromby.LLM.Router;

/// <summary>
/// Routes prompts to the optimal provider while enforcing budgets, caching and scheduler preferences.
/// </summary>
public sealed class LLMProviderRouter
{
    private static readonly IReadOnlyList<string> QualityPriority = new[] { "openai", "gemini", "mistral", "xai", "local" };
    private static readonly IReadOnlyList<string> EcoPriority = new[] { "local", "mistral", "xai", "gemini", "openai" };

    private readonly IReadOnlyDictionary<string, ILLMClient> _clients;
    private readonly IScheduler _scheduler;
    private readonly ILogger<LLMProviderRouter> _logger;
    private readonly IMetricsCollector _metrics;
    private readonly ConcurrentDictionary<string, (DateTimeOffset expiresAt, string response)> _cache = new();
    private readonly TimeSpan _cacheTtl;
    private readonly BudgetLimits _limits;
    private readonly IReadOnlyDictionary<string, decimal> _costsPer1K;
    private readonly object _budgetLock = new();
    private long _promptTokens;
    private long _completionTokens;
    private decimal _estimatedSpend;

    /// <summary>
    /// Initializes a new instance of the router.
    /// </summary>
    public LLMProviderRouter(
        IEnumerable<ILLMClient> clients,
        IScheduler scheduler,
        IMetricsCollector metrics,
        IConfigurationAdapter configurationAdapter,
        IConfiguration configuration,
        ILogger<LLMProviderRouter> logger)
    {
        _clients = clients.ToDictionary(client => client.Name, StringComparer.OrdinalIgnoreCase);
        _scheduler = scheduler;
        _metrics = metrics;
        _logger = logger;
        _cacheTtl = configurationAdapter.GetCacheTtl();
        _limits = new BudgetLimits(
            configuration.GetValue<int?>("budgets:limits:tokenBudget") ?? 0,
            configuration.GetValue<decimal?>("budgets:limits:euroBudget") ?? 0m);

        var configuredCosts = configuration
            .GetSection("llm:costsPer1K")
            .GetChildren()
            .ToDictionary(
                child => child.Key,
                child => decimal.TryParse(child.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m,
                StringComparer.OrdinalIgnoreCase);

        if (configuredCosts.Count == 0)
        {
            configuredCosts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["openai"] = 15m,
                ["mistral"] = 4m,
                ["gemini"] = 7m,
                ["xai"] = 5m,
                ["local"] = 0m
            };
        }

        _costsPer1K = configuredCosts;
    }

    /// <summary>
    /// Sends a prompt through the selected provider with persona instructions.
    /// </summary>
    public async Task<string> SendAsync(string prompt, string persona, CancellationToken cancellationToken)
    {
        var cacheKey = ComputeCacheKey(prompt, persona);
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.expiresAt > DateTimeOffset.UtcNow)
        {
            _logger.LogDebug("Serving cached response for key {Key}.", cacheKey);
            return cached.response;
        }

        var orderedClients = await ResolveClientsAsync(cancellationToken).ConfigureAwait(false);
        Exception? lastError = null;
        foreach (var client in orderedClients)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _logger.LogInformation("Routing prompt to {Provider}", client.Name);
                var response = await client.CompleteAsync(prompt, persona, cancellationToken).ConfigureAwait(false);
                RegisterSuccess(client, prompt, response);
                _cache[cacheKey] = (DateTimeOffset.UtcNow.Add(_cacheTtl), response);
                return response;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Provider {Provider} failed, attempting fallback.", client.Name);
                lastError = ex;
            }
        }

        throw new InvalidOperationException("No LLM provider succeeded.", lastError);
    }

    /// <summary>
    /// Streams a completion response chunk-by-chunk using provider fallbacks.
    /// </summary>
    public async Task<IAsyncEnumerable<string>> StreamAsync(string prompt, string persona, CancellationToken cancellationToken)
    {
        var orderedClients = await ResolveClientsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var client in orderedClients)
        {
            try
            {
                return await client.StreamAsync(prompt, persona, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Streaming failed with {Provider}, retrying next option.", client.Name);
            }
        }

        return FallbackStream(prompt, persona, cancellationToken);
    }

    /// <summary>
    /// Gets the current budget snapshot for UI badges.
    /// </summary>
    public BudgetSnapshot GetBudgetSnapshot()
    {
        lock (_budgetLock)
        {
            var consumed = _promptTokens + _completionTokens;
            var tokenBudget = _limits.TokenBudget;
            var tokenRatio = tokenBudget > 0 ? Math.Clamp(1d - consumed / (double)tokenBudget, 0d, 1d) : 1d;
            var euroBudget = _limits.EuroBudget;
            var euroRatio = euroBudget > 0m ? Math.Clamp(1m - (_estimatedSpend / euroBudget), 0m, 1m) : 1m;
            return new BudgetSnapshot(tokenRatio, euroRatio, consumed, tokenBudget, _estimatedSpend, euroBudget);
        }
    }

    private async Task<IReadOnlyList<ILLMClient>> ResolveClientsAsync(CancellationToken cancellationToken)
    {
        var tier = await _scheduler.ResolveTargetTierAsync(cancellationToken).ConfigureAwait(false);
        var preferred = string.Equals(tier, "premium", StringComparison.OrdinalIgnoreCase) ? QualityPriority : EcoPriority;
        var sequence = new List<ILLMClient>(_clients.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in preferred)
        {
            if (_clients.TryGetValue(name, out var client) && seen.Add(client.Name))
            {
                sequence.Add(client);
            }
        }

        foreach (var client in _clients.Values)
        {
            if (seen.Add(client.Name))
            {
                sequence.Add(client);
            }
        }

        return sequence;
    }

    private void RegisterSuccess(ILLMClient client, string prompt, string response)
    {
        _metrics.TrackCompletion(client.Name);
        var promptTokens = client.EstimateTokens(prompt);
        var completionTokens = client.EstimateTokens(response);

        lock (_budgetLock)
        {
            _promptTokens += promptTokens;
            _completionTokens += completionTokens;

            if (_limits.EuroBudget > 0 && _costsPer1K.TryGetValue(client.Name, out var costPer1K))
            {
                var totalTokens = promptTokens + completionTokens;
                _estimatedSpend += (totalTokens / 1000m) * costPer1K;
            }
        }
    }

    private static string ComputeCacheKey(string prompt, string persona)
    {
        using var sha = SHA256.Create();
        var buffer = Encoding.UTF8.GetBytes($"{persona}\u2029{prompt}");
        return Convert.ToHexString(sha.ComputeHash(buffer));
    }

    private async IAsyncEnumerable<string> FallbackStream(string prompt, string persona, CancellationToken cancellationToken)
    {
        var completion = await SendAsync(prompt, persona, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(completion))
        {
            yield return completion;
        }
    }

    /// <summary>
    /// Represents budget state consumed by the router.
    /// </summary>
    public readonly struct BudgetSnapshot
    {
        public BudgetSnapshot(double tokenRatio, decimal euroRatio, long tokensConsumed, long tokenBudget, decimal eurosSpent, decimal euroBudget)
        {
            TokenRatio = tokenRatio;
            EuroRatio = euroRatio;
            TokensConsumed = tokensConsumed;
            TokenBudget = tokenBudget;
            EurosSpent = eurosSpent;
            EuroBudget = euroBudget;
        }

        public double TokenRatio { get; }

        public decimal EuroRatio { get; }

        public long TokensConsumed { get; }

        public long TokenBudget { get; }

        public decimal EurosSpent { get; }

        public decimal EuroBudget { get; }
    }

    private readonly struct BudgetLimits
    {
        public BudgetLimits(int tokenBudget, decimal euroBudget)
        {
            TokenBudget = tokenBudget;
            EuroBudget = euroBudget;
        }

        public int TokenBudget { get; }

        public decimal EuroBudget { get; }
    }
}
