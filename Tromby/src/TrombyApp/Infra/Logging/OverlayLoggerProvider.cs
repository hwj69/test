using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Tromby.Infra.Logging;

/// <summary>
/// Minimal structured logger provider capturing overlay events for diagnostics.
/// </summary>
public sealed class OverlayLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, OverlayLogger> _loggers = new();

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new OverlayLogger(name));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _loggers.Clear();
    }

    private sealed class OverlayLogger : ILogger
    {
        private readonly string _categoryName;

        public OverlayLogger(string categoryName)
        {
            _categoryName = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            // TODO: Persist logs to rolling buffer exposed in diagnostics panel.
            System.Diagnostics.Debug.WriteLine($"[{logLevel}] {_categoryName}: {message}");
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
