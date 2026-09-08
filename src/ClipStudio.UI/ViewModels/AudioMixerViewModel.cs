using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Owns per-clip audio: master volume and mute, the per-track include and volume settings, and
/// the choice between native track selection and an FFmpeg remux preview.
/// </summary>
/// <remarks>
/// All player interaction goes through <see cref="IAudioPlaybackHost"/>, so this view model holds
/// no LibVLC types. Two ordering rules that LibVLC is sensitive about are preserved here and must
/// not be "tidied away":
/// <list type="bullet">
///   <item>On the first play of a clip, a native track selection is not applied: calling
///   SetAudioTrack from inside the player's own Playing callback silently breaks audio output on
///   Windows before the pipeline is ready. See <c>isInitialLoad</c> in
///   <see cref="ApplyAudioRoutingAsync"/>.</item>
///   <item>Remux previews alternate between two cache slots so FFmpeg never writes to the file the
///   player currently holds open. See <see cref="GetAudioMixCachePath"/>.</item>
/// </list>
/// </remarks>
public sealed partial class AudioMixerViewModel : ViewModelBase, IDisposable
{
    private readonly IAudioPlaybackHost _host;
    private readonly IAudioTrackService _audioTracks;
    private readonly IMixedAudioService _mixedAudio;
    private readonly ISettingsService _settings;
    private readonly IFileSystem _fileSystem;
    private readonly AppDataPaths _paths;

    private Clip? _clip;

    /// <summary>
    /// Cancellation token source for the debounced audio-routing update task.
    /// Cancelled and replaced whenever audio track settings change.
    /// </summary>
    private CancellationTokenSource? _mixDebounce;

    /// <summary>
    /// Cancellation token source for an in-progress FFmpeg remux generation.
    /// Cancelled when a new mix is requested before the previous one finishes, or when the clip
    /// is closed. Prevents concurrent writes to the same output file.
    /// </summary>
    private CancellationTokenSource? _mixApplyCts;

    /// <summary>
    /// Index (0 or 1) of the cache slot the player is currently playing.
    /// The next generation always targets the <em>other</em> slot so that FFmpeg never tries to
    /// overwrite a file the player holds open on Windows.
    /// </summary>
    private int _activeMixSlot;

    /// <summary>
    /// Tracks the current audio rendering mode so that mode transitions can be detected and the
    /// appropriate action (native track switch versus media reload) taken.
    /// </summary>
    private AudioMode _audioMode = AudioMode.NativeSingleTrack;

    /// <summary>Gets the per-track include and volume settings for the open clip.</summary>
    public ObservableCollection<AudioTrackViewModel> AudioTracks { get; } = new();

    /// <summary>
    /// Gets or sets the master output level, 0-100.
    /// Applied to the player immediately unless <see cref="IsMuted"/> is set.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeIconKind))]
    private int _masterVolume = 100;

    /// <summary>
    /// Gets or sets a value indicating whether output is muted.
    /// When set, the player's volume is held at zero regardless of <see cref="MasterVolume"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeIconKind))]
    private bool _isMuted;

    /// <summary>
    /// Gets or sets a value indicating whether an FFmpeg remux is in progress.
    /// Drives the spinner on the Apply Mix button.
    /// </summary>
    [ObservableProperty]
    private bool _isMixApplying;

    /// <summary>
    /// Gets or sets a value indicating whether track settings have changed and the user needs to
    /// press Apply Mix to hear them. Only meaningful in <see cref="AudioMode.MixedRemux"/>.
    /// </summary>
    [ObservableProperty]
    private bool _hasPendingAudioChanges;

    /// <summary>
    /// Gets or sets a value indicating whether the player is on the remux preview rather than the
    /// original clip. Surfaced in the UI as a "Playing preview" badge.
    /// </summary>
    [ObservableProperty]
    private bool _isPlayingMixPreview;

    /// <summary>Gets the speaker icon matching the current mute state and volume level.</summary>
    public MaterialIconKind VolumeIconKind =>
        IsMuted || MasterVolume == 0 ? MaterialIconKind.VolumeMute   :
        MasterVolume <= 50           ? MaterialIconKind.VolumeLow    :
        MasterVolume <= 100          ? MaterialIconKind.VolumeMedium :
                                       MaterialIconKind.VolumeHigh;

    /// <summary>Gets a value indicating whether the open clip's tracks have been discovered yet.</summary>
    public bool HasTracks => AudioTracks.Count > 0;

    /// <summary>Gets the command that toggles master mute.</summary>
    public IRelayCommand ToggleMuteCommand { get; }

    /// <summary>Gets the command that snaps the master level back to 100%.</summary>
    public IRelayCommand SetMasterVolumeToFullCommand { get; }

