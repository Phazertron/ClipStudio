namespace ClipStudio.Core.Entities;

/// <summary>
/// Represents a watched folder on disk from which clips are imported into the library.
/// </summary>
public class SourceFolder
{
    /// <summary>Gets or sets the unique identifier of this source folder.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the absolute path to the folder on disk.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets whether this folder is currently being watched for new files.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Gets or sets the UTC timestamp of the last scan performed on this folder.</summary>
    public DateTime? LastScannedAt { get; set; }

    /// <summary>Gets the collection of clips imported from this folder.</summary>
    public ICollection<Clip> Clips { get; set; } = new List<Clip>();
}
