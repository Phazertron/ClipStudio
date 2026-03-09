using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="ExportJob"/>.
/// </summary>
internal sealed class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ExportJob> builder)
    {
        builder.ToTable("ExportJobs");

        builder.HasKey(ej => ej.Id);

        builder.Property(ej => ej.OutputPath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(ej => ej.TrimMode)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(ej => ej.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasDefaultValue(ExportJobStatus.Pending);

        builder.Property(ej => ej.ErrorMessage)
            .HasMaxLength(2048);

        builder.Property(ej => ej.DeleteOriginalAfterExport)
            .IsRequired()
            .HasDefaultValue(false);

        // TimeSpan? stored as nullable ticks (long?) in SQLite.
        builder.Property(ej => ej.StartTime)
            .HasConversion(
                ts => ts.HasValue ? (long?)ts.Value.Ticks : null,
                ticks => ticks.HasValue ? (TimeSpan?)TimeSpan.FromTicks(ticks.Value) : null);

        builder.Property(ej => ej.EndTime)
            .HasConversion(
                ts => ts.HasValue ? (long?)ts.Value.Ticks : null,
                ticks => ticks.HasValue ? (TimeSpan?)TimeSpan.FromTicks(ticks.Value) : null);

        builder.HasIndex(ej => ej.ClipId);
        builder.HasIndex(ej => ej.Status);

        builder.HasOne(ej => ej.Highlight)
            .WithMany()
            .HasForeignKey(ej => ej.HighlightId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
    }
}
