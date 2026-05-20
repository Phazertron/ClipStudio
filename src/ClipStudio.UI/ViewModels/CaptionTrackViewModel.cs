using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Represents a single audio track that can be included or excluded from the caption transcription mix.
/// Each instance corresponds to one FFmpeg audio stream in the source clip file and is displayed as a
/// labelled checkbox in the transcription panel so the user can choose which tracks feed the captions
/// independently of the audio mix configuration used for playback.
/// </summary>
public sealed partial class CaptionTrackViewModel : ViewModelBase
{
    /// <summary>Gets the 0-based FFmpeg audio stream index used to map this track in FFmpeg filter graphs.</summary>
    public int FfmpegStreamIndex { get; }

    /// <summary>Gets or sets the display name of the audio track (e.g. the VLC track description).</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// Gets or sets whether this track is included in the caption transcription mix.
    /// Defaults to <c>true</c> so all tracks are transcribed unless the user deselects one.
    /// </summary>
    [ObservableProperty]
    private bool _isIncluded = true;

    /// <summary>
    /// Initialises a new <see cref="CaptionTrackViewModel"/>.
    /// </summary>
    /// <param name="name">Display name of the audio track.</param>
    /// <param name="ffmpegStreamIndex">0-based FFmpeg audio stream index.</param>
    public CaptionTrackViewModel(string name, int ffmpegStreamIndex)
    {
        _name             = name;
        FfmpegStreamIndex = ffmpegStreamIndex;
    }
}
