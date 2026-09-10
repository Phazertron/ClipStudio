using ClipStudio.Application.Services;
using ClipStudio.Tests.Fakes;
using FFMpegCore;

namespace ClipStudio.Tests.Application;

/// <summary>
/// Integration tests for preview strip generation, run against real FFmpeg.
/// </summary>
/// <remarks>
/// These exist because the strip's speed comes from a single input-side argument,
/// <c>-discard nokey</c>, and an argument placed on the wrong side of <c>-i</c> is silently
/// ignored rather than rejected - the strip would still be produced, just as slowly as before,
/// and nothing else would notice. The strip's contents cannot tell the two apart either, since
/// both paths fill every tile, so these assert on the mode the service logs as well as on the
/// output.
/// They no-op when FFmpeg is absent rather than failing, so a machine without it can still run
/// the suite; adding a skippable-fact package for three tests was not worth a new dependency.
/// </remarks>
public sealed class MediaServicePreviewStripTests : IDisposable
{
    private readonly string _workDir;
    private readonly CapturingLogger<MediaService> _logger = new();
    private readonly MediaService _service;

    /// <summary>Creates a scratch directory for the generated fixtures and outputs.</summary>
    public MediaServicePreviewStripTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"ClipStudioStrip_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        _service = new MediaService(_logger);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Whether FFmpeg is available, since these tests shell out to it.</summary>
    private static bool FFmpegAvailable()
    {
        try
        {
            return FFMpeg.GetVideoCodecs().Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Renders a test video with a chosen keyframe interval.
    /// </summary>
    /// <param name="name">File name for the fixture.</param>
    /// <param name="seconds">Clip length.</param>
    /// <param name="gopSize">Frames between keyframes.</param>
    /// <returns>The path to the rendered file.</returns>
    private string RenderClip(string name, int seconds, int gopSize)
    {
        var path = Path.Combine(_workDir, name);

        FFMpegArguments
            .FromFileInput($"testsrc=size=320x180:rate=30:duration={seconds}", verifyExists: false,
                options => options.ForceFormat("lavfi"))
            .OutputToFile(path, overwrite: true, options => options
                .WithVideoCodec("libx264")
                .WithCustomArgument($"-g {gopSize} -pix_fmt yuv420p"))
            .ProcessSynchronously();

        return path;
    }

    /// <summary>Counts how many of the strip's tiles differ from one another.</summary>
    /// <param name="stripPath">The generated strip.</param>
    /// <param name="tileCount">How many tiles it should contain.</param>
    /// <returns>The number of visually distinct tiles.</returns>
    private int CountDistinctTiles(string stripPath, int tileCount)
    {
        var hashes = new HashSet<string>();

        for (var i = 0; i < tileCount; i++)
        {
            var tilePath = Path.Combine(_workDir, $"tile{i}.png");

            FFMpegArguments
                .FromFileInput(stripPath)
                .OutputToFile(tilePath, overwrite: true, options => options
                    .WithCustomArgument($"-vf crop=160:90:{i * 160}:0"))
                .ProcessSynchronously();

            hashes.Add(Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(tilePath))));
        }

        return hashes.Count;
    }

    [Fact]
    public async Task DenseKeyframes_ProduceAFullStripOfDistinctTiles()
    {
        if (!FFmpegAvailable()) return;

        // 60 seconds with a keyframe every second: comfortably more keyframes than tiles, so the
        // fast keyframe-only path is taken and must still fill every tile.
        var clip = RenderClip("dense.mp4", seconds: 60, gopSize: 30);

        var strip = await _service.GeneratePreviewStripAsync(
            clip, _workDir, frameCount: 20, knownDuration: TimeSpan.FromSeconds(60));

        var analysis = await FFProbe.AnalyseAsync(strip);
        Assert.Equal(20 * 160, analysis.PrimaryVideoStream!.Width);
        Assert.Equal(90, analysis.PrimaryVideoStream.Height);
        Assert.Equal(20, CountDistinctTiles(strip, 20));

        // The saving only happens on this path, and the output alone cannot show which ran.
        Assert.Contains(_logger.Messages, m => m.Contains("keyframes only"));
    }

    [Fact]
    public async Task ExactlyEnoughKeyframes_TakeTheFastPath()
    {
        if (!FFmpegAvailable()) return;

        // The guard now stops reading the moment it has seen enough rather than counting them all,
        // so the boundary is worth pinning: 20 seconds at one keyframe a second is exactly the 20
        // the strip needs, and must not be treated as one too few.
        var clip = RenderClip("boundary.mp4", seconds: 20, gopSize: 30);

        var strip = await _service.GeneratePreviewStripAsync(
            clip, _workDir, frameCount: 20, knownDuration: TimeSpan.FromSeconds(20));

        Assert.Equal(20, CountDistinctTiles(strip, 20));
        Assert.Contains(_logger.Messages, m => m.Contains("keyframes only"));
    }

    [Fact]
    public async Task SparseKeyframes_FallBackToAFullDecodeRatherThanRepeatingTiles()
    {
        if (!FFmpegAvailable()) return;

        // 12 seconds with one keyframe every 10: only two keyframes exist, so decoding keyframes
        // alone would pad the strip with repeats. Measured before the guard existed: 2 distinct
        // tiles out of 20.
        var clip = RenderClip("sparse.mp4", seconds: 12, gopSize: 300);

        var strip = await _service.GeneratePreviewStripAsync(
            clip, _workDir, frameCount: 20, knownDuration: TimeSpan.FromSeconds(12));

        var analysis = await FFProbe.AnalyseAsync(strip);
        Assert.Equal(20 * 160, analysis.PrimaryVideoStream!.Width);

        // The whole point of the fallback: a sparse file still gets a usable strip.
        Assert.True(
            CountDistinctTiles(strip, 20) > 2,
            "Sparse clip fell back but still produced a strip of repeated tiles.");

        Assert.Contains(_logger.Messages, m => m.Contains("full decode"));
    }

    [Fact]
    public async Task AKnownDurationIsUsedWithoutReprobing()
    {
        if (!FFmpegAvailable()) return;

        // Passing a duration is what removes the second probe from an import. A wrong one changes
        // the sampling rate, which is how we can tell it was actually used.
        var clip = RenderClip("known.mp4", seconds: 30, gopSize: 30);

        var strip = await _service.GeneratePreviewStripAsync(
            clip, _workDir, frameCount: 10, knownDuration: TimeSpan.FromSeconds(30));

        var analysis = await FFProbe.AnalyseAsync(strip);
        Assert.Equal(10 * 160, analysis.PrimaryVideoStream!.Width);
    }
}
