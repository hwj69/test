using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tromby.Core.Scheduling;

/// <summary>
/// Provides scheduling decisions for routing LLM requests based on budgets and system load.
/// </summary>
public interface IScheduler
{
    /// <summary>
    /// Gets the current configured mode (Qualité, Éco ou Auto).
    /// </summary>
    string ActiveMode { get; }

    /// <summary>
    /// Gets or sets the daily start of the Qualité window.
    /// </summary>
    TimeSpan QualityStart { get; set; }

    /// <summary>
    /// Gets or sets the daily end of the Qualité window.
    /// </summary>
    TimeSpan QualityEnd { get; set; }

    /// <summary>
    /// Gets or sets the default duration in minutes for manual overrides.
    /// </summary>
    int OverrideMinutes { get; set; }

    /// <summary>Définit/retire un override manuel pour la durée demandée (minutes).</summary>
    void ManualOverride(int minutes);

    /// <summary>Retourne true si la fenêtre Qualité est active (hors override).</summary>
    bool IsInQualityWindow(TimeSpan start, TimeSpan end, DateTime now);

    /// <summary>Détermine le mode effectif (Qualité= true / Éco= false) selon fenêtre & override.</summary>
    bool ComputeEffectiveMode(bool userPrefQuality, TimeSpan start, TimeSpan end, DateTime now);

    /// <summary>Millisecondes restantes d’override, 0 si aucun.</summary>
    int OverrideMsRemaining();

    /// <summary>
    /// Computes the target provider tier for the next request.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> ResolveTargetTierAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Switches the configured mode (Qualité, Éco, Auto).
    /// </summary>
    /// <param name="mode">Requested mode.</param>
    void SwitchMode(string mode);
}
