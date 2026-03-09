using ClipStudio.Core.Entities;
using ClipStudio.Data.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ClipStudio.Data;

/// <summary>
/// The primary EF Core database context for ClipStudio.
/// Manages all persistence for the library: clips, tags, highlights, screenshots, and export jobs.
/// </summary>
public sealed class AppDbContext : DbContext
{
    /// <summary>Initializes a new instance of <see cref="AppDbContext"/> with the given options.</summary>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>Gets or sets the source folders watched by the library.</summary>
    public DbSet<SourceFolder> SourceFolders => Set<SourceFolder>();

    /// <summary>Gets or sets the video clips in the library.</summary>
    public DbSet<Clip> Clips => Set<Clip>();

    /// <summary>Gets or sets the user-defined tags.</summary>
    public DbSet<Tag> Tags => Set<Tag>();

    /// <summary>Gets or sets the soft relations between tags.</summary>
    public DbSet<TagRelation> TagRelations => Set<TagRelation>();

    /// <summary>Gets or sets the clip-level tag assignments.</summary>
    public DbSet<ClipTag> ClipTags => Set<ClipTag>();

    /// <summary>Gets or sets the highlighted time ranges within clips.</summary>
    public DbSet<Highlight> Highlights => Set<Highlight>();

    /// <summary>Gets or sets the highlight-level tag assignments.</summary>
    public DbSet<HighlightTag> HighlightTags => Set<HighlightTag>();

    /// <summary>Gets or sets the screenshots captured from clips.</summary>
    public DbSet<Screenshot> Screenshots => Set<Screenshot>();

    /// <summary>Gets or sets the video export jobs.</summary>
    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();

    /// <summary>Gets or sets the players (participants) in the library.</summary>
    public DbSet<Player> Players => Set<Player>();

    /// <summary>Gets or sets the player aliases.</summary>
    public DbSet<PlayerAlias> PlayerAliases => Set<PlayerAlias>();

    /// <summary>Gets or sets the clip-player join records.</summary>
    public DbSet<ClipPlayer> ClipPlayers => Set<ClipPlayer>();

    /// <summary>Gets or sets the per-clip audio track settings.</summary>
    public DbSet<AudioTrackSetting> AudioTrackSettings => Set<AudioTrackSetting>();

    /// <summary>Gets or sets the game-tag alias mappings (OBS string → Game Tag).</summary>
    public DbSet<GameTagAlias> GameTagAliases => Set<GameTagAlias>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SourceFolderConfiguration());
        modelBuilder.ApplyConfiguration(new ClipConfiguration());
        modelBuilder.ApplyConfiguration(new TagConfiguration());
        modelBuilder.ApplyConfiguration(new TagRelationConfiguration());
        modelBuilder.ApplyConfiguration(new ClipTagConfiguration());
        modelBuilder.ApplyConfiguration(new HighlightConfiguration());
        modelBuilder.ApplyConfiguration(new HighlightTagConfiguration());
        modelBuilder.ApplyConfiguration(new ScreenshotConfiguration());
        modelBuilder.ApplyConfiguration(new ExportJobConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerAliasConfiguration());
        modelBuilder.ApplyConfiguration(new ClipPlayerConfiguration());
        modelBuilder.ApplyConfiguration(new AudioTrackSettingConfiguration());
        modelBuilder.ApplyConfiguration(new GameTagAliasConfiguration());

        // SQLite stores DateTime as text and returns it without a Kind.
        // Apply value converters globally so that every DateTime read from the DB has
        // DateTimeKind.Utc, ensuring .ToLocalTime() in ViewModels converts correctly.
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        var utcNullableConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : null,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                // Skip properties that already have an explicit converter configured in an
                // entity type configuration (e.g. Clip.DeletedAt stored as long? ticks).
                if (property.GetValueConverter() != null)
                    continue;

                if (property.ClrType == typeof(DateTime))
                    property.SetValueConverter(utcConverter);
                else if (property.ClrType == typeof(DateTime?))
                    property.SetValueConverter(utcNullableConverter);
            }
        }
    }
}
