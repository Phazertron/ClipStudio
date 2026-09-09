namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Computes content hashes used to recognise a file the library has already imported.
/// </summary>
/// <remarks>
/// Two hashes, because clips are large and most comparisons are negative. The quick hash is what
/// every import pays for; the full hash only runs when the quick hash says two files might match.
/// </remarks>
public interface IFileHashService
{
    /// <summary>
    /// Hashes a file's size together with the start and end of its contents.
    /// </summary>
    /// <remarks>
    /// Cheap enough to run on every import regardless of file size. Two different files can share a
    /// quick hash, so a match means "probably the same file" and must be confirmed with
    /// <see cref="ComputeFullHashAsync"/> before anything is treated as a duplicate.
    /// </remarks>
    /// <param name="path">The file to hash.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The hash as a lowercase hex string.</returns>
    Task<string> ComputeQuickHashAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hashes a file's entire contents. Reads the whole file, so it is only worth running to
    /// confirm a quick-hash match.
    /// </summary>
    /// <param name="path">The file to hash.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The hash as a lowercase hex string.</returns>
    Task<string> ComputeFullHashAsync(string path, CancellationToken cancellationToken = default);
}
