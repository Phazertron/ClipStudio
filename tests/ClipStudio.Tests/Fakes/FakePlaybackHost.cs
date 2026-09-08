using ClipStudio.UI.ViewModels;

namespace ClipStudio.Tests.Fakes;

/// <summary>
/// Stands in for the media player so <see cref="PlaybackViewModel"/> can be driven without one.
/// </summary>
public sealed class FakePlaybackHost : IPlaybackHost
{
    /// <inheritdoc/>
    public long TimeMs { get; set; }

    /// <inheritdoc/>
    public bool IsPlayerPlaying { get; set; }

    /// <inheritdoc/>
    public float Fps { get; set; }

    /// <summary>Gets the number of times playback was started.</summary>
    public int PlayCalls { get; private set; }

    /// <summary>Gets the number of times playback was paused.</summary>
    public int PauseCalls { get; private set; }

    /// <inheritdoc/>
    public void Play()
    {
        PlayCalls++;
        IsPlayerPlaying = true;
    }

    /// <inheritdoc/>
    public void Pause()
    {
        PauseCalls++;
        IsPlayerPlaying = false;
    }
}
