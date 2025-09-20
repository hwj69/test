using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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
/// Adapter for the Mistral API.
/// </summary>
public sealed class MistralClient : ILLMClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MistralClient> _logger;
    private readonly string _endpoint;
    private readonly string? _apiKey;
    private readonly string _model;

    /// <summary>
    /// Initializes a new instance of the <see cref="MistralClient"/> class.
    /// </summary>
    public MistralClient(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<MistralClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var section = configuration.GetSection("llm:mistral");
        _endpoint = section["endpoint"] ?? "https://api.mistral.ai/v1/chat/completions";
        _apiKey = section["apiKey"];
        _model = section["model"] ?? "mistral-large-latest";
    }

    /// <inheritdoc />
    public string Name => "mistral";

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string prompt, string persona, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Mistral API key missing, returning simulated completion.");
            return $"[Mistral simulé] {prompt}";
        }

        var client = _httpClientFactory.CreateClient(nameof(MistralClient));
        var body = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = persona },
                new { role = "user", content = prompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = JsonContent.Create(body, options: SerializerOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Mistral completion failed: {response.StatusCode} - {error}");
        }

        var payload = await response.Content.ReadFromJsonAsync<MistralResponse>(SerializerOptions, cancellationToken).ConfigureAwait(false);
        var completion = payload?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
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

    private sealed record MistralResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<MistralChoice>? Choices);

    private sealed record MistralChoice(
        [property: JsonPropertyName("message")] MistralMessage? Message);

    private sealed record MistralMessage(
        [property: JsonPropertyName("content")] string? Content);
}
