using ClipStudio.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="ClipLink"/>.
/// </summary>
/// <remarks>
/// A link is stored once, in the direction it was created, and read from both ends.
/// </remarks>
internal sealed class ClipLinkConfiguration : IEntityTypeConfiguration<ClipLink>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ClipLink> builder)
    {
        builder.ToTable("ClipLinks");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Note).HasMaxLength(500);

        // One relationship of a given kind between a given pair. Creating it twice is a no-op the
        // service handles, not an error the user should see.
        builder.HasIndex(l => new { l.SourceClipId, l.TargetClipId, l.LinkType }).IsUnique();

        // Both ends are indexed because a clip's links are read from either side.
        builder.HasIndex(l => l.TargetClipId);

        builder.HasOne(l => l.SourceClip)
            .WithMany(c => c.OutgoingLinks)
            .HasForeignKey(l => l.SourceClipId)
            .OnDelete(DeleteBehavior.Cascade);

        // Cascade on both ends, so deleting either clip takes the link with it. A link to a clip
        // that no longer exists is not a relationship, it is a dangling row.
        builder.HasOne(l => l.TargetClip)
            .WithMany(c => c.IncomingLinks)
            .HasForeignKey(l => l.TargetClipId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
