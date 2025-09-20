using System.Threading;
using System.Threading.Tasks;

namespace Tromby.Infra.Config;

/// <summary>
/// Manages persisted application profile data (persona, schedules, preferences).
/// </summary>
public interface IAppProfileLoader
{
    /// <summary>
    /// Loads the persisted profile.
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sets the active persona preset for persistence.
    /// </summary>
    void SetActivePersona(string preset);
}
