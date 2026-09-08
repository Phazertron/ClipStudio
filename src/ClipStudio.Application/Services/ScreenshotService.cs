using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Manages frame capture and the persistence of screenshot records linked to clips.
/// </summary>
public sealed class ScreenshotService : IScreenshotService
{
    private readonly IClipRepository _clips;
    private readonly IScreenshotRepository _screenshots;
    private readonly IMediaService _media;
    private readonly ISettingsService _settings;
    private readonly AppDataPaths _paths;
    private readonly ILogger<ScreenshotService> _logger;

    /// <summary>Initializes a new instance of <see cref="ScreenshotService"/>.</summary>
    public ScreenshotService(
        IClipRepository clips,
        IScreenshotRepository screenshots,
        IMediaService media,
        ISettingsService settings,
        AppDataPaths paths,
        ILogger<ScreenshotService> logger)
    {
        _clips = clips;
        _screenshots = screenshots;
        _media = media;
        _settings = settings;
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Screenshot> CaptureAsync(
        int clipId,
        TimeSpan timestamp,
        CancellationToken cancellationToken = default)
    {
        var clip = await _clips.GetByIdAsync(clipId, cancellationToken)
            ?? throw new InvalidOperationException($"Clip {clipId} not found.");

        var outputDirectory = string.IsNullOrEmpty(_settings.Current.ScreenshotOutputFolder)
            ? _paths.DefaultScreenshotFolder
            : _settings.Current.ScreenshotOutputFolder;

        var filePath = await _media.CaptureScreenshotAsync(
            clip.FilePath, outputDirectory, timestamp, cancellationToken);

        var screenshot = new Screenshot
        {
            ClipId = clipId,
            FilePath = filePath,
            CapturedAt = DateTime.UtcNow,
            PlaybackTimestamp = timestamp
        };

        await _screenshots.AddAsync(screenshot, cancellationToken);
        _logger.LogInformation("Screenshot saved: {Path}", filePath);
        return screenshot;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Screenshot>> GetByClipAsync(int clipId, CancellationToken cancellationToken = default)
        => _screenshots.GetByClipAsync(clipId, cancellationToken);

    /// <inheritdoc/>
    public async Task DeleteAsync(int screenshotId, CancellationToken cancellationToken = default)
    {
        var screenshot = await _screenshots.GetByIdAsync(screenshotId, cancellationToken);
        if (screenshot is null)
            return;

        if (File.Exists(screenshot.FilePath))
            File.Delete(screenshot.FilePath);

        await _screenshots.DeleteAsync(screenshotId, cancellationToken);
        _logger.LogInformation("Screenshot deleted: {Path}", screenshot.FilePath);
    }
}
