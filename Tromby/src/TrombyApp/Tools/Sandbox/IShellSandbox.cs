using System.Threading;
using System.Threading.Tasks;

namespace Tromby.Tools.Sandbox;

/// <summary>
/// Executes PowerShell scripts within a constrained sandbox.
/// </summary>
public interface IShellSandbox
{
    /// <summary>
    /// Executes a script with optional confirmation.
    /// </summary>
    Task<string> ExecuteAsync(string script, bool requireConfirmation, CancellationToken cancellationToken);
}
