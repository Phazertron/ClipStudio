using System;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Owns frame capture for the clip currently open in the detail view.
/// </summary>
/// <remarks>
/// The playback position is supplied by the parent through a delegate rather than by holding a
/// reference to the LibVLC media player, so this view model has no dependency on the playback
/// stack and can be exercised without one. The parent calls <see cref="SetClip"/> whenever the
/// open clip changes, and <see cref="SetClip"/> with <see langword="null"/> when the view closes.
/// </remarks>
public sealed partial class ScreenshotViewModel : ViewModelBase
{
    private readonly IScreenshotService _screenshots;
    private readonly Func<TimeSpan> _currentPosition;

    private int? _clipId;

    /// <summary>Gets or sets a value indicating whether a capture is currently in progress.</summary>
    /// <remarks>Bound to the capture button so it can show progress and refuse re-entry.</remarks>
    [ObservableProperty]
    private bool _isCapturing;

    /// <summary>Gets or sets the path of the most recently captured screenshot, or null when none.</summary>
    [ObservableProperty]
    private string? _lastCapturePath;

    /// <summary>Gets the command that captures the frame at the current playback position.</summary>
    public IAsyncRelayCommand TakeScreenshotCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="ScreenshotViewModel"/>.
    /// </summary>
    /// <param name="screenshots">The service that captures and persists screenshots.</param>
    /// <param name="currentPosition">
    /// Returns the playback position to capture at, evaluated at the moment of capture.
    /// </param>
    public ScreenshotViewModel(IScreenshotService screenshots, Func<TimeSpan> currentPosition)
    {
        _screenshots     = screenshots;
        _currentPosition = currentPosition;

        TakeScreenshotCommand = new AsyncRelayCommand(TakeScreenshotAsync, () => !IsCapturing);
    }

    /// <summary>
    /// Points this view model at a different clip, or clears it when the detail view closes.
    /// </summary>
    /// <param name="clipId">The identifier of the open clip, or <see langword="null"/> for none.</param>
    public void SetClip(int? clipId)
    {
        _clipId         = clipId;
        LastCapturePath = null;
    }

    /// <summary>
    /// Captures the frame at the current playback position and records it against the open clip.
    /// Does nothing when no clip is open.
    /// </summary>
    /// <returns>A task that completes once the frame has been captured and persisted.</returns>
    private async Task TakeScreenshotAsync()
    {
        if (_clipId is null || IsCapturing)
            return;

        IsCapturing = true;
        TakeScreenshotCommand.NotifyCanExecuteChanged();

        try
        {
            var screenshot  = await _screenshots.CaptureAsync(_clipId.Value, _currentPosition());
            LastCapturePath = screenshot.FilePath;
        }
        finally
        {
            IsCapturing = false;
            TakeScreenshotCommand.NotifyCanExecuteChanged();
        }
    }
}
