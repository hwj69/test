using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tromby.Infra.Config;

namespace Tromby.Infra.Profiles;

/// <summary>
/// Loads and persists simple JSON profile data.
/// </summary>
public sealed class AppProfileLoader : IAppProfileLoader
{
    private readonly ILogger<AppProfileLoader> _logger;
    private readonly string _profilePath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Tromby", "profile.json");

    /// <summary>
    /// Initializes a new instance of the <see cref="AppProfileLoader"/> class.
    /// </summary>
    public AppProfileLoader(ILogger<AppProfileLoader> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task LoadAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(_profilePath))
            {
                return;
            }

            var payload = File.ReadAllText(_profilePath);
            _logger.LogDebug("Loaded profile: {Payload}", payload);
            // TODO: Deserialize into strongly typed profile.
        }, cancellationToken);
    }

    /// <inheritdoc />
    public void SetActivePersona(string preset)
    {
        var json = JsonSerializer.Serialize(new { persona = preset });
        Directory.CreateDirectory(Path.GetDirectoryName(_profilePath)!);
        File.WriteAllText(_profilePath, json);
    }
}
