using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tromby.LLM.Abstractions;

namespace Tromby.LLM.Adapters;

/// <summary>
/// Adapter for Google Gemini APIs.
/// </summary>
public sealed class GeminiClient : ILLMClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GeminiClient> _logger;
    private readonly string _endpoint;
    private readonly string? _apiKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeminiClient"/> class.
    /// </summary>
    public GeminiClient(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<GeminiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var section = configuration.GetSection("llm:gemini");
        var model = section["model"] ?? "gemini-pro";
        var baseEndpoint = section["endpoint"] ?? "https://generativelanguage.googleapis.com/v1beta";
        _endpoint = $"{baseEndpoint.TrimEnd('/')}/models/{model}:generateContent";
        _apiKey = section["apiKey"];
    }

    /// <inheritdoc />
    public string Name => "gemini";

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string prompt, string persona, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Gemini API key missing, returning simulated completion.");
            return $"[Gemini simulé] {prompt}";
        }

        var client = _httpClientFactory.CreateClient(nameof(GeminiClient));
        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = persona },
                        new { text = prompt }
                    }
                }
            }
        };

        var requestUri = new UriBuilder(_endpoint) { Query = $"key={_apiKey}" }.Uri;
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(body, options: SerializerOptions)
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Gemini completion failed: {response.StatusCode} - {error}");
        }

        var payload = await response.Content.ReadFromJsonAsync<GeminiResponse>(SerializerOptions, cancellationToken).ConfigureAwait(false);
        var completion = payload?.Candidates?
            .SelectMany(candidate => candidate.Content?.Parts ?? Array.Empty<GeminiPart>())
            .Select(part => part.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Aggregate(string.Empty, (acc, next) => string.Concat(acc, next)) ?? string.Empty;

        return completion;
    }

    /// <inheritdoc />
    public async Task<IAsyncEnumerable<string>> StreamAsync(string prompt, string persona, CancellationToken cancellationToken)
    {
        var completion = await CompleteAsync(prompt, persona, cancellationToken).ConfigureAwait(false);
        return CreateSingleChunk(completion);
    }

    /// <inheritdoc />
    public int EstimateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return Math.Max(1, text.Length / 4);
    }

    private static Task<IAsyncEnumerable<string>> CreateSingleChunk(string content)
    {
        async IAsyncEnumerable<string> Enumerate()
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                yield return content;
            }

            await Task.CompletedTask;
        }

        return Task.FromResult<IAsyncEnumerable<string>>(Enumerate());
    }

    private sealed record GeminiResponse(
        [property: JsonPropertyName("candidates")] IReadOnlyList<GeminiCandidate>? Candidates);

    private sealed record GeminiCandidate(
        [property: JsonPropertyName("content")] GeminiContent? Content);

    private sealed record GeminiContent(
        [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart>? Parts);

    private sealed record GeminiPart(
        [property: JsonPropertyName("text")] string? Text);
}
