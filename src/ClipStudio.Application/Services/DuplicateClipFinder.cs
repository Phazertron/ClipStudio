using System.Collections.Concurrent;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Interfaces;
using ClipStudio.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Compares the library against itself using the two hashes the import path already uses.
/// </summary>
/// <remarks>
/// Registered as a singleton so the groups survive the scope the run happened in, which is what
/// lets the attention list show what the last repair found.
/// </remarks>
public sealed class DuplicateClipFinder : IDuplicateClipFinder
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileHashService _hashes;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<DuplicateClipFinder> _logger;

    /// <summary>
    /// The last run's groups, keyed by full hash so one can be forgotten once it is resolved.
    /// </summary>
    private readonly ConcurrentDictionary<string, DuplicateClipGroup> _groups = new();

    /// <inheritdoc/>
    public IReadOnlyList<DuplicateClipGroup> LastGroups => _groups.Values.ToList();

    /// <inheritdoc/>
    public DateTime? LastRunUtc { get; private set; }

    /// <summary>Initialises a new <see cref="DuplicateClipFinder"/>.</summary>
    /// <param name="scopeFactory">Creates the scope the run's repository is resolved from.</param>
    /// <param name="hashes">The hashing service, shared with the import path.</param>
    /// <param name="fileSystem">The file system abstraction, so the run is testable.</param>
    /// <param name="logger">The logger.</param>
    public DuplicateClipFinder(
        IServiceScopeFactory scopeFactory,
        IFileHashService hashes,
        IFileSystem fileSystem,
        ILogger<DuplicateClipFinder> logger)
    {
        _scopeFactory = scopeFactory;
        _hashes       = hashes;
        _fileSystem   = fileSystem;
        _logger       = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DuplicateClipGroup>> FindAsync(
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var clips = scope.ServiceProvider.GetRequiredService<IClipRepository>();

        var all = await clips.GetFileSnapshotsAsync(ct);

        // The stored quick hash screens. Only a bucket with more than one member is worth reading
        // files for, and on a typical library there are none.
        var candidateBuckets = all
            .Where(c => !string.IsNullOrEmpty(c.FileHash))
            .GroupBy(c => c.FileHash!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        var found = new List<DuplicateClipGroup>();

        foreach (var bucket in candidateBuckets)
        {
            ct.ThrowIfCancellationRequested();

            var members = bucket.Where(c => _fileSystem.FileExists(c.FilePath)).ToList();
            if (members.Count < 2)
            {
                // Every candidate but one has lost its file; there is nothing to compare.
                continue;
            }

            progress?.Report($"Confirming {members.Count} possible duplicates...");
            found.AddRange(await ConfirmAsync(members, ct));
        }

        _groups.Clear();
        foreach (var group in found)
            _groups[group.FullHash] = group;

        LastRunUtc = DateTime.UtcNow;

        var clipCount = found.Sum(g => g.ClipIds.Count);
        _logger.LogInformation(
            "Duplicate scan: {Groups} group(s) covering {Clips} clip(s).", found.Count, clipCount);

        return found;
    }

    /// <inheritdoc/>
    public void Forget(string fullHash) => _groups.TryRemove(fullHash, out _);

    /// <summary>
    /// Confirms one quick-hash bucket by hashing each member in full and regrouping.
    /// </summary>
    /// <param name="members">The bucket's clips, all of whose files exist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The confirmed groups within the bucket, which may be none.</returns>
    private async Task<IReadOnlyList<DuplicateClipGroup>> ConfirmAsync(
        IReadOnlyList<ClipFileSnapshot> members, CancellationToken ct)
    {
        var byFullHash = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested();

            string fullHash;
            try
            {
                fullHash = await _hashes.ComputeFullHashAsync(member.FilePath, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Unreadable now; leave it out rather than guess it matches.
                _logger.LogWarning(ex, "Could not hash clip {Id} at '{Path}'.", member.Id, member.FilePath);
                continue;
            }

            if (!byFullHash.TryGetValue(fullHash, out var ids))
                byFullHash[fullHash] = ids = [];

            ids.Add(member.Id);
        }

        var confirmed = new List<DuplicateClipGroup>();

        foreach (var (fullHash, ids) in byFullHash)
        {
            if (ids.Count < 2)
            {
                // A quick-hash collision between different recordings. Expected, and correctly
                // rejected here rather than shown to the user as a duplicate.
                continue;
            }

            confirmed.Add(new DuplicateClipGroup(fullHash, ids));
        }

        return confirmed;
    }
}
