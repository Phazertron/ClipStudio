using ClipStudio.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for the <see cref="ClipPlayer"/> join entity.
/// </summary>
internal sealed class ClipPlayerConfiguration : IEntityTypeConfiguration<ClipPlayer>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ClipPlayer> builder)
    {
        builder.ToTable("ClipPlayers");

        builder.HasKey(cp => new { cp.ClipId, cp.PlayerId });

        builder.HasOne(cp => cp.Clip)
            .WithMany(c => c.ClipPlayers)
            .HasForeignKey(cp => cp.ClipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cp => cp.Player)
            .WithMany(p => p.ClipPlayers)
            .HasForeignKey(cp => cp.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
