using ClipStudio.Application.Models;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Finds clips already in the library that are the same recording.
/// </summary>
/// <remarks>
/// Duplicate detection ran only at import, so once two copies were both in the library nothing
/// ever compared them again - a clip imported while hashing was off, or before hashing existed,
/// was invisible forever. This closes that: the same two hashes the import path uses, applied to
/// the library instead of to an incoming file.
/// <para>
/// It reads whole files to confirm a match, so it belongs to Repair Library rather than to a
/// launch. The result is kept on the service so the attention list can show what the last repair
/// found without re-reading anything.
/// </para>
/// </remarks>
public interface IDuplicateClipFinder
{
    /// <summary>Gets the groups the last run found. Empty until a run has happened.</summary>
    IReadOnlyList<DuplicateClipGroup> LastGroups { get; }

    /// <summary>Gets when the last run finished, or null when none has.</summary>
    DateTime? LastRunUtc { get; }

    /// <summary>
    /// Rebuilds the known groups from the confirmed hashes already stored on the clips.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The groups still standing.</returns>
    /// <remarks>
    /// Reads no files: a scan writes each confirmed full hash onto its clip, so the groups are one
    /// indexed query away afterwards. Called at startup, which is what stops a finished scan being
    /// forgotten the moment the application closes.
    /// <para>
    /// Deriving the groups rather than storing them is deliberate. A clip that has since been
    /// trashed, deleted or left with one surviving member simply stops appearing, so a group can
    /// never outlive the situation that produced it.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<DuplicateClipGroup>> LoadKnownGroupsAsync(CancellationToken ct = default);

    /// <summary>
    /// Compares every hashed clip in the library against every other, and returns the groups whose
    /// contents are identical.
    /// </summary>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One group per set of identical clips. Clips with no hash take no part.</returns>
    /// <remarks>
    /// The stored quick hash only screens. Every candidate group is confirmed by hashing its
    /// members in full, exactly as an import does, so a quick-hash collision between two different
    /// recordings is never reported as a duplicate. A clip whose own file is missing is passed
    /// over rather than reported: there is nothing to compare, and a stale row should not invent a
    /// duplicate.
    /// </remarks>
    Task<IReadOnlyList<DuplicateClipGroup>> FindAsync(
        IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Drops a group from <see cref="LastGroups"/>, after it has been resolved.
    /// </summary>
    /// <param name="fullHash">The hash of the group to forget.</param>
    void Forget(string fullHash);
}
