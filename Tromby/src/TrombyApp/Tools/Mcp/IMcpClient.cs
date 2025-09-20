using System.Threading;
using System.Threading.Tasks;

namespace Tromby.Tools.Mcp;

/// <summary>
/// Minimal MCP client abstraction for executing local actions.
/// </summary>
public interface IMcpClient
{
    /// <summary>
    /// Executes a named MCP action.
    /// </summary>
    Task ExecuteActionAsync(string actionName, string payload, CancellationToken cancellationToken);
}
