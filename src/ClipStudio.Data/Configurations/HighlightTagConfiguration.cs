using ClipStudio.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for the <see cref="HighlightTag"/> join entity.
/// </summary>
internal sealed class HighlightTagConfiguration : IEntityTypeConfiguration<HighlightTag>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<HighlightTag> builder)
    {
        builder.ToTable("HighlightTags");

        builder.HasKey(ht => new { ht.HighlightId, ht.TagId });

        builder.HasOne(ht => ht.Highlight)
            .WithMany(h => h.HighlightTags)
            .HasForeignKey(ht => ht.HighlightId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ht => ht.Tag)
            .WithMany(t => t.HighlightTags)
            .HasForeignKey(ht => ht.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
