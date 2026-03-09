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

    /// <summary>Creates a successful import result wrapping the given clip.</summary>
    public static ImportResult Succeeded(Clip clip) =>
        new() { Success = true, Clip = clip, Message = "Import completed successfully." };

    /// <summary>Creates a failed import result with the given reason.</summary>
    public static ImportResult Failed(string reason) =>
        new() { Success = false, Message = reason };

    /// <summary>Creates a skipped result for files that were already present in the library.</summary>
    public static ImportResult Skipped(string reason) =>
        new() { Success = true, Message = reason };
}
