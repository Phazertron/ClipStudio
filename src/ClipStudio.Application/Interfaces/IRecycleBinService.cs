namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Provides cross-platform support for moving files to the operating system recycle bin
/// (or equivalent trash folder) instead of permanently deleting them.
/// </summary>
public interface IRecycleBinService
{
    /// <summary>
    /// Attempts to move the file at <paramref name="path"/> to the OS recycle bin / trash folder.
    /// </summary>
    /// <param name="path">The absolute path of the file to recycle.</param>
    /// <returns>
    /// <see langword="true"/> if the file was successfully moved to the recycle bin;
    /// <see langword="false"/> if the operation failed (the file will have been permanently deleted
    /// as a fallback).
    /// </returns>
    bool TryMoveToRecycleBin(string path);
}
