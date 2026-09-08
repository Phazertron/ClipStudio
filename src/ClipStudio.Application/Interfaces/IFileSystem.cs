namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Abstracts the file-system operations used by the application services.
/// </summary>
/// <remarks>
/// Only the operations the services actually perform are exposed, deliberately kept as a thin
/// mirror of <see cref="System.IO.File"/> and <see cref="System.IO.Directory"/> so that call sites
/// read the same as before. Pure path manipulation (<see cref="System.IO.Path"/>) performs no I/O
/// and is intentionally left out.
/// The production implementation is <see cref="Services.PhysicalFileSystem"/>; tests substitute an
/// in-memory implementation so the import, sanitizer, highlight and export pipelines can be covered
/// without touching disk.
/// </remarks>
public interface IFileSystem
{
    /// <summary>Determines whether the given file exists.</summary>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> when the file exists; otherwise <see langword="false"/>.</returns>
    bool FileExists(string path);

    /// <summary>Determines whether the given directory exists.</summary>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> when the directory exists; otherwise <see langword="false"/>.</returns>
    bool DirectoryExists(string path);

    /// <summary>Creates the given directory and any missing parents. No-ops when it already exists.</summary>
    /// <param name="path">The directory to create.</param>
    void CreateDirectory(string path);

    /// <summary>Returns the full paths of the files directly inside the given directory.</summary>
    /// <param name="path">The directory to enumerate.</param>
    /// <param name="searchPattern">A search pattern to match against file names. Defaults to all files.</param>
    /// <returns>The matching file paths, not recursing into subdirectories.</returns>
    IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*");

    /// <summary>Returns the full paths of the directories directly inside the given directory.</summary>
    /// <param name="path">The directory to enumerate.</param>
    /// <returns>The immediate subdirectory paths.</returns>
    IEnumerable<string> EnumerateDirectories(string path);

    /// <summary>Deletes the given file. No-ops when the file does not exist.</summary>
    /// <param name="path">The file to delete.</param>
    void DeleteFile(string path);

    /// <summary>Moves a file to a new path.</summary>
    /// <param name="sourcePath">The file to move.</param>
    /// <param name="destinationPath">The destination path.</param>
    /// <param name="overwrite">Whether an existing destination file may be replaced.</param>
    void MoveFile(string sourcePath, string destinationPath, bool overwrite = false);

    /// <summary>Returns the creation time of the given file in UTC.</summary>
    /// <param name="path">The file to inspect.</param>
    /// <returns>The UTC creation time.</returns>
    DateTime GetCreationTimeUtc(string path);

    /// <summary>Returns the size of the given file in bytes.</summary>
    /// <param name="path">The file to inspect.</param>
    /// <returns>The file length in bytes.</returns>
    long GetFileSizeBytes(string path);

    /// <summary>Reads the entire contents of a text file.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The file contents.</returns>
    string ReadAllText(string path);

    /// <summary>Writes text to a file, replacing any existing content.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="contents">The text to write.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes once the file has been written.</returns>
    Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default);
}
