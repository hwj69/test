using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tromby.Core.Persona;
using Tromby.Core.Scheduling;
using Tromby.Core.Sessions;
using Tromby.LLM.Router;
using Tromby.Tools.Mcp;
using Tromby.Voice;

namespace Tromby.Core.Orchestration;

/// <summary>
/// Default orchestrator implementation coordinating audio, MCP actions, and LLM routing.
/// </summary>
public sealed class OverlayOrchestrator : IOverlayOrchestrator
{
    private readonly LLMProviderRouter _router;
    private readonly IPersonaManager _personaManager;
    private readonly IScheduler _scheduler;
    private readonly ISessionStore _sessionStore;
    private readonly IMcpClient _mcpClient;
    private readonly IWhisperService _whisperService;
    private readonly IEdgeTtsService _edgeTtsService;
    private readonly ISapiFallbackService _sapiFallback;
    private readonly ILogger<OverlayOrchestrator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OverlayOrchestrator"/> class.
    /// </summary>
    public OverlayOrchestrator(
        LLMProviderRouter router,
        IPersonaManager personaManager,
        IScheduler scheduler,
        ISessionStore sessionStore,
        IMcpClient mcpClient,
        IWhisperService whisperService,
        IEdgeTtsService edgeTtsService,
        ISapiFallbackService sapiFallback,
        ILogger<OverlayOrchestrator> logger)
    {
        _router = router;
        _personaManager = personaManager;
        _scheduler = scheduler;
        _sessionStore = sessionStore;
        _mcpClient = mcpClient;
        _whisperService = whisperService;
        _edgeTtsService = edgeTtsService;
        _sapiFallback = sapiFallback;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting interaction in {Mode} mode.", _scheduler.ActiveMode);
        try
        {
            var request = await _whisperService.CaptureAsync(cancellationToken).ConfigureAwait(false);
            var persona = await _personaManager.GetActivePersonaAsync(cancellationToken).ConfigureAwait(false);
            var llmResponse = await _router.SendAsync(request, persona, cancellationToken).ConfigureAwait(false);

            // TODO: Evaluate MCP action triggers before speaking response.
            var speechStream = await _edgeTtsService.SpeakAsync(llmResponse, cancellationToken).ConfigureAwait(false);
            if (speechStream == null)
            {
                speechStream = await _sapiFallback.SpeakAsync(llmResponse, cancellationToken).ConfigureAwait(false);
            }

            await _sessionStore.AppendAsync(llmResponse, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Interaction cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Interaction failed.");
            // TODO: notify UI of failure state.
        }
    }
}
