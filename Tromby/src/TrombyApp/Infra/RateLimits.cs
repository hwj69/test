using System;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Tromby.Infra;

/// <summary>
/// Centralized rate limiting and concurrency guard for outbound operations.
/// </summary>
public sealed class RateLimits : IDisposable
{
    private readonly FixedWindowRateLimiter _requestLimiter;
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly ILogger<RateLimits> _logger;
    private readonly TimeSpan _window;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimits"/> class.
    /// </summary>
    /// <param name="requestsPerMinute">Allowed requests per minute.</param>
    /// <param name="concurrencyLimit">Maximum concurrent operations.</param>
    /// <param name="logger">Logger instance.</param>
    public RateLimits(int requestsPerMinute, int concurrencyLimit, ILogger<RateLimits> logger)
    {
        if (requestsPerMinute <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestsPerMinute));
        }

        if (concurrencyLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(concurrencyLimit));
        }

        _window = TimeSpan.FromMinutes(1);
        _requestLimiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = requestsPerMinute,
            Window = _window,
            AutoReplenishment = true,
            QueueLimit = requestsPerMinute,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
        _concurrencyLimiter = new SemaphoreSlim(concurrencyLimit, concurrencyLimit);
        _logger = logger;
    }

    /// <summary>
    /// Executes a work item within the configured rate and concurrency limits.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        await _concurrencyLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        RateLimitLease? lease = null;
        try
        {
            lease = await AcquireRequestAsync(cancellationToken).ConfigureAwait(false);
            return await work(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lease?.Dispose();
            _concurrencyLimiter.Release();
        }
    }

    /// <summary>
    /// Executes a work item without returning a value within the configured rate limits.
    /// </summary>
    public async Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        await _concurrencyLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        RateLimitLease? lease = null;
        try
        {
            lease = await AcquireRequestAsync(cancellationToken).ConfigureAwait(false);
            await work(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lease?.Dispose();
            _concurrencyLimiter.Release();
        }
    }

    /// <summary>
    /// Acquires a concurrency slot returning an <see cref="IDisposable"/> that releases on dispose.
    /// </summary>
    public async Task<IDisposable> AcquireConcurrencyAsync(CancellationToken cancellationToken)
    {
        await _concurrencyLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(_concurrencyLimiter);
    }

    private async Task<RateLimitLease> AcquireRequestAsync(CancellationToken cancellationToken)
    {
        var lease = await _requestLimiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (lease.IsAcquired)
        {
            return lease;
        }

        lease.Dispose();
        _logger.LogWarning("Rate limit exceeded for window of {Window}.", _window);
        throw new RateLimitExceededException("Request rate limit reached.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _requestLimiter.Dispose();
        _concurrencyLimiter.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _released;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _semaphore.Release();
        }
    }
}

/// <summary>
/// Exception thrown when a rate limit prevents execution.
/// </summary>
public sealed class RateLimitExceededException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitExceededException"/> class.
    /// </summary>
    public RateLimitExceededException(string message)
        : base(message)
    {
    }
}
