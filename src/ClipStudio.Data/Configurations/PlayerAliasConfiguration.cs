using ClipStudio.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="PlayerAlias"/>.
/// </summary>
internal sealed class PlayerAliasConfiguration : IEntityTypeConfiguration<PlayerAlias>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<PlayerAlias> builder)
    {
        builder.ToTable("PlayerAliases");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Alias)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(a => new { a.PlayerId, a.Alias })
            .IsUnique();
    }
}
