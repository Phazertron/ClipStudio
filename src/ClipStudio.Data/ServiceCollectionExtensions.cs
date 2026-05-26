using ClipStudio.Core.Interfaces;
using ClipStudio.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClipStudio.Data;

/// <summary>
/// Extension methods for registering ClipStudio.Data services with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="AppDbContext"/> and all repository implementations with the given
    /// service collection, using SQLite at the specified database file path.
    /// </summary>
    /// <param name="services">The service collection to register services into.</param>
    /// <param name="databasePath">The absolute path to the SQLite database file.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddClipStudioData(
        this IServiceCollection services,
        string databasePath)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}")
                   .AddInterceptors(new SqliteWalInterceptor()));

        services.AddScoped<ISourceFolderRepository, SourceFolderRepository>();
        services.AddScoped<IClipRepository, ClipRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<IHighlightRepository, HighlightRepository>();
        services.AddScoped<IScreenshotRepository, ScreenshotRepository>();
        services.AddScoped<IExportJobRepository, ExportJobRepository>();
        services.AddScoped<IPlayerRepository, PlayerRepository>();
        services.AddScoped<IAudioTrackRepository, AudioTrackRepository>();
        services.AddScoped<IGameTagAliasRepository, GameTagAliasRepository>();
        services.AddScoped<IFilterPresetRepository, FilterPresetRepository>();
        services.AddScoped<ITranscriptionRepository, TranscriptionRepository>();

        return services;
    }

    /// <summary>
    /// Applies any pending EF Core migrations to the database, creating it if it does not yet exist.
    /// Should be called once at application startup before any repository operations are performed.
    /// </summary>
    /// <param name="services">The application's root service provider.</param>
    public static async Task ApplyMigrationsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.MigrateAsync();
    }
}
