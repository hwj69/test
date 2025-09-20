using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Tromby.Core.Orchestration;
using Tromby.Core.Persona;
using Tromby.Core.Scheduling;
using Tromby.Core.Sessions;
using Tromby.Security;
using Tromby.LLM.Router;
using Tromby.Infra;
using Tromby.Infra.Metrics;

namespace Tromby.UI.ViewModels;

/// <summary>
/// View model for the shell overlay orchestrating user interactions and background state.
/// </summary>
public partial class ShellViewModel : ObservableRecipient, IDisposable
{
    private readonly IOverlayOrchestrator _orchestrator;
    private readonly IPersonaManager _personaManager;
    private readonly IScheduler _scheduler;
    private readonly IConsentService _consentService;
    private readonly ISessionStore _sessionStore;
    private readonly PolicyManager _policyManager;
    private readonly LLMProviderRouter _router;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly IMetricsCollector _metrics;
    private readonly BudgetService? _budgetService;
    private readonly CancellationTokenSource _cts = new();
    private string _budgetBadge = "Budget indisponible";
    private BudgetSnapshot? _lastBudgetSnapshot;
    private QualityMode _lastBudgetMode = QualityMode.Qualite;
    private bool _hasBudget;

    [ObservableProperty]
    private string _activeMode = "Qualité";

    [ObservableProperty]
    private bool _showOnboarding = true;

    [ObservableProperty]
    private string _lastSessionPreview = string.Empty;

    /// <summary>
    /// Initializes a new instance of the view model.
    /// </summary>
    public ShellViewModel(
        IOverlayOrchestrator orchestrator,
        IPersonaManager personaManager,
        IScheduler scheduler,
        IConsentService consentService,
        ISessionStore sessionStore,
        PolicyManager policyManager,
        LLMProviderRouter router,
        IMetricsCollector metrics,
        ILogger<ShellViewModel> logger)
    {
        _orchestrator = orchestrator;
        _personaManager = personaManager;
        _scheduler = scheduler;
        _consentService = consentService;
        _sessionStore = sessionStore;
        _policyManager = policyManager;
        _router = router;
        _metrics = metrics;
        _logger = logger;

        try
        {
            var budgetsPath = Path.Combine(AppContext.BaseDirectory, "config", "budgets.json");
            _budgetService = new BudgetService(budgetsPath);
            _hasBudget = true;
        }
        catch (Exception ex)
        {
            _hasBudget = false;
            _logger.LogWarning(ex, "Budget service initialisation failed.");
        }

        TriggerInteractionCommand = new AsyncRelayCommand(TriggerInteractionAsync);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        DemoReserveCommand = new RelayCommand(DemoReserve);
        ResumeAsync(_cts.Token).FireAndForget(_logger);
        RefreshBadges();
    }

    /// <summary>
    /// Command that triggers a new LLM interaction.
    /// </summary>
    public IAsyncRelayCommand TriggerInteractionCommand { get; }

    /// <summary>
    /// Command to open the settings overlay.
    /// </summary>
    public IRelayCommand OpenSettingsCommand { get; }

    /// <summary>
    /// Command triggering a demo reservation of budget usage.
    /// </summary>
    public IRelayCommand DemoReserveCommand { get; }

