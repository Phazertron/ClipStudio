namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Describes the active audio rendering strategy used by the clip player.
/// </summary>
public enum AudioMode
{
    /// <summary>
    /// A single VLC audio track is selected natively via
    /// <see cref="LibVLCSharp.Shared.MediaPlayer.SetAudioTrack"/>.
    /// No file generation or media reload is required; latency is zero and playback stays
    /// perfectly synchronised with the video.
    /// </summary>
    NativeSingleTrack,

    /// <summary>
    /// Multiple tracks or non-unity volumes require an FFmpeg remux: the video stream is
    /// copied and the audio is replaced by an FFmpeg-mixed WAV baked into a temporary MKV
    /// preview file. VLC opens the preview file directly with no input-slave, avoiding the
    /// pixelation and sync drift that input-slave causes on Windows.
    /// The user triggers this mode explicitly via the Apply Mix button.
    /// </summary>
    MixedRemux,
}
