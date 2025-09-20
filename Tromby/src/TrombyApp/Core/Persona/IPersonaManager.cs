using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tromby.Core.Persona;

/// <summary>
/// Provides persona presets and resolves the active Tromby persona.
/// </summary>
public interface IPersonaManager
{
    /// <summary>
    /// Gets the available persona presets.
    /// </summary>
    IReadOnlyCollection<string> GetPresets();

    /// <summary>
    /// Resolves the active persona prompt for the current mode.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> GetActivePersonaAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sets the persona preset to use.
    /// </summary>
    /// <param name="preset">Preset key.</param>
    void SetPersona(string preset);
}
