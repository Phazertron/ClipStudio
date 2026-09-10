namespace ClipStudio.Core.Models;

/// <summary>
/// The few columns a library-wide pass needs from a clip row.
/// </summary>
/// <param name="Id">The clip's database identifier.</param>
/// <param name="SourceFolderId">The source folder the clip belongs to.</param>
/// <param name="FilePath">The absolute path the clip's row points at.</param>
/// <param name="FileName">The clip's file name, for messages.</param>
/// <param name="IsBroken">Whether the clip is currently flagged as broken.</param>
/// <param name="FileHash">The clip's stored quick hash, or null when it has never been hashed.</param>
/// <remarks>
/// Loading whole clips to answer "is the file still there" or "do any two of these share a hash"
/// pulls every tag, player and highlight with them, which is one of the two costs that made the
/// old startup pass slow. This projection is what those passes read instead.
/// </remarks>
public sealed record ClipFileSnapshot(
    int Id,
    int SourceFolderId,
    string FilePath,
    string FileName,
    bool IsBroken,
    string? FileHash = null);
