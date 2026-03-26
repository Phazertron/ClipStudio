using ClipStudio.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="TranscriptionSegment"/>.
/// </summary>
internal sealed class TranscriptionSegmentConfiguration : IEntityTypeConfiguration<TranscriptionSegment>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<TranscriptionSegment> builder)
    {
        builder.ToTable("TranscriptionSegments");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Text)
            .IsRequired()
            .HasMaxLength(4096);

        builder.HasOne(s => s.Transcription)
            .WithMany(t => t.Segments)
            .HasForeignKey(s => s.TranscriptionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.TranscriptionId);
    }
}
