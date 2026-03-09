using ClipStudio.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="Screenshot"/>.
/// </summary>
internal sealed class ScreenshotConfiguration : IEntityTypeConfiguration<Screenshot>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Screenshot> builder)
    {
        builder.ToTable("Screenshots");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.FilePath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(s => s.Notes)
            .HasMaxLength(2048);

        // TimeSpan stored as ticks in SQLite.
        builder.Property(s => s.PlaybackTimestamp)
            .HasConversion(
                ts => ts.Ticks,
                ticks => TimeSpan.FromTicks(ticks));

        builder.HasIndex(s => s.ClipId);
    }
}
