using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Tromby.Core.Sessions;

/// <summary>
/// Session store leveraging DPAPI to encrypt persisted conversation history.
/// </summary>
public sealed class DpapiSessionStore : ISessionStore
{
    private readonly ILogger<DpapiSessionStore> _logger;
    private readonly string _sessionPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tromby", "session.bin");
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private const int MaxSnapshotBytes = 512 * 1024;

    /// <summary>
    /// Gets the last restored session snapshot.
    /// </summary>
    public string LastSnapshot { get; private set; } = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="DpapiSessionStore"/> class.
    /// </summary>
    public DpapiSessionStore(ILogger<DpapiSessionStore> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_sessionPath))
        {
            LastSnapshot = string.Empty;
            return;
        }

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = await File.ReadAllBytesAsync(_sessionPath, cancellationToken).ConfigureAwait(false);
            var decrypted = ProtectedData.Unprotect(payload, null, DataProtectionScope.CurrentUser);
            LastSnapshot = Encoding.UTF8.GetString(decrypted);
            _logger.LogDebug("Restored session payload of {Length} characters.", LastSnapshot.Length);
            // TODO: hydrate in-memory conversation state.
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore session payload.");
            LastSnapshot = string.Empty;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task AppendAsync(string message, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(_sessionPath)!);
            if (File.Exists(_sessionPath))
            {
                try
                {
                    var existingBytes = await File.ReadAllBytesAsync(_sessionPath, cancellationToken).ConfigureAwait(false);
                    var decrypted = ProtectedData.Unprotect(existingBytes, null, DataProtectionScope.CurrentUser);
                    LastSnapshot = Encoding.UTF8.GetString(decrypted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load existing session before append.");
                    LastSnapshot = string.Empty;
                }
            }

            var builder = new StringBuilder(LastSnapshot);
            builder.AppendLine(message);
            var snapshot = builder.ToString();
            var utf8Bytes = Encoding.UTF8.GetBytes(snapshot);
            if (utf8Bytes.Length > MaxSnapshotBytes)
            {
                var trimmed = TrimToBudget(utf8Bytes, MaxSnapshotBytes);
                snapshot = trimmed;
                utf8Bytes = Encoding.UTF8.GetBytes(snapshot);
                _logger.LogWarning("Session snapshot truncated to {Limit} bytes to respect storage budget.", MaxSnapshotBytes);
            }

            LastSnapshot = snapshot;
            var encrypted = ProtectedData.Protect(utf8Bytes, null, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(_sessionPath, encrypted, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist session snapshot.");
        }
        finally
        {
            _mutex.Release();
        }
    }

    private static string TrimToBudget(byte[] source, int limit)
    {
        if (source.Length <= limit)
        {
            return Encoding.UTF8.GetString(source);
        }

        var start = source.Length - limit;
        return Encoding.UTF8.GetString(source, start, limit);
    }
}
