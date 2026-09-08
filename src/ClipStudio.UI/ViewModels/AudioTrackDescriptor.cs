namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Describes one audio track exposed by the media player, stripped of any LibVLC types.
/// </summary>
/// <param name="Id">
/// The player's audio track identifier, passed back to
/// <see cref="IAudioPlaybackHost.SetAudioTrack"/> to select this track natively.
/// </param>
/// <param name="Name">The track's display name as reported by the player.</param>
public sealed record AudioTrackDescriptor(int Id, string Name);
