using ClipStudio.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClipStudio.Data.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="SourceFolder"/>.
/// </summary>
internal sealed class SourceFolderConfiguration : IEntityTypeConfiguration<SourceFolder>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<SourceFolder> builder)
    {
        builder.ToTable("SourceFolders");

        builder.HasKey(sf => sf.Id);

        builder.Property(sf => sf.Path)
            .IsRequired()
            .HasMaxLength(1024);

        builder.HasIndex(sf => sf.Path)
            .IsUnique();

        builder.Property(sf => sf.IsActive)
            .IsRequired()
            .HasDefaultValue(true);
    }
}
