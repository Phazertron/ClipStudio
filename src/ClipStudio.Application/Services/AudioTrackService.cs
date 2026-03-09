using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;

namespace ClipStudio.Application.Services;

/// <summary>
/// Service that manages per-clip audio track settings, merging VLC's runtime track list
/// with persisted database records and saving changes on demand.
/// </summary>
internal sealed class AudioTrackService : IAudioTrackService
{
    private readonly IAudioTrackRepository _repo;

    /// <summary>Initialises a new <see cref="AudioTrackService"/>.</summary>
    /// <param name="repo">The audio track repository.</param>
    public AudioTrackService(IAudioTrackRepository repo)
    {
        _repo = repo;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AudioTrackSetting>> GetOrInitAsync(
        int clipId,
        IEnumerable<(int Index, string Name)> vlcTracks)
    {
        var existing = await _repo.GetByClipAsync(clipId);
        var existingMap = existing.ToDictionary(e => e.TrackIndex);

        var result = new List<AudioTrackSetting>();
        foreach (var (index, name) in vlcTracks)
        {
            if (existingMap.TryGetValue(index, out var row))
            {
                result.Add(row);
            }
            else
            {
                result.Add(new AudioTrackSetting
                {
                    ClipId      = clipId,
                    TrackIndex  = index,
                    DisplayName = string.IsNullOrWhiteSpace(name) ? $"Track {index}" : name,
                    IsMuted     = false,
                    Volume      = 1.0,
                });
            }
        }

        return result.AsReadOnly();
    }

    /// <inheritdoc/>
    public async Task SaveAsync(int clipId, IEnumerable<AudioTrackSetting> settings)
    {
        await _repo.UpsertAsync(clipId, settings);
    }
}
