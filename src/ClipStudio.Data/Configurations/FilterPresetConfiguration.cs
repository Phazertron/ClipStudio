using ClipStudio.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="FilterPreset"/>.
/// </summary>
internal sealed class FilterPresetConfiguration : IEntityTypeConfiguration<FilterPreset>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<FilterPreset> builder)
    {
        builder.ToTable("FilterPresets");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(p => p.IncludedTagIds)
            .HasMaxLength(512);

        builder.Property(p => p.ExcludedTagIds)
            .HasMaxLength(512);

        builder.Property(p => p.IncludedPlayerIds)
            .HasMaxLength(512);

        builder.Property(p => p.ExcludedPlayerIds)
            .HasMaxLength(512);

        builder.Property(p => p.Status)
            .HasMaxLength(32);
    }
}