    /// <summary>Gets the command that generates the remux preview for the current settings.</summary>
    public IAsyncRelayCommand ApplyAudioMixCommand { get; }

    /// <summary>Gets the command that persists the per-track settings for the open clip.</summary>
    public IAsyncRelayCommand SaveAudioSettingsCommand { get; }

    /// <summary>
    /// Raised after the track list has been rebuilt, carrying the track display names so the
    /// parent can populate anything that needs them, such as the transcription track picker.
    /// </summary>
    public event Action<IReadOnlyList<string>>? TracksRefreshed;

    /// <summary>Initialises a new <see cref="AudioMixerViewModel"/>.</summary>
    /// <param name="host">The seam onto the media player.</param>
    /// <param name="audioTracks">Persistence for per-track settings.</param>
    /// <param name="mixedAudio">FFmpeg mixing and remuxing.</param>
    /// <param name="settings">Application settings, read for the preview caching preference.</param>
    /// <param name="fileSystem">File-system access for the preview cache.</param>
    /// <param name="paths">Resolved application data directories.</param>
    public AudioMixerViewModel(
        IAudioPlaybackHost host,
        IAudioTrackService audioTracks,
        IMixedAudioService mixedAudio,
        ISettingsService settings,
        IFileSystem fileSystem,
        AppDataPaths paths)
    {
        _host        = host;
        _audioTracks = audioTracks;
        _mixedAudio  = mixedAudio;
        _settings    = settings;
        _fileSystem  = fileSystem;
        _paths       = paths;

        // The _masterVolume field initialiser bypasses the generated setter, so
        // OnMasterVolumeChanged never runs during construction and the player would be left at
        // whatever LibVLC read from the system VLC config, which can be 0. Apply it explicitly.
        _host.Volume = _masterVolume;

        ToggleMuteCommand            = new RelayCommand(() => IsMuted = !IsMuted);
        SetMasterVolumeToFullCommand = new RelayCommand(() => MasterVolume = 100);
        ApplyAudioMixCommand         = new AsyncRelayCommand(ApplyAudioMixAsync);
        SaveAudioSettingsCommand     = new AsyncRelayCommand(SaveAudioSettingsAsync);
    }

    /// <summary>
    /// Points the mixer at a different clip and clears all per-clip audio state, so the new
    /// clip's tracks are rediscovered on its first play.
    /// </summary>
    /// <param name="clip">The clip now open, or <see langword="null"/> when the view closes.</param>
    public void SetClip(Clip? clip)
    {
        _mixDebounce?.Cancel();
        _mixApplyCts?.Cancel();

        _clip = clip;

        AudioTracks.Clear();
        OnPropertyChanged(nameof(HasTracks));

        _audioMode             = AudioMode.NativeSingleTrack;
        _activeMixSlot         = 0;
        HasPendingAudioChanges = false;
        IsPlayingMixPreview    = false;
        IsMixApplying          = false;
    }

    /// <summary>
    /// Called by the source generator when <see cref="MasterVolume"/> changes.
    /// Applies the new level to the player unless output is muted.
    /// </summary>
    /// <param name="value">The new master level.</param>
    partial void OnMasterVolumeChanged(int value)
    {
        if (!IsMuted)
            _host.Volume = value;
    }

    /// <summary>
    /// Called by the source generator when <see cref="IsMuted"/> changes.
    /// Drops the player to silence or restores the master level.
    /// </summary>
    /// <param name="value">The new mute state.</param>
    partial void OnIsMutedChanged(bool value) =>
        _host.Volume = value ? 0 : MasterVolume;

    /// <summary>
    /// Rebuilds the track list from what the player has discovered, merged with the settings
    /// saved for this clip, then applies the resulting routing. Called on the clip's first play,
    /// once the player has actually started and its track list is populated.
    /// </summary>
    /// <returns>A task that completes once routing has been applied.</returns>
    public async Task RefreshAudioTracksAsync()
    {
        if (_clip is null) return;

        // The position of a track in this list is its 0-based FFmpeg stream index.
        var playerTracks = _host.GetAudioTracks()
            .Select((d, i) => (Index: d.Id, FfmpegIndex: i, d.Name))
            .ToList();

        if (playerTracks.Count == 0) return;

        var trackPairs = playerTracks.Select(t => (t.Index, t.Name));
        var settings   = await _audioTracks.GetOrInitAsync(_clip.Id, trackPairs);

        AudioTracks.Clear();
        foreach (var setting in settings)
        {
            var ffmpegIndex = playerTracks.FirstOrDefault(t => t.Index == setting.TrackIndex).FfmpegIndex;
            AudioTracks.Add(new AudioTrackViewModel(
                setting.TrackIndex,
                ffmpegStreamIndex: ffmpegIndex,
                setting.DisplayName,
                isIncluded: !setting.IsMuted,
                setting.Volume,
                onChanged: ScheduleMixRegeneration));
        }

        OnPropertyChanged(nameof(HasTracks));

        // isInitialLoad: SetAudioTrack must not be called from inside the player's Playing
        // callback - the audio pipeline is not fully initialised at that boundary.
        await ApplyAudioRoutingAsync(CancellationToken.None, isInitialLoad: true);
        _host.LogAudioDiagnostics("AfterRefreshAudioTracks");

        TracksRefreshed?.Invoke(AudioTracks.Select(t => t.DisplayName).ToList());
    }

