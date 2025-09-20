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
/// Adapter for OpenAI compatible HTTP APIs.
/// </summary>
public sealed class OpenAiClient : ILLMClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiClient> _logger;
    private readonly string _endpoint;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly double _temperature;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAiClient"/> class.
    /// </summary>
    public OpenAiClient(HttpClient httpClient, IConfiguration configuration, ILogger<OpenAiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var section = configuration.GetSection("llm:openai");
        _endpoint = section["endpoint"] ?? "https://api.openai.com/v1/chat/completions";
        _apiKey = section["apiKey"];
        _model = section["model"] ?? "gpt-4o-mini";
        _temperature = section.GetValue<double?>("temperature") ?? 0.7d;

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        var timeoutSeconds = section.GetValue("timeoutSeconds", 60);
        if (timeoutSeconds > 0)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }
    }

    /// <inheritdoc />
    public string Name => "openai";

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string prompt, string persona, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("OpenAI API key missing, returning simulated completion.");
            return $"[OpenAI simulé] {prompt}";
        }

        var body = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = persona },
                new { role = "user", content = prompt }
            },
            temperature = _temperature,
            stream = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = JsonContent.Create(body, options: SerializerOptions)
        };

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"OpenAI completion failed: {response.StatusCode} - {error}");
        }

        var payload = await response.Content.ReadFromJsonAsync<OpenAiResponse>(SerializerOptions, cancellationToken).ConfigureAwait(false);
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

    private sealed record OpenAiResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<OpenAiChoice>? Choices);

    private sealed record OpenAiChoice(
        [property: JsonPropertyName("message")] OpenAiMessage? Message);

    private sealed record OpenAiMessage(
        [property: JsonPropertyName("content")] string? Content);
}
