using ClipStudio.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for the <see cref="ClipTag"/> join entity.
/// </summary>
internal sealed class ClipTagConfiguration : IEntityTypeConfiguration<ClipTag>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ClipTag> builder)
    {
        builder.ToTable("ClipTags");

        builder.HasKey(ct => new { ct.ClipId, ct.TagId });

        builder.HasOne(ct => ct.Clip)
            .WithMany(c => c.ClipTags)
            .HasForeignKey(ct => ct.ClipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ct => ct.Tag)
            .WithMany(t => t.ClipTags)
            .HasForeignKey(ct => ct.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
