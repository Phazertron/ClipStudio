using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Application.Services;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using ClipStudio.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClipStudio.Tests.Application;

/// <summary>
/// Unit tests for <see cref="MediaAssetProvider"/>, which took over the thumbnail and strip
/// regeneration the startup sanitize used to do silently.
/// </summary>
public sealed class MediaAssetProviderTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly Mock<IClipRepository> _clips = new();
    private readonly Mock<IHighlightRepository> _highlights = new();
    private readonly Mock<IMediaService> _media = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly AppSettings _appSettings = new();

    public MediaAssetProviderTests()
    {
        _settings.Setup(x => x.Current).Returns(_appSettings);
    }

    private MediaAssetProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _clips.Object);
        services.AddScoped(_ => _highlights.Object);
        services.AddScoped(_ => _media.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new MediaAssetProvider(
            scopeFactory, _fs, _settings.Object,
            new AppDataPaths("/appdata"),
            NullLogger<MediaAssetProvider>.Instance);
    }

    private static Clip ClipWith(string? thumbnail, string filePath = "/clips/a.mp4") => new()
    {
        Id             = 1,
        FilePath       = filePath,
        FileName       = "a.mp4",
        Duration       = TimeSpan.FromSeconds(120),
        ThumbnailPath  = thumbnail,
    };

    [Fact]
    public async Task ReturnsTheExistingThumbnailWithoutGeneratingAnything()
    {
        _fs.AddFile("/clips/a.mp4");
        _fs.AddFile("/cache/a.png");
        _clips.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
              .ReturnsAsync(ClipWith("/cache/a.png"));

        var path = await BuildProvider().EnsureClipThumbnailAsync(1);

        Assert.Equal("/cache/a.png", path);
        _media.Verify(x => x.GenerateThumbnailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GeneratesAndPersistsAMissingThumbnail()
    {
        _fs.AddFile("/clips/a.mp4");
        var clip = ClipWith(thumbnail: null);
        _clips.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(clip);
        _media.Setup(x => x.GenerateThumbnailAsync(
                  "/clips/a.mp4", It.IsAny<string>(), It.IsAny<TimeSpan>(),
                  It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("/cache/made.png");

        var path = await BuildProvider().EnsureClipThumbnailAsync(1);

        Assert.Equal("/cache/made.png", path);
        Assert.Equal("/cache/made.png", clip.ThumbnailPath);
        _clips.Verify(x => x.UpdateAsync(clip, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegeneratesAThumbnailWhoseFileHasGoneMissing()
    {
        // The row still names a path, but the cache file behind it is gone - which is exactly the
        // case the startup pass used to cover.
        _fs.AddFile("/clips/a.mp4");
        var clip = ClipWith("/cache/deleted.png");
        _clips.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(clip);
        _media.Setup(x => x.GenerateThumbnailAsync(
                  It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                  It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("/cache/remade.png");

        var path = await BuildProvider().EnsureClipThumbnailAsync(1);

        Assert.Equal("/cache/remade.png", path);
    }

    [Fact]
    public async Task ReturnsNullWhenTheSourceFileIsGone()
    {
        // Nothing to generate from. A broken clip must not spawn a doomed FFmpeg run per card.
        _clips.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
              .ReturnsAsync(ClipWith(thumbnail: null));

        var path = await BuildProvider().EnsureClipThumbnailAsync(1);

        Assert.Null(path);
        _media.Verify(x => x.GenerateThumbnailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReturnsNullWhenTheClipIsNotInTheDatabase()
    {
        _clips.Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((Clip?)null);

        Assert.Null(await BuildProvider().EnsureClipThumbnailAsync(99));
    }

    [Fact]
    public async Task ReturnsNullWhenGenerationFails()
    {
        _fs.AddFile("/clips/a.mp4");
        _clips.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
              .ReturnsAsync(ClipWith(thumbnail: null));
        _media.Setup(x => x.GenerateThumbnailAsync(
                  It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                  It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("ffmpeg died"));

        Assert.Null(await BuildProvider().EnsureClipThumbnailAsync(1));
    }

    [Fact]
    public async Task ConcurrentRequestsForOneAssetShareASingleGeneration()
    {
        // A grid scrolling past twenty cards of the same clip must not start twenty FFmpeg runs.
        _fs.AddFile("/clips/a.mp4");
        var gate = new TaskCompletionSource<string>();
        _clips.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
              .ReturnsAsync(() => ClipWith(thumbnail: null));
        _media.Setup(x => x.GenerateThumbnailAsync(
                  It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                  It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns(gate.Task);

        var provider = BuildProvider();
        var first  = provider.EnsureClipThumbnailAsync(1);
        var second = provider.EnsureClipThumbnailAsync(1);

        gate.SetResult("/cache/once.png");
        var results = await Task.WhenAll(first, second);

        Assert.All(results, r => Assert.Equal("/cache/once.png", r));
        _media.Verify(x => x.GenerateThumbnailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GeneratesAMissingPreviewStripFromTheDurationOnTheRow()
    {
        // The duration is already on the clip row, so the strip does not need its own probe.
        _fs.AddFile("/clips/a.mp4");
        _appSettings.PreviewStripFrameCount = 20;
        var clip = ClipWith(thumbnail: null);
        _clips.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(clip);
        _media.Setup(x => x.GeneratePreviewStripAsync(
                  "/clips/a.mp4", It.IsAny<string>(), 20,
                  TimeSpan.FromSeconds(120), It.IsAny<CancellationToken>()))
              .ReturnsAsync("/cache/strip.jpg");

        var path = await BuildProvider().EnsureClipPreviewStripAsync(1);

        Assert.Equal("/cache/strip.jpg", path);
        Assert.Equal("/cache/strip.jpg", clip.PreviewStripPath);
    }

    [Fact]
    public async Task GeneratesAMissingHighlightThumbnailAtTheMidpoint()
    {
        _fs.AddFile("/clips/a.mp4");
        var clip      = ClipWith(thumbnail: null);
        var highlight = new Highlight
        {
            Id        = 7,
            ClipId    = 1,
            Clip      = clip,
            StartTime = TimeSpan.FromSeconds(10),
            EndTime   = TimeSpan.FromSeconds(30),
        };
        _highlights.Setup(x => x.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(highlight);
        _media.Setup(x => x.GenerateThumbnailAsync(
                  "/clips/a.mp4", It.IsAny<string>(), TimeSpan.FromSeconds(20),
                  "hl7", It.IsAny<CancellationToken>()))
              .ReturnsAsync("/cache/hl7.png");

        var path = await BuildProvider().EnsureHighlightThumbnailAsync(7);

        Assert.Equal("/cache/hl7.png", path);
        Assert.Equal("/cache/hl7.png", highlight.ThumbnailPath);
        _highlights.Verify(x => x.UpdateAsync(highlight, It.IsAny<CancellationToken>()), Times.Once);
    }
}
