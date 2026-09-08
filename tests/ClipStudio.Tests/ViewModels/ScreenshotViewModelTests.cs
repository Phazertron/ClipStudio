using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using ClipStudio.UI.ViewModels;
using Moq;
using Xunit;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="ScreenshotViewModel"/> covering capture routing, the clip lifecycle
/// and re-entry protection.
/// </summary>
public sealed class ScreenshotViewModelTests
{
    private readonly Mock<IScreenshotService> _screenshots = new();
    private readonly ScreenshotViewModel _viewModel;

    private TimeSpan _position = TimeSpan.FromSeconds(42);

    /// <summary>Sets up a view model whose capture succeeds and whose position is test-controlled.</summary>
    public ScreenshotViewModelTests()
    {
        _screenshots
            .Setup(s => s.CaptureAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Screenshot { Id = 1, ClipId = 1, FilePath = "/cache/shot.png" });

        _viewModel = new ScreenshotViewModel(_screenshots.Object, () => _position);
    }

    [Fact]
    public async Task TakeScreenshot_WithoutAClip_DoesNothing()
    {
        await _viewModel.TakeScreenshotCommand.ExecuteAsync(null);

        _screenshots.Verify(
            s => s.CaptureAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TakeScreenshot_CapturesAtTheCurrentPositionForTheOpenClip()
    {
        _viewModel.SetClip(7);
        _position = TimeSpan.FromSeconds(93.5);

        await _viewModel.TakeScreenshotCommand.ExecuteAsync(null);

        _screenshots.Verify(
            s => s.CaptureAsync(7, TimeSpan.FromSeconds(93.5), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal("/cache/shot.png", _viewModel.LastCapturePath);
    }

    [Fact]
    public async Task TakeScreenshot_ReadsThePositionAtCaptureTime()
    {
        _viewModel.SetClip(1);
        _position = TimeSpan.FromSeconds(10);
        await _viewModel.TakeScreenshotCommand.ExecuteAsync(null);

        _position = TimeSpan.FromSeconds(20);
        await _viewModel.TakeScreenshotCommand.ExecuteAsync(null);

        _screenshots.Verify(
            s => s.CaptureAsync(1, TimeSpan.FromSeconds(10), It.IsAny<CancellationToken>()), Times.Once);
        _screenshots.Verify(
            s => s.CaptureAsync(1, TimeSpan.FromSeconds(20), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetClip_ClearsTheLastCapturePath()
    {
        _viewModel.SetClip(1);
        await _viewModel.TakeScreenshotCommand.ExecuteAsync(null);
        Assert.NotNull(_viewModel.LastCapturePath);

        _viewModel.SetClip(2);

        Assert.Null(_viewModel.LastCapturePath);
    }

    [Fact]
    public async Task SetClip_Null_StopsFurtherCaptures()
    {
        _viewModel.SetClip(1);
        _viewModel.SetClip(null);

        await _viewModel.TakeScreenshotCommand.ExecuteAsync(null);

        _screenshots.Verify(
            s => s.CaptureAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TakeScreenshot_ClearsIsCapturing_EvenWhenCaptureFails()
    {
        _viewModel.SetClip(1);
        _screenshots
            .Setup(s => s.CaptureAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ffmpeg exploded"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _viewModel.TakeScreenshotCommand.ExecuteAsync(null));

        Assert.False(_viewModel.IsCapturing);
        Assert.True(_viewModel.TakeScreenshotCommand.CanExecute(null));
    }

    [Fact]
    public void TakeScreenshotCommand_IsExecutableWhenIdle()
    {
        Assert.True(_viewModel.TakeScreenshotCommand.CanExecute(null));
    }
}