    private async Task ResumeAsync(CancellationToken token)
    {
        try
        {
            await _policyManager.EnsurePolicyUpToDateAsync(_consentService, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            ShowOnboarding = true;
            LastSessionPreview = string.Empty;
            RefreshBadges();
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Policy validation failed.");
            ShowOnboarding = true;
            LastSessionPreview = string.Empty;
            return;
        }

        try
        {
            await _sessionStore.RestoreAsync(token).ConfigureAwait(false);
            if (_sessionStore is DpapiSessionStore dpapiStore)
            {
                LastSessionPreview = dpapiStore.LastSnapshot;
            }

            ShowOnboarding = false;
            ActiveMode = _scheduler.ActiveMode;
            RefreshBadges();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume session state.");
        }
    }

    private async Task TriggerInteractionAsync()
    {
        await _orchestrator.ExecuteAsync(_cts.Token).ConfigureAwait(false);
        RefreshBadges();

        if (_hasBudget && _lastBudgetSnapshot is { } snapshot)
        {
            _logger.LogDebug(
                "Budget snapshot mode={Mode} tokens={Tokens}/{Budget} euros={Euros}/{EuroBudget} percent={Percent}%.",
                _lastBudgetMode,
                snapshot.DayTokensUsed,
                snapshot.DayTokensCap,
                snapshot.DayCostUsed,
                snapshot.DayCostCap,
                snapshot.DayPercent);
        }
    }

    private void OpenSettings()
    {
        // TODO: Navigate to settings view.
    }

    private void DemoReserve()
    {
        if (_budgetService is null)
        {
            return;
        }

        var prefersQuality = !string.Equals(_scheduler.ActiveMode, "Éco", StringComparison.OrdinalIgnoreCase);
        var effectiveQuality = _scheduler.ComputeEffectiveMode(prefersQuality, _scheduler.QualityStart, _scheduler.QualityEnd, DateTime.Now);
        var mode = effectiveQuality ? QualityMode.Qualite : QualityMode.Eco;

        var success = _budgetService.TryConsume(mode, Guid.Empty, 500, 0.05m, out var snapshot, out var softCap);
        if (!success)
        {
            _logger.LogInformation("Demo reserve blocked by hard cap for mode {Mode}.", mode);
        }

        _metrics.TrackBudgetUsed(mode.ToString(), snapshot.DayTokensUsed, snapshot.DayCostUsed, snapshot.DayPercent, softCap, snapshot.IsHardCapReached);
        RefreshBadges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    internal string BuildBudgetBadge()
    {
        if (!_hasBudget)
        {
            return "Budget indisponible";
        }

        var badge = _budgetBadge;
        var overrideMs = _scheduler.OverrideMsRemaining();
        if (overrideMs > 0)
        {
            var remaining = Math.Max(1, (int)Math.Ceiling(overrideMs / 60000d));
            badge += $" • Override {remaining} min";
        }

        return badge;
    }

    internal void UpdateBudgetBadge()
    {
        if (!TryGetBudgetSnapshot(out var mode, out var snapshot))
        {
            _budgetBadge = "Budget indisponible";
            _lastBudgetSnapshot = null;
            _lastBudgetMode = QualityMode.Qualite;
            _hasBudget = false;
            return;
        }

        _hasBudget = true;
        _lastBudgetMode = mode;
        _lastBudgetSnapshot = snapshot;

        var label = mode == QualityMode.Qualite ? "Qualité" : "Éco";
        label += $" • {snapshot.DayPercent}%";

        if (snapshot.IsHardCapReached)
        {
            label += " ⚠ Hard cap";
        }
        else if (snapshot.SoftCapReached)
        {
            label += " • Soft cap";
        }

        _budgetBadge = label;
        _metrics.TrackBudgetUsed(mode.ToString(), snapshot.DayTokensUsed, snapshot.DayCostUsed, snapshot.DayPercent, snapshot.SoftCapReached, snapshot.IsHardCapReached);
    }

    private bool TryGetBudgetSnapshot(out QualityMode mode, out BudgetSnapshot snapshot)
    {
        mode = QualityMode.Qualite;
        snapshot = null!;

        if (_budgetService is null)
        {
            return false;
        }

        try
        {
            var prefersQuality = !string.Equals(_scheduler.ActiveMode, "Éco", StringComparison.OrdinalIgnoreCase);
            var effectiveQuality = _scheduler.ComputeEffectiveMode(prefersQuality, _scheduler.QualityStart, _scheduler.QualityEnd, DateTime.Now);
            mode = effectiveQuality ? QualityMode.Qualite : QualityMode.Eco;
            snapshot = _budgetService.GetSnapshot(mode);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute budget snapshot.");
            return false;
        }
    }

}

internal static class TaskExtensions
{
    public static async void FireAndForget(this Task task, ILogger logger)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FireAndForget task failed.");
        }
    }
}
