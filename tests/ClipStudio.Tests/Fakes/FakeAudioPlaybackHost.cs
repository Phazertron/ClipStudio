using ClipStudio.UI.ViewModels;

namespace ClipStudio.Tests.Fakes;

/// <summary>
/// Records the calls <see cref="AudioMixerViewModel"/> makes against the media player, so tests
/// can assert on the ordering rules LibVLC is sensitive about without a real player.
/// </summary>
public sealed class FakeAudioPlaybackHost : IAudioPlaybackHost
{
    /// <summary>Gets or sets the current output volume.</summary>
    public int Volume { get; set; }

    /// <summary>Gets the audio tracks this host reports, in player order.</summary>
    public List<AudioTrackDescriptor> Tracks { get; } = [];

    /// <summary>Gets every track id passed to <see cref="SetAudioTrack"/>, in call order.</summary>
    public List<int> SetAudioTrackCalls { get; } = [];

    /// <summary>Gets every reload, as the path and the deferred track id requested with it.</summary>
    public List<(string Path, int PendingTrackId)> ReloadCalls { get; } = [];

    /// <summary>Gets the context labels passed to <see cref="LogAudioDiagnostics"/>.</summary>
    public List<string> DiagnosticsCalls { get; } = [];

    /// <summary>Adds a track to the list this host reports.</summary>
    /// <param name="id">The player's track identifier.</param>
    /// <param name="name">The track's display name.</param>
    /// <returns>This instance, so calls can be chained.</returns>
    public FakeAudioPlaybackHost WithTrack(int id, string name)
    {
        Tracks.Add(new AudioTrackDescriptor(id, name));
        return this;
    }

    /// <inheritdoc/>
    public void SetAudioTrack(int trackId) => SetAudioTrackCalls.Add(trackId);

    /// <inheritdoc/>
    public IReadOnlyList<AudioTrackDescriptor> GetAudioTracks() => Tracks;

    /// <inheritdoc/>
    public void ReloadMedia(string path, int pendingNativeTrackId = -2)
        => ReloadCalls.Add((path, pendingNativeTrackId));

    /// <inheritdoc/>
    public void LogAudioDiagnostics(string context) => DiagnosticsCalls.Add(context);
}
