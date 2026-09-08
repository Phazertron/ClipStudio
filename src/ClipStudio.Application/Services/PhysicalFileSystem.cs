using System.Text;
using ClipStudio.Application.Interfaces;

namespace ClipStudio.Application.Services;

/// <summary>
/// Default <see cref="IFileSystem"/> implementation backed by the real file system.
/// </summary>
/// <remarks>
/// Every member is a direct delegation to <see cref="File"/> or <see cref="Directory"/>, with two
/// deliberate softenings so callers do not have to guard every call:
/// <see cref="DeleteFile"/> ignores a missing file and <see cref="EnumerateFiles"/> and
/// <see cref="EnumerateDirectories"/> return an empty sequence for a missing directory.
/// </remarks>
public sealed class PhysicalFileSystem : IFileSystem
{
    /// <inheritdoc/>
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc/>
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc/>
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <inheritdoc/>
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*")
        => Directory.Exists(path) ? Directory.EnumerateFiles(path, searchPattern) : [];

    /// <inheritdoc/>
    public IEnumerable<string> EnumerateDirectories(string path)
        => Directory.Exists(path) ? Directory.EnumerateDirectories(path) : [];

    /// <inheritdoc/>
    public void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <inheritdoc/>
    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
        => File.Move(sourcePath, destinationPath, overwrite);

    /// <inheritdoc/>
    public DateTime GetCreationTimeUtc(string path) => File.GetCreationTimeUtc(path);

    /// <inheritdoc/>
    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

    /// <inheritdoc/>
    public long GetFileSizeBytes(string path) => new FileInfo(path).Length;

    /// <inheritdoc/>
    public string ReadAllText(string path) => File.ReadAllText(path);

    /// <inheritdoc/>
    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
        => File.WriteAllTextAsync(path, contents, Encoding.UTF8, cancellationToken);
}
