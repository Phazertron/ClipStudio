namespace ClipStudio.Application.Models;

/// <summary>
/// Describes the mix parameters for a single audio track when generating a playback preview
/// via <see cref="Interfaces.IMixedAudioService"/>.
/// </summary>
/// <param name="TrackIndex">
/// The VLC track ID for this stream, as returned by
/// <c>MediaPlayer.AudioTrackDescription[n].Id</c>. Used with
/// <c>MediaPlayer.SetAudioTrack()</c> for native single-track selection.
/// </param>
/// <param name="IsIncluded">
/// Whether this track should be included in the mix output.
/// Excluded tracks are omitted entirely from the output stream.
/// </param>
/// <param name="Volume">
/// The volume multiplier applied to this track (0.0 – 2.0; 1.0 = unity gain).
/// </param>
/// <param name="FfmpegStreamIndex">
/// The zero-based audio stream index within the source file as FFmpeg sees it
/// (i.e. the position of this track in the <c>MediaPlayer.AudioTrackDescription</c> list
/// after the disabled <c>Id = -1</c> sentinel is removed). This value, not
/// <paramref name="TrackIndex"/>, must be used for FFmpeg <c>-map 0:a:N</c> arguments.
/// Defaults to 0 for callers that do not need FFmpeg mapping.
/// </param>
public record TrackMixInfo(int TrackIndex, bool IsIncluded, double Volume, int FfmpegStreamIndex = 0);
