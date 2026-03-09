using System.Collections.Generic;
using System.Threading.Tasks;
using ClipStudio.Core.Entities;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Service that manages per-clip audio track settings.
/// Merges VLC's runtime track list with persisted DB settings and persists changes on demand.
/// </summary>
public interface IAudioTrackService
{
    /// <summary>
    /// Returns the persisted audio track settings for a clip, creating default entries for any
    /// VLC tracks that are not yet in the database.
    /// </summary>
    /// <param name="clipId">The clip identifier.</param>
    /// <param name="vlcTracks">
    /// The track descriptors reported by VLC at playback time: each is a (zero-based index, display name) pair.
    /// </param>
    /// <returns>The merged list of track settings, one per VLC track.</returns>
    Task<IReadOnlyList<AudioTrackSetting>> GetOrInitAsync(
        int clipId,
        IEnumerable<(int Index, string Name)> vlcTracks);

    /// <summary>Persists the supplied audio track settings to the database.</summary>
    /// <param name="clipId">The clip identifier.</param>
    /// <param name="settings">The current state of all tracks for the clip.</param>
    Task SaveAsync(int clipId, IEnumerable<AudioTrackSetting> settings);
}
