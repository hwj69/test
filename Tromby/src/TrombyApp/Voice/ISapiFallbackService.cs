using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Tromby.Voice;

/// <summary>
/// Provides legacy SAPI fallback when Edge TTS is unavailable.
/// </summary>
public interface ISapiFallbackService
{
    /// <summary>
    /// Synthesizes text and returns audio stream.
    /// </summary>
    Task<Stream> SpeakAsync(string text, CancellationToken cancellationToken);
}
