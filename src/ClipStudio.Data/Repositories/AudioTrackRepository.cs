using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClipStudio.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAudioTrackRepository"/>.
/// </summary>
internal sealed class AudioTrackRepository : IAudioTrackRepository
{
    private readonly AppDbContext _db;

    /// <summary>Initialises a new <see cref="AudioTrackRepository"/>.</summary>
    /// <param name="db">The EF Core database context.</param>
    public AudioTrackRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AudioTrackSetting>> GetByClipAsync(int clipId)
    {
        return await _db.AudioTrackSettings
            .Where(a => a.ClipId == clipId)
            .OrderBy(a => a.TrackIndex)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(int clipId, IEnumerable<AudioTrackSetting> settings)
    {
        var incoming = settings.ToList();
        var existing = await _db.AudioTrackSettings
            .Where(a => a.ClipId == clipId)
            .ToListAsync();

        // Remove rows not present in incoming
        var incomingIndices = incoming.Select(s => s.TrackIndex).ToHashSet();
        var toRemove = existing.Where(e => !incomingIndices.Contains(e.TrackIndex)).ToList();
        _db.AudioTrackSettings.RemoveRange(toRemove);

        foreach (var s in incoming)
        {
            var existingRow = existing.FirstOrDefault(e => e.TrackIndex == s.TrackIndex);
            if (existingRow is null)
            {
                _db.AudioTrackSettings.Add(new AudioTrackSetting
                {
                    ClipId      = clipId,
                    TrackIndex  = s.TrackIndex,
                    DisplayName = s.DisplayName,
                    IsMuted     = s.IsMuted,
                    Volume      = s.Volume,
                });
            }
            else
            {
                existingRow.DisplayName = s.DisplayName;
                existingRow.IsMuted     = s.IsMuted;
                existingRow.Volume      = s.Volume;
            }
        }

        await _db.SaveChangesAsync();
    }
}
