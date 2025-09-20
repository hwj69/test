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
/// Adapter for xAI Grok models.
/// </summary>
public sealed class XaiClient : ILLMClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<XaiClient> _logger;
    private readonly string _endpoint;
    private readonly string? _apiKey;
    private readonly string _model;

    /// <summary>
    /// Initializes a new instance of the <see cref="XaiClient"/> class.
    /// </summary>
    public XaiClient(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<XaiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var section = configuration.GetSection("llm:xai");
        _endpoint = section["endpoint"] ?? "https://api.x.ai/v1/chat/completions";
        _apiKey = section["apiKey"];
        _model = section["model"] ?? "grok-beta";
    }

    /// <inheritdoc />
    public string Name => "xai";

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string prompt, string persona, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("xAI API key missing, returning simulated completion.");
            return $"[xAI simulé] {prompt}";
        }

        var client = _httpClientFactory.CreateClient(nameof(XaiClient));
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
            throw new InvalidOperationException($"xAI completion failed: {response.StatusCode} - {error}");
        }

        var payload = await response.Content.ReadFromJsonAsync<XaiResponse>(SerializerOptions, cancellationToken).ConfigureAwait(false);
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

    private sealed record XaiResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<XaiChoice>? Choices);

    private sealed record XaiChoice(
        [property: JsonPropertyName("message")] XaiMessage? Message);

    private sealed record XaiMessage(
        [property: JsonPropertyName("content")] string? Content);
}
