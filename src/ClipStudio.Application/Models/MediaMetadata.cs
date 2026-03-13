namespace ClipStudio.Application.Models;

/// <summary>
/// Carries technical metadata extracted from a video file by FFprobe.
/// </summary>
public sealed class MediaMetadata
{
    /// <summary>Gets or sets the total playback duration of the video.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Gets or sets the width of the video frame in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the height of the video frame in pixels.</summary>
    public int Height { get; set; }

    /// <summary>Gets or sets the video codec name (e.g., "h264", "hevc").</summary>
    public string Codec { get; set; } = string.Empty;

    /// <summary>Gets or sets the file size in bytes.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the <c>creation_time</c> metadata tag embedded in the video container,
    /// parsed from the FFprobe format tags. <c>null</c> when the tag is absent or cannot be parsed.
    /// </summary>
    public DateTime? EmbeddedCreationTime { get; set; }

    /// <summary>Gets a formatted resolution string (e.g., "1920x1080").</summary>
    public string Resolution => $"{Width}x{Height}";
}
