namespace ClipStudio.UI.ViewModels;

/// <summary>
/// The narrow view of the media player that <see cref="PlaybackViewModel"/> needs.
/// </summary>
/// <remarks>
/// Implemented by <see cref="ClipDetailViewModel"/>, which owns the LibVLC media player. Seeks made
/// through this interface are raw: they do not arm the stale-event guard, because
/// <see cref="PlaybackViewModel"/> owns that guard and arms it around its own seeks.
/// </remarks>
public interface IPlaybackHost
{
    /// <summary>Gets or sets the player's position in milliseconds from the start of the media.</summary>
    long TimeMs { get; set; }

    /// <summary>Gets a value indicating whether the player is currently playing.</summary>
    bool IsPlayerPlaying { get; }

    /// <summary>Gets the media's frame rate, or 0 when it is not known yet.</summary>
    float Fps { get; }

    /// <summary>Starts or resumes playback.</summary>
    void Play();

    /// <summary>Pauses playback.</summary>
    void Pause();
}
