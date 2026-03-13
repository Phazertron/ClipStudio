using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for a single audio track within a clip.
/// Exposes include/exclude and volume settings that drive real-time FFmpeg playback mixing
/// and are persisted to the database for future sessions.
/// </summary>
public sealed partial class AudioTrackViewModel : ViewModelBase
{
    private readonly Action? _onChanged;

    /// <summary>
    /// Gets the VLC audio track ID for this stream, as returned by
    /// <c>MediaPlayer.AudioTrackDescription[n].Id</c>.
    /// Used with <c>MediaPlayer.SetAudioTrack()</c> for native single-track selection.
    /// </summary>
    [ObservableProperty] private int _trackIndex;

    /// <summary>
    /// Gets the zero-based audio stream index as FFmpeg sees it (i.e. the position of this
    /// track in the <c>AudioTrackDescription</c> list after the disabled <c>Id = -1</c> sentinel
    /// is removed). Use this value, not <see cref="TrackIndex"/>, for FFmpeg <c>-map 0:a:N</c> arguments.
    /// </summary>
    public int FfmpegStreamIndex { get; }

    /// <summary>Gets or sets the user-assigned display name for this track.</summary>
    [ObservableProperty] private string _displayName;

    /// <summary>
    /// Gets or sets whether this track is included in the real-time playback mix and the export.
    /// When <see langword="false"/> the track is excluded (equivalent to mute).
    /// Defaults to <see langword="true"/>.
    /// </summary>
    [ObservableProperty] private bool _isIncluded;

    /// <summary>
    /// Gets or sets the volume multiplier for this track (0.0 – 2.0; 1.0 = 100 %).
    /// Affects both the real-time playback mix and the export output.
    /// </summary>
    [ObservableProperty] private double _volume;

    /// <summary>
    /// Initialises a new <see cref="AudioTrackViewModel"/>.
    /// </summary>
    /// <param name="trackIndex">The VLC audio track ID (used with SetAudioTrack).</param>
    /// <param name="ffmpegStreamIndex">The zero-based FFmpeg stream index (used with -map 0:a:N).</param>
    /// <param name="displayName">The initial display name.</param>
    /// <param name="isIncluded">Whether the track is included in the mix.</param>
    /// <param name="volume">Initial volume multiplier.</param>
    /// <param name="onChanged">
    /// Callback invoked whenever <see cref="IsIncluded"/> or <see cref="Volume"/> changes,
    /// so the parent view model can schedule a playback mix regeneration.
    /// </param>
    public AudioTrackViewModel(
        int trackIndex,
        int ffmpegStreamIndex,
        string displayName,
        bool isIncluded,
        double volume,
        Action? onChanged = null)
    {
        _trackIndex              = trackIndex;
        FfmpegStreamIndex        = ffmpegStreamIndex;
        _displayName             = displayName;
        _isIncluded              = isIncluded;
        _volume                  = volume;
        _onChanged               = onChanged;
        SetVolumeToUnityCommand  = new RelayCommand(() => Volume = 1.0);
    }

    /// <summary>Gets the command that resets this track's volume multiplier to 1.0 (100 %).</summary>
    public IRelayCommand SetVolumeToUnityCommand { get; }

    /// <summary>Notifies the parent when the include toggle changes.</summary>
    partial void OnIsIncludedChanged(bool value) => _onChanged?.Invoke();

    /// <summary>Notifies the parent when the volume slider changes.</summary>
    partial void OnVolumeChanged(double value) => _onChanged?.Invoke();
}
