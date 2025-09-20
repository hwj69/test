using System.Threading;
using System.Threading.Tasks;

namespace Tromby.Voice;

/// <summary>
/// Handles speech-to-text capture via local Whisper models.
/// </summary>
public interface IWhisperService
{
    /// <summary>
    /// Captures and transcribes user speech.
    /// </summary>
    Task<string> CaptureAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Transcribes an existing WAV file using the local Whisper runtime.
    /// </summary>
    /// <param name="filePath">Path to the WAV audio file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> TranscribeAsync(string filePath, CancellationToken cancellationToken);
}
