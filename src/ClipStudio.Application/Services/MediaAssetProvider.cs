using System.Collections.Concurrent;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Generates a clip's cached images the first time something asks for one that is not on disk.
/// </summary>
/// <remarks>
/// Registered as a singleton because it has to outlive any one scope and because its in-flight
/// table is only useful if it is shared: two cards asking for the same thumbnail at the same time
/// must wait on one FFmpeg run, not start two. Each generation opens its own scope for the
/// repositories and the media service, which are scoped.
/// </remarks>
public sealed class MediaAssetProvider : IMediaAssetProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileSystem _fileSystem;
    private readonly ISettingsService _settings;
    private readonly AppDataPaths _paths;
    private readonly ILogger<MediaAssetProvider> _logger;

    /// <summary>
    /// Generations currently running, keyed by asset. Entries are removed as soon as the task
    /// completes, so a failure is retried rather than cached forever.
    /// </summary>
    private readonly ConcurrentDictionary<string, Task<string?>> _inFlight = new();

    /// <summary>Initialises a new <see cref="MediaAssetProvider"/>.</summary>
    /// <param name="scopeFactory">Creates the scope each generation resolves its services from.</param>
    /// <param name="fileSystem">The file system abstraction, so the checks are testable.</param>
    /// <param name="settings">Supplies the thumbnail offset and strip frame count.</param>
    /// <param name="paths">Supplies the media cache directory.</param>
    /// <param name="logger">The logger.</param>
    public MediaAssetProvider(
        IServiceScopeFactory scopeFactory,
        IFileSystem fileSystem,
        ISettingsService settings,
        AppDataPaths paths,
        ILogger<MediaAssetProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _fileSystem   = fileSystem;
        _settings     = settings;
        _paths        = paths;
        _logger       = logger;
    }

    /// <inheritdoc/>
    public Task<string?> EnsureClipThumbnailAsync(int clipId, CancellationToken ct = default)
        => RunOnceAsync($"clip-thumb-{clipId}", () => GenerateClipThumbnailAsync(clipId, ct));

    /// <inheritdoc/>
    public Task<string?> EnsureClipPreviewStripAsync(int clipId, CancellationToken ct = default)
        => RunOnceAsync($"clip-strip-{clipId}", () => GenerateClipPreviewStripAsync(clipId, ct));

    /// <inheritdoc/>
    public Task<string?> EnsureHighlightThumbnailAsync(int highlightId, CancellationToken ct = default)
        => RunOnceAsync($"highlight-thumb-{highlightId}", () => GenerateHighlightThumbnailAsync(highlightId, ct));

    /// <summary>
    /// Runs <paramref name="generate"/> unless the same asset is already being generated, in which
    /// case the caller joins the run already under way.
    /// </summary>
    /// <param name="key">The asset's key.</param>
    /// <param name="generate">The generation to run.</param>
    /// <returns>The generated path, or <see langword="null"/> when it could not be produced.</returns>
    private async Task<string?> RunOnceAsync(string key, Func<Task<string?>> generate)
    {
        var task = _inFlight.GetOrAdd(key, _ => generate());
        try
        {
            return await task;
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    private async Task<string?> GenerateClipThumbnailAsync(int clipId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var clips = scope.ServiceProvider.GetRequiredService<IClipRepository>();

        var clip = await clips.GetByIdAsync(clipId, ct);
        if (clip is null) return null;

        if (!string.IsNullOrEmpty(clip.ThumbnailPath) && _fileSystem.FileExists(clip.ThumbnailPath))
            return clip.ThumbnailPath;

        if (!_fileSystem.FileExists(clip.FilePath))
            return null;

        try
        {
            var media  = scope.ServiceProvider.GetRequiredService<IMediaService>();
            var offset = TimeSpan.FromSeconds(Math.Min(
                _settings.Current.ThumbnailOffsetSeconds,
                clip.Duration.TotalSeconds * 0.1));

            var path = await media.GenerateThumbnailAsync(
                clip.FilePath, MediaCacheDirectory(), offset, cancellationToken: ct);

            clip.ThumbnailPath = path;
            await clips.UpdateAsync(clip, ct);
            _logger.LogDebug("Generated a missing thumbnail for clip {Id} on demand.", clipId);
            return path;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not generate a thumbnail for clip {Id}.", clipId);
            return null;
        }
    }

    private async Task<string?> GenerateClipPreviewStripAsync(int clipId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var clips = scope.ServiceProvider.GetRequiredService<IClipRepository>();

        var clip = await clips.GetByIdAsync(clipId, ct);
        if (clip is null) return null;

        if (!string.IsNullOrEmpty(clip.PreviewStripPath) && _fileSystem.FileExists(clip.PreviewStripPath))
            return clip.PreviewStripPath;

        if (!_fileSystem.FileExists(clip.FilePath))
            return null;

        try
        {
            var media = scope.ServiceProvider.GetRequiredService<IMediaService>();

            // The duration is already on the row, so the strip does not need its own probe.
            var path = await media.GeneratePreviewStripAsync(
                clip.FilePath, MediaCacheDirectory(),
                _settings.Current.PreviewStripFrameCount, clip.Duration, ct);

            clip.PreviewStripPath = path;
            await clips.UpdateAsync(clip, ct);
            _logger.LogDebug("Generated a missing preview strip for clip {Id} on demand.", clipId);
            return path;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not generate a preview strip for clip {Id}.", clipId);
            return null;
        }
    }

    private async Task<string?> GenerateHighlightThumbnailAsync(int highlightId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var highlights = scope.ServiceProvider.GetRequiredService<IHighlightRepository>();
        var clips      = scope.ServiceProvider.GetRequiredService<IClipRepository>();

        var highlight = await highlights.GetByIdAsync(highlightId, ct);
        if (highlight is null) return null;

        if (!string.IsNullOrEmpty(highlight.ThumbnailPath) && _fileSystem.FileExists(highlight.ThumbnailPath))
            return highlight.ThumbnailPath;

        var clip = highlight.Clip ?? await clips.GetByIdAsync(highlight.ClipId, ct);
        if (clip is null || !_fileSystem.FileExists(clip.FilePath))
            return null;

        try
        {
            var media    = scope.ServiceProvider.GetRequiredService<IMediaService>();
            var midpoint = highlight.StartTime +
                TimeSpan.FromSeconds((highlight.EndTime - highlight.StartTime).TotalSeconds / 2.0);

            var path = await media.GenerateThumbnailAsync(
                clip.FilePath, MediaCacheDirectory(), midpoint, $"hl{highlight.Id}", ct);

            highlight.ThumbnailPath = path;
            await highlights.UpdateAsync(highlight, ct);
            _logger.LogDebug("Generated a missing thumbnail for highlight {Id} on demand.", highlightId);
            return path;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not generate a thumbnail for highlight {Id}.", highlightId);
            return null;
        }
    }

    /// <summary>Returns the media cache directory, creating it when it does not yet exist.</summary>
    /// <returns>The absolute path to the media cache directory.</returns>
    private string MediaCacheDirectory()
    {
        _fileSystem.CreateDirectory(_paths.MediaCachePath);
        return _paths.MediaCachePath;
    }
}
