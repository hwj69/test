using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace Tromby.Voice;

/// <summary>
/// Local Whisper inference wrapper.
/// </summary>
public sealed class WhisperService : IWhisperService
{
    private readonly ILogger<WhisperService> _logger;
    private readonly string? _executablePath;
    private readonly string _modelPath;
    private readonly string? _language;
    private readonly string? _additionalArgs;
    private readonly TimeSpan _captureDuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="WhisperService"/> class.
    /// </summary>
    public WhisperService(IConfiguration configuration, ILogger<WhisperService> logger)
    {
        _logger = logger;
        var section = configuration.GetSection("voice:whisper");
        _executablePath = section["executable"];
        _modelPath = section["model"] ?? "models/ggml-base.en.bin";
        _language = section["language"];
        _additionalArgs = section["extraArgs"];
        _captureDuration = TimeSpan.FromSeconds(section.GetValue("captureSeconds", 6));
    }

    /// <inheritdoc />
    public async Task<string> CaptureAsync(CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"tromby-whisper-{Guid.NewGuid():N}.wav");
        try
        {
            await RecordMicrophoneAsync(tempFile, cancellationToken).ConfigureAwait(false);
            return await TranscribeAsync(tempFile, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    /// <inheritdoc />
    public async Task<string> TranscribeAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_executablePath) || !File.Exists(_executablePath))
        {
            _logger.LogWarning("Whisper executable not configured or missing at {Path}.", _executablePath);
            return string.Empty;
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Audio file not found.", filePath);
        }

        var arguments = BuildArguments(filePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath!,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Whisper process.");
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
                _logger.LogWarning(ex, "Failed to cancel Whisper process cleanly.");
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await stdoutTask.ConfigureAwait(false);
        var error = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Whisper exited with code {process.ExitCode}: {error}");
        }

        return ParseWhisperOutput(output);
    }

    private async Task RecordMicrophoneAsync(string destinationPath, CancellationToken cancellationToken)
    {
        using var capture = new MediaCapture();
        var settings = new MediaCaptureInitializationSettings
        {
            StreamingCaptureMode = StreamingCaptureMode.Audio
        };

        await capture.InitializeAsync(settings).AsTask(cancellationToken);

        var tempFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(
            $"tromby-capture-{Guid.NewGuid():N}.wav",
            CreationCollisionOption.ReplaceExisting).AsTask(cancellationToken);

        var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.Auto);
        await capture.StartRecordToStorageFileAsync(profile, tempFile).AsTask(cancellationToken);

        try
        {
            await Task.Delay(_captureDuration, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await capture.StopRecordAsync().AsTask();
        }

        using var sourceStream = await tempFile.OpenReadAsync().AsTask(cancellationToken);
        await using var destination = File.Create(destinationPath);
        await sourceStream.AsStreamForRead().CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        await tempFile.DeleteAsync().AsTask();
    }

    private string BuildArguments(string filePath)
    {
        var builder = new StringBuilder();
        builder.Append("--model \"").Append(_modelPath).Append("\"");
        builder.Append(' ').Append("--file \"").Append(filePath).Append("\"");
        builder.Append(" --output_format json");
        if (!string.IsNullOrWhiteSpace(_language))
        {
            builder.Append(" --language ").Append(_language);
        }

        if (!string.IsNullOrWhiteSpace(_additionalArgs))
        {
            builder.Append(' ').Append(_additionalArgs);
        }

        return builder.ToString();
    }

    private static string ParseWhisperOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var builder = new StringBuilder();
                foreach (var segment in document.RootElement.EnumerateArray())
                {
                    if (segment.TryGetProperty("text", out var textElement))
                    {
                        builder.Append(textElement.GetString());
                    }
                }

                return builder.ToString().Trim();
            }

            if (document.RootElement.TryGetProperty("text", out var text))
            {
                return text.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Fallback to raw output below.
        }

        return output.Trim();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore cleanup failures
        }
    }
}
