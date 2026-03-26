using ClipStudio.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="Transcription"/>.
/// </summary>
internal sealed class TranscriptionConfiguration : IEntityTypeConfiguration<Transcription>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Transcription> builder)
    {
        builder.ToTable("Transcriptions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Language)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(t => t.ModelName)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(t => t.SrtFilePath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.HasOne(t => t.Clip)
            .WithMany(c => c.Transcriptions)
            .HasForeignKey(t => t.ClipId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.ClipId);
    }
}