    /// <summary>Persists the current per-track include and volume settings for the open clip.</summary>
    /// <returns>A task that completes once the settings have been saved.</returns>
    private async Task SaveAudioSettingsAsync()
    {
        if (_clip is null) return;

        var settings = AudioTracks.Select(t => new AudioTrackSetting
        {
            ClipId      = _clip.Id,
            TrackIndex  = t.TrackIndex,
            DisplayName = t.DisplayName,
            IsMuted     = !t.IsIncluded,
            Volume      = t.Volume,
        });

        await _audioTracks.SaveAsync(_clip.Id, settings);
    }

    /// <summary>
    /// Debounces routing updates so rapid slider moves or toggles do not each trigger an apply.
    /// Fires 300 ms after the last change.
    /// </summary>
    private void ScheduleMixRegeneration()
    {
        _mixDebounce?.Cancel();
        _mixDebounce = new CancellationTokenSource();
        var token = _mixDebounce.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
                await Dispatcher.UIThread.InvokeAsync(() => _ = ApplyAudioRoutingAsync(token));
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    /// <summary>
    /// Works out which rendering mode the current track configuration needs and applies it:
    /// a native track selection when one track is included at unity gain (or all are), and an
    /// FFmpeg remux otherwise.
    /// </summary>
    /// <param name="token">Cancellation token from the debounce scheduler.</param>
    /// <param name="isInitialLoad">
    /// <see langword="true"/> when called from <see cref="RefreshAudioTracksAsync"/> on first
    /// play, which suppresses the native SetAudioTrack call and auto-loads a cached mix instead
    /// of waiting for the user to press Apply Mix.
    /// </param>
    /// <returns>A task that completes once the mode has been applied.</returns>
    private async Task ApplyAudioRoutingAsync(CancellationToken token, bool isInitialLoad = false)
    {
        if (_clip is null || AudioTracks.Count == 0) return;

        var tracks = AudioTracks
            .Select(t => new TrackMixInfo(t.TrackIndex, t.IsIncluded, t.Volume, t.FfmpegStreamIndex))
            .ToList();

        // No tracks included - silence the player. Applied even on initial load, deliberately.
        if (tracks.Count(t => t.IsIncluded) == 0)
        {
            _host.SetAudioTrack(-1);
            HasPendingAudioChanges = false;
            IsPlayingMixPreview    = false;
            _audioMode             = AudioMode.NativeSingleTrack;
            return;
        }

        var needsMix = _mixedAudio.ShouldUseMix(tracks);
        var prevMode = _audioMode;
        _audioMode   = needsMix ? AudioMode.MixedRemux : AudioMode.NativeSingleTrack;

        if (!needsMix)
        {
            HasPendingAudioChanges = false;

            var included = AudioTracks.Where(t => t.IsIncluded).ToList();
            var trackId  = included.Count == 1
                ? included[0].TrackIndex
                : AudioTracks[0].TrackIndex; // all-unity - the player's default first track

            if (prevMode == AudioMode.MixedRemux)
            {
                // Reload the original file to drop the remux preview, then apply the selection
                // once the reload completes.
                _host.ReloadMedia(_clip.FilePath, pendingNativeTrackId: trackId);
                IsPlayingMixPreview = false;
            }
            else if (!isInitialLoad)
            {
                _host.SetAudioTrack(trackId);
                IsPlayingMixPreview = false;
            }

            return;
        }

        if (isInitialLoad)
        {
            // First play: load the cached mix if there is one so the user hears the mix they
            // configured last time without pressing Apply Mix; otherwise build it in the
            // background.
            HasPendingAudioChanges = false;

            if (_settings.Current.CacheAudioPreviews && TryAdoptCachedMix())
                return;

            _ = ApplyAudioMixAsync();
        }
        else
        {
            // User-initiated change - let them press Apply Mix to preview before committing.
            HasPendingAudioChanges = true;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Looks for a previously generated remux preview for this clip and, if one exists, adopts
    /// its slot and reloads the player onto it.
    /// </summary>
    /// <remarks>
    /// Both slots are inspected because slot tracking does not survive closing the clip, so the
    /// most recently written file is the authoritative one.
    /// </remarks>
    /// <returns><see langword="true"/> when a cached preview was adopted.</returns>
    private bool TryAdoptCachedMix()
    {
        var path0   = GetAudioMixCachePath(0);
        var path1   = GetAudioMixCachePath(1);
        var exists0 = _fileSystem.FileExists(path0);
        var exists1 = _fileSystem.FileExists(path1);

        string? cachedPath = null;
        if (exists0 && exists1)
        {
            cachedPath = _fileSystem.GetLastWriteTimeUtc(path0) >= _fileSystem.GetLastWriteTimeUtc(path1)
                ? path0
                : path1;
            _activeMixSlot = cachedPath == path0 ? 0 : 1;
        }
        else if (exists0)
        {
            cachedPath     = path0;
            _activeMixSlot = 0;
        }
        else if (exists1)
        {
            cachedPath     = path1;
            _activeMixSlot = 1;
        }

        if (cachedPath is null)
            return false;

        _host.ReloadMedia(cachedPath);
        IsPlayingMixPreview = true;
        return true;
    }

    /// <summary>
    /// Generates the FFmpeg remux preview for the current track configuration and reloads the
    /// player onto it. Cancels any generation already in flight.
    /// </summary>
    /// <returns>A task that completes once the preview has been generated or the attempt failed.</returns>
    private async Task ApplyAudioMixAsync()
    {
        if (_clip is null || AudioTracks.Count == 0) return;

        _mixApplyCts?.Cancel();
        _mixApplyCts?.Dispose();
        _mixApplyCts = new CancellationTokenSource();
        var token = _mixApplyCts.Token;

        IsMixApplying          = true;
        HasPendingAudioChanges = false;

        var tracks = AudioTracks
            .Select(t => new TrackMixInfo(t.TrackIndex, t.IsIncluded, t.Volume, t.FfmpegStreamIndex))
            .ToList();

        // Write to the slot the player is NOT on, to avoid a Windows file-lock conflict.
        var nextSlot = 1 - _activeMixSlot;
        var mkvPath  = GetAudioMixCachePath(nextSlot);

        try
        {
            await _mixedAudio.GenerateRemuxAsync(_clip.FilePath, tracks, mkvPath, token);

            // Bail out if this request was superseded while FFmpeg was running.
            token.ThrowIfCancellationRequested();

            _activeMixSlot    = nextSlot;
            _host.ReloadMedia(mkvPath);
            IsPlayingMixPreview = true;
        }
        catch (OperationCanceledException)
        {
            // A newer request superseded this one - leave player state as it is.
        }
        catch (Exception ex)
        {
            // Restore the pending flag so the user can retry, and put the player back on a
            // known-good track; the original file is still loaded.
            HasPendingAudioChanges = true;
            if (AudioTracks.Count > 0)
                _host.SetAudioTrack(AudioTracks[0].TrackIndex);

            System.Diagnostics.Debug.WriteLine($"[ClipStudio] ApplyAudioMixAsync failed: {ex.Message}");
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsMixApplying = false;
        }
    }

    /// <summary>
    /// Returns the preview cache path for the given slot: slot 0 is
    /// <c>clip_{id}_audio_preview.mkv</c>, slot 1 appends <c>_alt</c>. Alternating between the
    /// two ensures FFmpeg never overwrites the file the player currently has open.
    /// </summary>
    /// <param name="slot">The slot index, or -1 to use the slot currently in play.</param>
    /// <returns>The absolute path to the preview file for that slot.</returns>
    private string GetAudioMixCachePath(int slot = -1)
    {
        _fileSystem.CreateDirectory(_paths.AudioCachePath);

        var slotToUse = slot < 0 ? _activeMixSlot : slot;
        var suffix    = slotToUse == 0 ? string.Empty : "_alt";

        return System.IO.Path.Combine(_paths.AudioCachePath, $"clip_{_clip!.Id}_audio_preview{suffix}.mkv");
    }

    /// <summary>
    /// Deletes both preview slots for the open clip. Called when the view closes and preview
    /// caching is turned off, which makes previews session-only temporary files.
    /// </summary>
    public void DeleteSessionPreviews()
    {
        if (_clip is null || _settings.Current.CacheAudioPreviews)
            return;

        for (var slot = 0; slot <= 1; slot++)
        {
            try { _fileSystem.DeleteFile(GetAudioMixCachePath(slot)); }
            catch { /* non-fatal */ }
        }
    }

    /// <summary>Cancels any in-flight generation. Called when the view closes.</summary>
    public void CancelPendingWork() => _mixApplyCts?.Cancel();

    /// <inheritdoc/>
    public void Dispose()
    {
        _mixDebounce?.Cancel();
        _mixDebounce?.Dispose();
        _mixDebounce = null;

        _mixApplyCts?.Cancel();
        _mixApplyCts?.Dispose();
        _mixApplyCts = null;
    }
}
