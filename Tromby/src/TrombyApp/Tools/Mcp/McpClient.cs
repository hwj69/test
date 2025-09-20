using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Tromby.Tools.Mcp;

/// <summary>
/// Placeholder MCP client bridging to local action providers.
/// </summary>
public sealed class McpClient : IMcpClient
{
    private readonly ILogger<McpClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpClient"/> class.
    /// </summary>
    public McpClient(ILogger<McpClient> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task ExecuteActionAsync(string actionName, string payload, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing MCP action {Action} with payload length {Length}.", actionName, payload?.Length ?? 0);
        // TODO: Integrate with MCP runtime with sandbox enforcement.
        return Task.CompletedTask;
    }
}
