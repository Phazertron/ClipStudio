using ClipStudio.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="GameTagAlias"/>.
/// </summary>
internal sealed class GameTagAliasConfiguration : IEntityTypeConfiguration<GameTagAlias>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<GameTagAlias> builder)
    {
        builder.ToTable("GameTagAliases");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.AliasString)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(a => a.AliasString)
            .IsUnique();

        builder.HasOne(a => a.Tag)
            .WithMany()
            .HasForeignKey(a => a.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
