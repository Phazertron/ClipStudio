using ClipStudio.Application.Interfaces;

namespace ClipStudio.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IFileSystem"/> implementation used to drive the file-system dependent
/// services without touching disk.
/// </summary>
/// <remarks>
/// Paths are compared case-insensitively and normalised to use <c>/</c> as the separator, so tests
/// can write POSIX-style paths regardless of the host platform. Directories are implicit: a
/// directory exists once a file has been added beneath it or it has been created explicitly.
/// </remarks>
public sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, FakeFile> _files =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _directories =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A file held by the fake: its text contents and metadata.</summary>
    /// <param name="Contents">The file's text contents.</param>
    /// <param name="CreationTimeUtc">The file's UTC creation time.</param>
    /// <param name="SizeBytes">The file's reported length in bytes.</param>
    private sealed record FakeFile(string Contents, DateTime CreationTimeUtc, long SizeBytes, DateTime LastWriteTimeUtc);

    /// <summary>Gets the paths of every file currently present, in no particular order.</summary>
    public IReadOnlyCollection<string> AllFiles => _files.Keys.ToList();

    /// <summary>Gets the paths of every directory explicitly created through <see cref="CreateDirectory"/>.</summary>
    public IReadOnlyCollection<string> CreatedDirectories => _directories.ToList();

    /// <summary>Adds a file, creating its parent directory chain implicitly.</summary>
    /// <param name="path">The file path to add.</param>
    /// <param name="contents">The file's text contents.</param>
    /// <param name="creationTimeUtc">The UTC creation time to report. Defaults to 2025-01-01.</param>
    /// <param name="sizeBytes">The length to report. Defaults to the contents' length.</param>
    /// <param name="lastWriteTimeUtc">The UTC last-write time. Defaults to the creation time.</param>
    /// <returns>This instance, so calls can be chained.</returns>
    public FakeFileSystem AddFile(
        string path,
        string contents = "",
        DateTime? creationTimeUtc = null,
        long? sizeBytes = null,
        DateTime? lastWriteTimeUtc = null)
    {
        var normalised = Normalise(path);
        var stamp = creationTimeUtc ?? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _files[normalised] = new FakeFile(
            contents,
            stamp,
            sizeBytes ?? contents.Length,
            lastWriteTimeUtc ?? stamp);

        var parent = ParentOf(normalised);
        while (parent is not null)
        {
            _directories.Add(parent);
            parent = ParentOf(parent);
        }

        return this;
    }

    /// <summary>Adds an empty directory.</summary>
    /// <param name="path">The directory path to add.</param>
    /// <returns>This instance, so calls can be chained.</returns>
    public FakeFileSystem AddDirectory(string path)
    {
        CreateDirectory(path);
        return this;
    }

    /// <inheritdoc/>
    public bool FileExists(string path) => _files.ContainsKey(Normalise(path));

    /// <inheritdoc/>
    public bool DirectoryExists(string path) => _directories.Contains(Normalise(path));

    /// <inheritdoc/>
    public void CreateDirectory(string path)
    {
        var current = Normalise(path);
        while (current is not null)
        {
            _directories.Add(current);
            current = ParentOf(current);
        }
    }

    /// <inheritdoc/>
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*")
    {
        var directory = Normalise(path);

        return _files.Keys
            .Where(f => string.Equals(ParentOf(f), directory, StringComparison.OrdinalIgnoreCase))
            .Where(f => MatchesPattern(NameOf(f), searchPattern))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc/>
    public IEnumerable<string> EnumerateDirectories(string path)
    {
        var directory = Normalise(path);

        return _directories
            .Where(d => string.Equals(ParentOf(d), directory, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc/>
    public void DeleteFile(string path) => _files.Remove(Normalise(path));

    /// <inheritdoc/>
    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        var source      = Normalise(sourcePath);
        var destination = Normalise(destinationPath);

        if (!_files.TryGetValue(source, out var file))
            throw new FileNotFoundException($"Fake file not found: {sourcePath}", sourcePath);

        if (!overwrite && _files.ContainsKey(destination))
            throw new IOException($"Fake destination already exists: {destinationPath}");

        _files.Remove(source);
        _files[destination] = file;
        CreateDirectory(ParentOf(destination) ?? destination);
    }

    /// <inheritdoc/>
    public DateTime GetCreationTimeUtc(string path) => Require(path).CreationTimeUtc;

    /// <inheritdoc/>
    public DateTime GetLastWriteTimeUtc(string path) => Require(path).LastWriteTimeUtc;

    /// <inheritdoc/>
    public long GetFileSizeBytes(string path) => Require(path).SizeBytes;

    /// <inheritdoc/>
    public string ReadAllText(string path) => Require(path).Contents;

    /// <inheritdoc/>
    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        AddFile(path, contents);
        return Task.CompletedTask;
    }

    private FakeFile Require(string path)
        => _files.TryGetValue(Normalise(path), out var file)
            ? file
            : throw new FileNotFoundException($"Fake file not found: {path}", path);

    /// <summary>Normalises separators and trims any trailing separator so paths compare reliably.</summary>
    /// <param name="path">The path to normalise.</param>
    /// <returns>The normalised path.</returns>
    private static string Normalise(string path)
    {
        var normalised = path.Replace('\\', '/');
        return normalised.Length > 1 ? normalised.TrimEnd('/') : normalised;
    }

    /// <summary>Returns the parent directory of a normalised path, or null at the root.</summary>
    /// <param name="path">The normalised path.</param>
    /// <returns>The parent path, or <see langword="null"/> when there is none.</returns>
    private static string? ParentOf(string path)
    {
        if (path == "/")
            return null;

        var index = path.LastIndexOf('/');
        return index switch
        {
            < 0 => null,
            0   => "/",
            _   => path[..index]
        };
    }

    /// <summary>Returns the final segment of a normalised path.</summary>
    /// <param name="path">The normalised path.</param>
    /// <returns>The file or directory name.</returns>
    private static string NameOf(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0 ? path : path[(index + 1)..];
    }

    /// <summary>
    /// Matches a file name against the subset of wildcard syntax the services actually use:
    /// <c>*</c> alone, or a <c>*.ext</c> suffix pattern.
    /// </summary>
    /// <param name="name">The file name to test.</param>
    /// <param name="pattern">The search pattern.</param>
    /// <returns><see langword="true"/> when the name matches.</returns>
    private static bool MatchesPattern(string name, string pattern)
    {
        if (pattern == "*")
            return true;

        if (pattern.StartsWith('*'))
            return name.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);

        return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
