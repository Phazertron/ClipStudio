using System.Collections.Generic;
using System.Threading.Tasks;
using ClipStudio.Core.Entities;

namespace ClipStudio.Core.Interfaces;

/// <summary>
/// Repository for persisting per-clip audio track settings.
/// </summary>
public interface IAudioTrackRepository
{
    /// <summary>Returns all audio track settings stored for the given clip.</summary>
    /// <param name="clipId">The clip identifier.</param>
    Task<IReadOnlyList<AudioTrackSetting>> GetByClipAsync(int clipId);

    /// <summary>
    /// Inserts or updates the supplied collection of audio track settings for the given clip.
    /// Existing tracks for the clip that are not present in <paramref name="settings"/> are removed.
    /// </summary>
    /// <param name="clipId">The clip identifier.</param>
    /// <param name="settings">The full current state of all tracks for the clip.</param>
    Task UpsertAsync(int clipId, IEnumerable<AudioTrackSetting> settings);
}
