using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="Clip"/>.
/// </summary>
internal sealed class ClipConfiguration : IEntityTypeConfiguration<Clip>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Clip> builder)
    {
        builder.ToTable("Clips");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.FilePath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(c => c.FileName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.Resolution)
            .HasMaxLength(32);

        builder.Property(c => c.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasDefaultValue(ClipStatus.Unreviewed);

        builder.Property(c => c.Rating)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(c => c.IsFavourite)
            .IsRequired()
            .HasDefaultValue(false);

        // TimeSpan is stored as ticks (long) in SQLite.
        builder.Property(c => c.Duration)
            .HasConversion(
                ts => ts.Ticks,
                ticks => TimeSpan.FromTicks(ticks));

        builder.HasIndex(c => c.FilePath)
            .IsUnique();

        builder.HasIndex(c => c.Status);

        builder.HasIndex(c => c.IsFavourite);

        builder.Property(c => c.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(c => c.DeletedAt)
            .HasConversion(
                dt => dt.HasValue ? (long?)dt.Value.Ticks : null,
                ticks => ticks.HasValue ? (DateTime?)new DateTime(ticks.Value, DateTimeKind.Utc) : null);

        builder.HasIndex(c => c.IsDeleted);

        builder.Property(c => c.IsBroken)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(c => c.IsBroken);

        builder.HasOne(c => c.SourceFolder)
            .WithMany(sf => sf.Clips)
            .HasForeignKey(c => c.SourceFolderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Highlights)
            .WithOne(h => h.Clip)
            .HasForeignKey(h => h.ClipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Screenshots)
            .WithOne(s => s.Clip)
            .HasForeignKey(s => s.ClipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.ExportJobs)
            .WithOne(ej => ej.Clip)
            .HasForeignKey(ej => ej.ClipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(c => c.PlayCount)
            .IsRequired()
            .HasDefaultValue(0);
    }
}
