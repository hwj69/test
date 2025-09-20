using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Tromby.Voice;

/// <summary>
/// Provides speech synthesis via Microsoft Edge TTS service.
/// </summary>
public interface IEdgeTtsService
{
    /// <summary>
    /// Synthesizes text into audio stream.
    /// </summary>
    Task<Stream?> SpeakAsync(string text, CancellationToken cancellationToken);
}
