using ClipStudio.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="Highlight"/>.
/// </summary>
internal sealed class HighlightConfiguration : IEntityTypeConfiguration<Highlight>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Highlight> builder)
    {
        builder.ToTable("Highlights");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Label)
            .HasMaxLength(256);

        builder.Property(h => h.Notes)
            .HasMaxLength(2048);

        // TimeSpan stored as ticks in SQLite.
        builder.Property(h => h.StartTime)
            .HasConversion(
                ts => ts.Ticks,
                ticks => TimeSpan.FromTicks(ticks));

        builder.Property(h => h.EndTime)
            .HasConversion(
                ts => ts.Ticks,
                ticks => TimeSpan.FromTicks(ticks));

        // Duration is a computed property; exclude from persistence.
        builder.Ignore(h => h.Duration);

        builder.Property(h => h.Rating)
            .HasDefaultValue(0);

        builder.Property(h => h.IsFavorite)
            .HasDefaultValue(false);

        builder.Property(h => h.ThumbnailPath)
            .HasMaxLength(1024);

        builder.HasIndex(h => h.ClipId);

        builder.HasMany(h => h.HighlightTags)
            .WithOne(ht => ht.Highlight)
            .HasForeignKey(ht => ht.HighlightId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
