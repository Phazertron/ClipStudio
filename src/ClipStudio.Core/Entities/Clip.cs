using ClipStudio.Core.Enums;

namespace ClipStudio.Core.Entities;

/// <summary>
/// Represents a video clip file imported into the ClipStudio library.
/// The source file is never modified by the library; all metadata is stored separately.
/// </summary>
public class Clip
{
    /// <summary>Gets or sets the unique identifier of this clip.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the identifier of the source folder this clip was imported from.</summary>
    public int SourceFolderId { get; set; }

    /// <summary>Gets or sets the source folder this clip was imported from.</summary>
    public SourceFolder SourceFolder { get; set; } = null!;

    /// <summary>Gets or sets the absolute path to the video file on disk.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the filename of the video file, including its extension.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Gets or sets the total duration of the video clip.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Gets or sets the video resolution as a formatted string (e.g., "1920x1080").</summary>
    public string Resolution { get; set; } = string.Empty;

    /// <summary>Gets or sets the file size in bytes.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>Gets or sets the UTC timestamp when the file was originally created on disk.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets the UTC timestamp when this clip was imported into the library.</summary>
    public DateTime ImportedAt { get; set; }

    /// <summary>Gets or sets the absolute path to the generated thumbnail image for this clip.</summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>
    /// Gets or sets the absolute path to the generated preview strip image for this clip.
    /// The strip contains multiple frames used for hover-scrub previewing in the UI.
    /// </summary>
    public string? PreviewStripPath { get; set; }

    /// <summary>Gets or sets the current review lifecycle status of this clip.</summary>
    public ClipStatus Status { get; set; } = ClipStatus.Unreviewed;

    /// <summary>Gets or sets the user rating for this clip, from 0 (unrated) to 5 (highest).</summary>
    public int Rating { get; set; } = 0;

    /// <summary>Gets or sets whether the user has marked this clip as a favourite.</summary>
    public bool IsFavourite { get; set; } = false;

    /// <summary>Gets or sets optional free-text notes added by the user for this clip.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the raw game name string parsed from the filename (e.g., from the OBS script
    /// convention "[Game Name]"). This is a suggestion only; the user must confirm it as a tag.
    /// </summary>
    public string? SuggestedGameName { get; set; }

    /// <summary>Gets the collection of tags applied directly to this clip.</summary>
    public ICollection<ClipTag> ClipTags { get; set; } = new List<ClipTag>();

    /// <summary>Gets the collection of highlights (tagged time ranges) defined within this clip.</summary>
    public ICollection<Highlight> Highlights { get; set; } = new List<Highlight>();

    /// <summary>Gets the collection of screenshots captured from this clip.</summary>
    public ICollection<Screenshot> Screenshots { get; set; } = new List<Screenshot>();

    /// <summary>Gets the collection of export jobs associated with this clip.</summary>
    public ICollection<ExportJob> ExportJobs { get; set; } = new List<ExportJob>();

    /// <summary>Gets the collection of player-participation records for this clip.</summary>
    public ICollection<ClipPlayer> ClipPlayers { get; set; } = new List<ClipPlayer>();

    /// <summary>Gets the collection of per-track audio settings for this clip.</summary>
    public ICollection<AudioTrackSetting> AudioTracks { get; set; } = new List<AudioTrackSetting>();

    /// <summary>
    /// Gets or sets a value indicating whether this clip has been moved to the application trash.
    /// Trashed clips are hidden from the library and unreviewed queue.
    /// </summary>
    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// Gets or sets the UTC timestamp when this clip was moved to the trash, or null if not trashed.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Gets or sets the absolute path to the clip file inside the application trash folder,
    /// or null if the clip has not been trashed. The original <see cref="FilePath"/> retains the
    /// source location for restore operations.
    /// </summary>
    public string? TrashPath { get; set; }
}
