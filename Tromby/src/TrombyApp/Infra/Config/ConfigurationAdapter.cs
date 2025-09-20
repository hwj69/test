using System;
using Microsoft.Extensions.Configuration;

namespace Tromby.Infra.Config;

/// <summary>
/// Default configuration adapter using <see cref="IConfiguration"/>.
/// </summary>
public sealed class ConfigurationAdapter : IConfigurationAdapter
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationAdapter"/> class.
    /// </summary>
    public ConfigurationAdapter(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    public TimeSpan GetCacheTtl()
    {
        var ttlSeconds = _configuration.GetValue("budgets:cacheTtlSeconds", 60);
        return TimeSpan.FromSeconds(ttlSeconds);
    }
}
