using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tromby.LLM.Abstractions;

namespace Tromby.LLM.Adapters;

/// <summary>
/// Adapter for local inference engines (GGUF, ONNX, etc.).
/// </summary>
public sealed class LocalProviderClient : ILLMClient
{
    private readonly ILogger<LocalProviderClient> _logger;
    private readonly string? _command;
    private readonly string? _arguments;
    private readonly string? _workingDirectory;
    private readonly string _inputTemplate;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalProviderClient"/> class.
    /// </summary>
    public LocalProviderClient(IConfiguration configuration, ILogger<LocalProviderClient> logger)
    {
        _logger = logger;
        var section = configuration.GetSection("llm:local");
        _command = section["command"];
        _arguments = section["arguments"];
        _workingDirectory = section["workingDirectory"];
        _inputTemplate = section["inputTemplate"] ?? "{{persona}}\n\n{{prompt}}";
    }

    /// <inheritdoc />
    public string Name => "local";

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string prompt, string persona, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_command))
        {
            _logger.LogWarning("Local LLM command not configured. Echoing prompt.");
            return $"{persona}{Environment.NewLine}{prompt}";
        }

        var payload = _inputTemplate
            .Replace("{{persona}}", persona, StringComparison.Ordinal)
            .Replace("{{prompt}}", prompt, StringComparison.Ordinal);

        var startInfo = new ProcessStartInfo
        {
            FileName = _command!,
            Arguments = _arguments ?? string.Empty,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(_workingDirectory))
        {
            startInfo.WorkingDirectory = _workingDirectory;
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is { Length: > 0 })
            {
                lock (outputBuilder)
                {
                    outputBuilder.AppendLine(args.Data);
                }
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is { Length: > 0 })
            {
                lock (errorBuilder)
                {
                    errorBuilder.AppendLine(args.Data);
                }
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start local provider process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.StandardInput.WriteAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
        process.StandardInput.Close();

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to terminate local LLM process on cancellation.");
            }
        });

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var error = errorBuilder.ToString();
            throw new InvalidOperationException($"Local provider failed with exit code {process.ExitCode}: {error}");
        }

        var response = outputBuilder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(response))
        {
            _logger.LogWarning("Local provider returned empty response.");
        }

        return response;
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

        return Math.Max(1, text.Length / 3);
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
}
