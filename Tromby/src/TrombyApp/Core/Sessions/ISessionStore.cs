using System.Threading;
using System.Threading.Tasks;

namespace Tromby.Core.Sessions;

/// <summary>
/// Provides persistence for encrypted conversation sessions.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Restores the last session into memory.
    /// </summary>
    Task RestoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Appends a new entry to the current session and persists it.
    /// </summary>
    /// <param name="message">Content to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AppendAsync(string message, CancellationToken cancellationToken);
}
