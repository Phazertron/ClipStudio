namespace ClipStudio.Core.Entities;

/// <summary>
/// Persisted per-clip, per-track audio settings used when exporting.
/// Allows individual tracks to be muted or have their volume adjusted in the exported file.
/// </summary>
public class AudioTrackSetting
{
    /// <summary>Gets or sets the primary key.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the identifier of the owning clip.</summary>
    public int ClipId { get; set; }

    /// <summary>Gets or sets the clip navigation property.</summary>
    public Clip Clip { get; set; } = null!;

    /// <summary>
    /// Gets or sets the VLC/FFmpeg audio track index (zero-based within the file).
    /// This is the index reported by <c>MediaPlayer.AudioTrackDescription</c>.
    /// </summary>
    public int TrackIndex { get; set; }

    /// <summary>Gets or sets the user-assigned display name for this track (e.g. "Game Audio", "Mic").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets whether this track is muted in the exported output.</summary>
    public bool IsMuted { get; set; } = false;

    /// <summary>
    /// Gets or sets the volume multiplier applied to this track in the exported output.
    /// 1.0 means 100 % (no change). 0.0 means silent. 2.0 means 200 %.
    /// </summary>
    public double Volume { get; set; } = 1.0;
}
