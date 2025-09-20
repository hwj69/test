using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Tromby.Infra.Config;

namespace Tromby.Core.Persona;

/// <summary>
/// Loads persona prompts from configuration and exposes preset switching.
/// </summary>
public sealed class PersonaManager : IPersonaManager
{
    private readonly IConfiguration _configuration;
    private readonly IAppProfileLoader _profileLoader;
    private string _activePreset = "drole";

    /// <summary>
    /// Initializes a new instance of the <see cref="PersonaManager"/> class.
    /// </summary>
    public PersonaManager(IConfiguration configuration, IAppProfileLoader profileLoader)
    {
        _configuration = configuration;
        _profileLoader = profileLoader;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetPresets()
    {
        var section = _configuration.GetSection("persona:presets");
        return section.GetChildren().Select(child => child.Key).ToArray();
    }

    /// <inheritdoc />
    public Task<string> GetActivePersonaAsync(CancellationToken cancellationToken)
    {
        var prompt = _configuration[$"persona:presets:{_activePreset}:prompt"] ?? string.Empty;
        // TODO: Merge with dynamic profile information and persona mood.
        return Task.FromResult(prompt);
    }

    /// <inheritdoc />
    public void SetPersona(string preset)
    {
        _activePreset = preset;
        _profileLoader.SetActivePersona(preset);
    }
}
