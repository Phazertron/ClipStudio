using ClipStudio.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// Entity Framework Core configuration for the <see cref="AudioTrackSetting"/> entity.
/// </summary>
internal sealed class AudioTrackSettingConfiguration : IEntityTypeConfiguration<AudioTrackSetting>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<AudioTrackSetting> builder)
    {
        builder.ToTable("AudioTrackSettings");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.DisplayName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(a => a.Volume)
            .HasDefaultValue(1.0);

        builder.HasIndex(a => new { a.ClipId, a.TrackIndex })
            .IsUnique();

        builder.HasOne(a => a.Clip)
            .WithMany(c => c.AudioTracks)
            .HasForeignKey(a => a.ClipId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
