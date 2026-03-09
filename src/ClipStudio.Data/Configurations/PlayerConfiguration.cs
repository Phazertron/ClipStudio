using ClipStudio.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="Player"/>.
/// </summary>
internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.ToTable("Players");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.DisplayName)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(p => p.IconPath)
            .HasMaxLength(1024);

        builder.Property(p => p.IsMe)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.HasMany(p => p.Aliases)
            .WithOne(a => a.Player)
            .HasForeignKey(a => a.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.ClipPlayers)
            .WithOne(cp => cp.Player)
            .HasForeignKey(cp => cp.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
