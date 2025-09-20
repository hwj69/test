using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tromby.Voice;

/// <summary>
/// Uses Microsoft Edge APIs for neural TTS.
/// </summary>
public sealed class EdgeTtsService : IEdgeTtsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EdgeTtsService> _logger;
    private readonly string _voice;
    private readonly string _format;
    private readonly string? _apiKey;
    private readonly string _region;
    private readonly string _endpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="EdgeTtsService"/> class.
    /// </summary>
    public EdgeTtsService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<EdgeTtsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var section = configuration.GetSection("voice:edge");
        _voice = section["voice"] ?? "en-US-AriaNeural";
        _format = section["outputFormat"] ?? "audio-16khz-128kbitrate-mono-mp3";
        _apiKey = section["apiKey"];
        _region = section["region"] ?? "westeurope";
        _endpoint = section["endpoint"] ?? $"https://{_region}.tts.speech.microsoft.com/cognitiveservices/v1";
    }

    /// <inheritdoc />
    public async Task<Stream?> SpeakAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Edge TTS API key missing. Skipping cloud synthesis.");
            return null;
        }

        var client = _httpClientFactory.CreateClient(nameof(EdgeTtsService));
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _apiKey);
        request.Headers.Add("Ocp-Apim-Subscription-Region", _region);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TrombyApp", "1.0"));
        request.Headers.Add("X-Microsoft-OutputFormat", _format);

        var ssml = BuildSsml(text);
        request.Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("Edge TTS failed with {Status}: {Message}", response.StatusCode, error);
            return null;
        }

        var memory = new MemoryStream();
        await response.Content.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        memory.Position = 0;
        return memory;
    }

    private string BuildSsml(string text)
    {
        var escaped = SecurityElement.Escape(text) ?? string.Empty;
        return $"<speak version='1.0' xml:lang='en-US'><voice name='{_voice}'>{escaped}</voice></speak>";
    }
}
