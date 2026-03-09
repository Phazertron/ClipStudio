using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="Tag"/>, including self-referencing hierarchy.
/// </summary>
internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(t => t.Color)
            .IsRequired()
            .HasMaxLength(16)
            .HasDefaultValue("#607D8B");

        builder.Property(t => t.Icon)
            .HasMaxLength(256);

        builder.Property(t => t.Description)
            .HasMaxLength(512);

        builder.Property(t => t.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasDefaultValue(TagType.General);

        builder.Property(t => t.GameCoverUrl)
            .HasMaxLength(512);

        builder.HasIndex(t => t.Name);
        builder.HasIndex(t => t.Type);

        // Self-referencing hierarchy: a tag may have one parent tag.
        builder.HasOne(t => t.ParentTag)
            .WithMany(t => t.ChildTags)
            .HasForeignKey(t => t.ParentTagId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
