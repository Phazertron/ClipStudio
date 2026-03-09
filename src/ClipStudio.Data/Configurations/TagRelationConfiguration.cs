using ClipStudio.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="TagRelation"/>.
/// Relations are symmetric: (A, B) and (B, A) represent the same logical relationship,
/// but both directions are stored to simplify querying.
/// </summary>
internal sealed class TagRelationConfiguration : IEntityTypeConfiguration<TagRelation>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<TagRelation> builder)
    {
        builder.ToTable("TagRelations");

        builder.HasKey(tr => new { tr.TagId, tr.RelatedTagId });

        builder.HasOne(tr => tr.Tag)
            .WithMany(t => t.Relations)
            .HasForeignKey(tr => tr.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(tr => tr.RelatedTag)
            .WithMany()
            .HasForeignKey(tr => tr.RelatedTagId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
