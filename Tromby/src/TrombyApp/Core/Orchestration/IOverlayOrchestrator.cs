using System.Threading;
using System.Threading.Tasks;

namespace Tromby.Core.Orchestration;

/// <summary>
/// Coordinates overlay interactions, routing requests to the appropriate LLM provider and managing lifecycle.
/// </summary>
public interface IOverlayOrchestrator
{
    /// <summary>
    /// Executes an interaction according to the active scheduler and persona.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the interaction.</param>
    Task ExecuteAsync(CancellationToken cancellationToken);
}
