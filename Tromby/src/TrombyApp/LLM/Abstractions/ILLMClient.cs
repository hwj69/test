using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tromby.LLM.Abstractions;

/// <summary>
/// Provides a unified contract for interacting with LLM providers, including streaming and cost estimation helpers.
/// </summary>
public interface ILLMClient
{
    /// <summary>
    /// Gets the logical provider name (OpenAI, Mistral, Local, etc.).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Sends a prompt to the provider and returns the aggregated completion.
    /// </summary>
    /// <param name="prompt">Prompt payload.</param>
    /// <param name="persona">Persona instructions sent as a system message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> CompleteAsync(string prompt, string persona, CancellationToken cancellationToken);

    /// <summary>
    /// Streams a completion response chunk-by-chunk.
    /// </summary>
    /// <param name="prompt">Prompt payload.</param>
    /// <param name="persona">Persona instructions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IAsyncEnumerable<string>> StreamAsync(string prompt, string persona, CancellationToken cancellationToken);

    /// <summary>
    /// Estimates token usage for budgeting purposes.
    /// </summary>
    /// <param name="text">Text payload to evaluate.</param>
    int EstimateTokens(string text);
}
