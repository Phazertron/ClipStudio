using ClipStudio.Core.Entities;

namespace ClipStudio.Application.Models;

/// <summary>
/// Represents the outcome of a single file import operation.
/// </summary>
public sealed class ImportResult
{
    /// <summary>Gets or sets whether the import completed successfully.</summary>
    public bool Success { get; set; }

    /// <summary>Gets or sets the imported clip entity when <see cref="Success"/> is true.</summary>
    public Clip? Clip { get; set; }

    /// <summary>Gets or sets a human-readable message describing the outcome or failure reason.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the clip already in the library holding the same content, when the import
    /// stopped because the file is a duplicate.
    /// </summary>
    /// <remarks>
    /// Non-null only for <see cref="IsDuplicate"/> results. The caller decides what happens next -
    /// skipping is not assumed, because the same recording under a different name is sometimes
    /// wanted.
    /// </remarks>
    public Clip? DuplicateOf { get; set; }

    /// <summary>Gets or sets the path of the file that was found to be a duplicate.</summary>
    public string? DuplicateFilePath { get; set; }

    /// <summary>Gets or sets how the match was recognised: by file name, or by contents.</summary>
    public DuplicateMatchKind DuplicateMatch { get; set; }

    /// <summary>
    /// Gets whether the import stopped because the file is already in the library by content.
    /// </summary>
    public bool IsDuplicate => DuplicateOf is not null;

    /// <summary>Creates a successful import result wrapping the given clip.</summary>
    public static ImportResult Succeeded(Clip clip) =>
        new() { Success = true, Clip = clip, Message = "Import completed successfully." };

    /// <summary>Creates a failed import result with the given reason.</summary>
    public static ImportResult Failed(string reason) =>
        new() { Success = false, Message = reason };

    /// <summary>Creates a skipped result for files that were already present in the library.</summary>
    public static ImportResult Skipped(string reason) =>
        new() { Success = true, Message = reason };

    /// <summary>
    /// Creates a result for a file whose contents match a clip already in the library. Nothing has
    /// been imported; the caller chooses whether to skip it or import it anyway.
    /// </summary>
    /// <param name="filePath">The path of the file being imported.</param>
    /// <param name="existing">The clip already in the library that it matched.</param>
    /// <param name="match">How the match was recognised.</param>
    /// <returns>The duplicate result.</returns>
    public static ImportResult Duplicate(
        string filePath, Clip existing, DuplicateMatchKind match = DuplicateMatchKind.Content) =>
        new()
        {
            Success           = true,
            DuplicateOf       = existing,
            DuplicateFilePath = filePath,
            DuplicateMatch    = match,
            Message           = match == DuplicateMatchKind.FileName
                ? $"A clip named \"{existing.FileName}\" is already in the library."
                : $"Already in the library as \"{existing.FileName}\".",
        };
}
