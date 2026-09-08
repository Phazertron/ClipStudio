using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Application.Services;
using ClipStudio.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application;

/// <summary>
/// Extension methods for registering ClipStudio.Application services with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all application services: clip, tag, highlight, import, media, Steam game search, watcher,
    /// export, screenshot, settings, library sanitizer, and recycle bin services.
    /// </summary>
    /// <param name="services">The service collection to register services into.</param>
    /// <param name="settingsFilePath">
    /// The absolute path to the JSON settings file. The file will be created if it does not exist.
    /// </param>
    /// <param name="appDataPath">
    /// The absolute path to the application data root directory. Defaults to
    /// <c>%AppData%\ClipStudio</c> but may be overridden by a <c>--profile</c> launch argument.
    /// </param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddClipStudioApplication(
        this IServiceCollection services,
        string settingsFilePath,
        string appDataPath,
        bool isProfileScoped = false)
    {
        // Resolved paths singleton — consumed by services that write to the data directory.
        services.AddSingleton(new AppDataPaths(appDataPath, isProfileScoped));
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();

        // Settings must be registered first as other services depend on it.
        services.AddSingleton<ISettingsService>(sp =>
            new SettingsService(
                settingsFilePath,
                sp.GetRequiredService<ILogger<SettingsService>>()));

        services.AddScoped<IClipService, ClipService>();
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<IHighlightService, HighlightService>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IExportService, ExportService>();
        services.AddScoped<IScreenshotService, ScreenshotService>();
        services.AddScoped<IPlayerService, PlayerService>();
        services.AddScoped<IAudioTrackService, AudioTrackService>();
        services.AddScoped<ILibrarySanitizerService, LibrarySanitizerService>();
        services.AddScoped<IMixedAudioService, MixedAudioService>();
        services.AddScoped<IGameTagAliasService, GameTagAliasService>();
        services.AddScoped<IStatsService, StatsService>();
        services.AddScoped<ITagSuggestionService, TagSuggestionService>();
        services.AddScoped<IFilterPresetService, FilterPresetService>();
        services.AddScoped<ITranscriptionService, TranscriptionService>();
        services.AddSingleton<IRecycleBinService, RecycleBinService>();

        // Steam game search: credential-free, uses a named HttpClient.
        services.AddHttpClient<IGameSearchService, SteamSearchService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent", "ClipStudio/1.0");
        });

        // Library watcher is a long-lived singleton that holds FileSystemWatcher instances.
        services.AddSingleton<ILibraryWatcherService, LibraryWatcherService>();

        return services;
    }
}
