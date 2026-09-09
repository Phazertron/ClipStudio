using System.Buffers;
using System.Security.Cryptography;
using ClipStudio.Application.Interfaces;

namespace ClipStudio.Application.Services;

/// <summary>
/// SHA-256 hashing of clip files, in a cheap form for screening and a full form for confirming.
/// </summary>
/// <remarks>
/// <para>
/// Gaming clips run to hundreds of megabytes, and an import that hashed every byte of every file
/// would make the unreviewed queue unusable. The quick hash reads a fixed amount from each end of
/// the file and mixes in the length, so its cost is the same for a 50 MB clip and a 5 GB one.
/// </para>
/// <para>
/// Including the length matters: two recordings of the same session often share a long identical
/// header, and without the length a truncated file would hash the same as a complete one. Including
/// the tail matters for the opposite reason - re-encodes of the same source frequently differ only
/// near the end.
/// </para>
/// </remarks>
public sealed class FileHashService : IFileHashService
{
    /// <summary>
    /// How much is read from each end of the file for a quick hash. Large enough to cover a
    /// container header and index, small enough that the read cost does not scale with the clip.
    /// </summary>
    public const int DefaultChunkSizeBytes = 8 * 1024 * 1024;

    private readonly IFileSystem _fileSystem;
    private readonly int _chunkSizeBytes;

    /// <summary>Initialises a new <see cref="FileHashService"/>.</summary>
    /// <param name="fileSystem">Provides the file reads.</param>
    /// <param name="chunkSizeBytes">
    /// How much to read from each end of a file for the quick hash. Defaults to
    /// <see cref="DefaultChunkSizeBytes"/>; tests override it so small fixtures still exercise the
    /// two-chunk path.
    /// </param>
    public FileHashService(IFileSystem fileSystem, int chunkSizeBytes = DefaultChunkSizeBytes)
    {
        if (chunkSizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSizeBytes), "Chunk size must be positive.");

        _fileSystem     = fileSystem;
        _chunkSizeBytes = chunkSizeBytes;
    }

    /// <inheritdoc/>
    public async Task<string> ComputeQuickHashAsync(
        string path, CancellationToken cancellationToken = default)
    {
        await using var stream = _fileSystem.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var length = stream.Length;

        // Length first, so a truncated copy of a file cannot hash the same as the complete one.
        hash.AppendData(BitConverter.GetBytes(length));

        if (length <= _chunkSizeBytes * 2L)
        {
            // Small enough that the two chunks would overlap; hashing all of it is both cheaper
            // and exact, which also makes the quick hash conclusive for short files.
            await AppendRangeAsync(hash, stream, 0, length, cancellationToken);
        }
        else
        {
            await AppendRangeAsync(hash, stream, 0, _chunkSizeBytes, cancellationToken);
            await AppendRangeAsync(
                hash, stream, length - _chunkSizeBytes, _chunkSizeBytes, cancellationToken);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <inheritdoc/>
    public async Task<string> ComputeFullHashAsync(
        string path, CancellationToken cancellationToken = default)
    {
        await using var stream = _fileSystem.OpenRead(path);
        using var sha = SHA256.Create();

        var digest = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>Feeds a byte range of the stream into the running hash.</summary>
    /// <param name="hash">The hash to append to.</param>
    /// <param name="stream">The stream to read, which must be seekable.</param>
    /// <param name="offset">Where to start reading.</param>
    /// <param name="count">How many bytes to read.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    private static async Task AppendRangeAsync(
        IncrementalHash hash,
        Stream stream,
        long offset,
        long count,
        CancellationToken cancellationToken)
    {
        stream.Seek(offset, SeekOrigin.Begin);

        // Rented so a large chunk size does not allocate a multi-megabyte array per call.
        var bufferSize = (int)Math.Min(count, 64 * 1024);
        var buffer     = ArrayPool<byte>.Shared.Rent(bufferSize);

        try
        {
            var remaining = count;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var wanted = (int)Math.Min(remaining, buffer.Length);
                var read   = await stream.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken);

                // A shorter file than its reported length: stop rather than spin on zero reads.
                if (read == 0) break;

                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
