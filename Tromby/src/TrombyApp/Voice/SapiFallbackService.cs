using System;
using System.IO;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Tromby.Voice;

/// <summary>
/// Uses Windows SAPI for fallback speech synthesis.
/// </summary>
public sealed class SapiFallbackService : ISapiFallbackService
{
    private readonly ILogger<SapiFallbackService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SapiFallbackService"/> class.
    /// </summary>
    public SapiFallbackService(ILogger<SapiFallbackService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Stream> SpeakAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogWarning("Using SAPI fallback for speech synthesis.");

        var memoryStream = new MemoryStream();
        using var synthesizer = new SpeechSynthesizer();
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnCompleted(object? sender, SpeakCompletedEventArgs args)
        {
            if (args.Cancelled)
            {
                tcs.TrySetCanceled();
            }
            else if (args.Error != null)
            {
                tcs.TrySetException(args.Error);
            }
            else
            {
                tcs.TrySetResult(null);
            }
        }

        synthesizer.SpeakCompleted += OnCompleted;
        synthesizer.SetOutputToWaveStream(memoryStream);

        using var registration = cancellationToken.Register(() =>
        {
            synthesizer.SpeakAsyncCancelAll();
            tcs.TrySetCanceled(cancellationToken);
        });

        synthesizer.SpeakAsync(text);
        try
        {
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            synthesizer.SpeakCompleted -= OnCompleted;
        }

        memoryStream.Position = 0;
        return memoryStream;
    }
}
