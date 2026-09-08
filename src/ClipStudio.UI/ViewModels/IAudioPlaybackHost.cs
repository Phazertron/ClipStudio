using System.Collections.Generic;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// The narrow view of the media player that <see cref="AudioMixerViewModel"/> needs.
/// </summary>
/// <remarks>
/// Implemented by <see cref="ClipDetailViewModel"/>, which owns the LibVLC media player and the
/// media-reload lifecycle. Keeping the mixer behind this seam means it carries no reference to
/// LibVLC and can be exercised in tests, while the ordering rules that LibVLC is sensitive
/// about - when <see cref="SetAudioTrack"/> may be called, and when a reload is allowed to seek
/// back - stay in one place in the parent.
/// </remarks>
public interface IAudioPlaybackHost
{
    /// <summary>Gets or sets the player's output volume, 0-100.</summary>
    int Volume { get; set; }

    /// <summary>
    /// Selects an audio track natively, with no file generation or reload.
    /// </summary>
    /// <param name="trackId">
    /// The identifier of the track to select, or -1 to disable audio output entirely.
    /// </param>
    void SetAudioTrack(int trackId);

    /// <summary>
    /// Returns the audio tracks the player has discovered for the open media, excluding the
    /// player's own "disabled" sentinel entry.
    /// </summary>
    /// <returns>
    /// The tracks in player order. The position of a track in this list is its 0-based FFmpeg
    /// stream index. Empty until playback has actually started.
    /// </returns>
    IReadOnlyList<AudioTrackDescriptor> GetAudioTracks();

    /// <summary>
    /// Reopens the player on a different file, preserving the current playback position.
    /// </summary>
    /// <param name="path">
    /// The media to open: either the original clip or a generated remux preview.
    /// </param>
    /// <param name="pendingNativeTrackId">
    /// A track to select once the reload completes, or -2 for none. Deferred because the track
    /// cannot be selected until the player reports that the new media is playing.
    /// </param>
    void ReloadMedia(string path, int pendingNativeTrackId = -2);

    /// <summary>Writes a diagnostic snapshot of the player's audio state to the audio log.</summary>
    /// <param name="context">A short label identifying the call site.</param>
    void LogAudioDiagnostics(string context);
}
