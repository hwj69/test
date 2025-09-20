using System;

namespace Tromby.Infra.Config;

/// <summary>
/// Provides strongly typed accessors for configuration values.
/// </summary>
public interface IConfigurationAdapter
{
    /// <summary>
    /// Gets the cache TTL for offline-first interactions.
    /// </summary>
    TimeSpan GetCacheTtl();
}
