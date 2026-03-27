using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Services;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the transcription panel embedded in the clip detail view.
/// Manages per-clip caption track selection, transcription progress state, and the ordered list of
/// timed text segments produced by the local Whisper model.
///
/// Caption tracks are owned by this panel and are independent of the audio mix configuration used
/// for playback.  The user selects which tracks to include for captioning via checkboxes; all tracks
/// are included by default.
/// </summary>
public sealed partial class TranscriptionViewModel : ViewModelBase
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly ITranscriptionRepository _transcriptionRepository;
    private readonly ISettingsService _settingsService;
    private readonly Action<long> _seekRequested;
    private CancellationTokenSource? _transcribeCts;

    /// <summary>The most recently loaded or produced transcription; used to locate the SRT path for regeneration.</summary>
    private Transcription? _currentTranscription;

    /// <summary>Index into <see cref="Segments"/> of the segment currently highlighted as playing; -1 when none.</summary>
    private int _activeSegmentIndex = -1;

    // ---- State ----

    /// <summary>Gets or sets whether a transcription run is currently in progress.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTranscribe))]
    private bool _isTranscribing;

    /// <summary>Gets or sets the transcription progress (0 - 1).</summary>
    [ObservableProperty]
    private float _progress;

    /// <summary>Gets or sets a human-readable status message shown below the progress bar.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Gets or sets whether a transcription already exists for the current clip.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTranscribe))]
    private bool _hasExistingTranscription;

    /// <summary>Gets or sets the display date of the most recent transcription, if any.</summary>
    [ObservableProperty]
    private string _lastTranscriptionDate = string.Empty;

    // ---- Caption track selection ----

    /// <summary>
    /// Gets the caption track items for this clip.  Each item corresponds to one FFmpeg audio stream
    /// and exposes an <c>IsIncluded</c> checkbox so the user can choose which tracks feed the
    /// transcription independently of the audio mix used for playback.
    /// </summary>
    public ObservableCollection<CaptionTrackViewModel> CaptionTracks { get; } = new();

    // ---- Segments ----

    /// <summary>Gets the ordered list of timed text segments from the latest transcription.</summary>
    public ObservableCollection<TranscriptionSegmentViewModel> Segments { get; } = new();

    // ---- Commands ----

    /// <summary>Gets the command that starts a new transcription run.</summary>
    public IAsyncRelayCommand TranscribeCommand { get; }

    /// <summary>Gets the command that cancels an in-progress transcription.</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>
    /// Gets a value indicating whether the user can start a new transcription.
    /// Requires at least one caption track available and no run currently in progress.
    /// </summary>
    public bool CanTranscribe => !IsTranscribing && CaptionTracks.Count > 0;

    /// <summary>
    /// Raised when a transcription completes so that <see cref="ClipDetailViewModel"/>
    /// can update <c>HasTranscription</c> and reload the SRT path for the subtitle slave.
    /// </summary>
    public event Action<string>? TranscriptionCompleted;

    /// <summary>
    /// Raised after a segment's text has been edited and the SRT file rewritten on disk,
    /// so that <see cref="ClipDetailViewModel"/> can reload the subtitle slave in LibVLC.
    /// </summary>
    public event Action? SegmentTextEdited;

    /// <summary>Gets the clip identifier for which this panel is active.</summary>
    private int _clipId;

    /// <summary>
    /// Initialises a new <see cref="TranscriptionViewModel"/>.
    /// </summary>
    public TranscriptionViewModel(
        ITranscriptionService transcriptionService,
        ITranscriptionRepository transcriptionRepository,
        ISettingsService settingsService,
        Action<long> seekRequested)
    {
        _transcriptionService    = transcriptionService;
        _transcriptionRepository = transcriptionRepository;
        _settingsService         = settingsService;
        _seekRequested           = seekRequested;

        TranscribeCommand = new AsyncRelayCommand(TranscribeAsync, () => CanTranscribe);
        CancelCommand     = new RelayCommand(CancelTranscription, () => IsTranscribing);
    }

    /// <summary>Refreshes command enabled state when <see cref="IsTranscribing"/> changes.</summary>
    partial void OnIsTranscribingChanged(bool value)
    {
        TranscribeCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Loads the latest existing transcription for the given clip (if any) and populates segments.
    /// Also initialises the caption track list from the supplied display names.
    /// </summary>
    public async Task LoadAsync(int clipId, IReadOnlyList<string> trackDisplayNames)
    {
        _clipId = clipId;

        SetAvailableTracks(trackDisplayNames);
        _activeSegmentIndex      = -1;
        Segments.Clear();
        HasExistingTranscription = false;
        LastTranscriptionDate    = string.Empty;
        StatusMessage            = string.Empty;

        var latest = await _transcriptionRepository.GetLatestByClipIdAsync(clipId);
        if (latest is null) return;

        _currentTranscription    = latest;
        HasExistingTranscription = true;
        LastTranscriptionDate    = latest.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        PopulateSegments(latest.Segments);
    }

    /// <summary>
    /// Updates the caption track list from the audio tracks discovered by VLC after playback starts.
    /// Called by <see cref="ClipDetailViewModel"/> once <c>RefreshAudioTracksAsync</c> completes.
    /// Existing <c>IsIncluded</c> state is preserved for tracks whose display name has not changed.
    /// </summary>
    public void SetAvailableTracks(IReadOnlyList<string> trackDisplayNames)
    {
        // Snapshot current inclusion state keyed by name so it can be reapplied after refresh.
        var previous = CaptionTracks.ToDictionary(t => t.Name, t => t.IsIncluded);

        CaptionTracks.Clear();
        for (var i = 0; i < trackDisplayNames.Count; i++)
        {
            var name      = trackDisplayNames[i];
            var isIncluded = !previous.TryGetValue(name, out var was) || was;
            CaptionTracks.Add(new CaptionTrackViewModel(name, i) { IsIncluded = isIncluded });
        }

        OnPropertyChanged(nameof(CanTranscribe));
        TranscribeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Starts a transcription run using the currently selected caption tracks.
    /// Can be called by <see cref="ClipDetailViewModel"/> for the auto-on-mix-save trigger.
    /// </summary>
    public async Task TranscribeAsync()
    {
        var settings = _settingsService.Current;

        if (string.IsNullOrWhiteSpace(settings.TranscriptionModelPath)
            || !File.Exists(settings.TranscriptionModelPath))
        {
            StatusMessage = "No model configured. Open Settings > Transcription to download or browse a model.";
            return;
        }

        var trackIndices = ResolveTrackIndices();

        _transcribeCts = new CancellationTokenSource();
        IsTranscribing = true;
        Progress       = 0f;
        StatusMessage  = trackIndices.Count > 1
            ? $"Mixing {trackIndices.Count} caption tracks..."
            : "Extracting audio...";
        Segments.Clear();

        try
        {
            var progress = new Progress<float>(p =>
            {
                Progress      = p;
                StatusMessage = p < 1f ? $"Transcribing... {p * 100:F0}%" : StatusMessage;
            });

            var result = await _transcriptionService.TranscribeAsync(
                clipId:             _clipId,
                ffmpegTrackIndices: trackIndices,
                modelPath:          settings.TranscriptionModelPath,
                backend:            settings.TranscriptionBackend,
                language:           settings.TranscriptionLanguage,
                progress:           progress,
                cancellationToken:  _transcribeCts.Token);

            _currentTranscription    = result;
            PopulateSegments(result.Segments);
            HasExistingTranscription = true;
            LastTranscriptionDate    = result.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            StatusMessage            = $"Done - {result.Segments.Count} segments.";
            TranscriptionCompleted?.Invoke(result.SrtFilePath);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsTranscribing = false;
            Progress       = 0f;
            _transcribeCts?.Dispose();
            _transcribeCts = null;
        }
    }

    // ---- Segment editing ----

    /// <summary>
    /// Persists a corrected segment text to the database and rewrites the SRT file on disk.
    /// Called by each <see cref="TranscriptionSegmentViewModel"/> via its save callback.
    /// </summary>
    /// <param name="segmentId">The primary key of the segment to update.</param>
    /// <param name="newText">The corrected text.</param>
    public async Task SaveSegmentEditAsync(int segmentId, string newText)
    {
        await _transcriptionRepository.UpdateSegmentAsync(segmentId, newText);

        if (_currentTranscription is null || string.IsNullOrWhiteSpace(_currentTranscription.SrtFilePath))
            return;

        var updatedSegments = await _transcriptionRepository.GetSegmentsAsync(_currentTranscription.Id);
        await SrtWriter.WriteToFileAsync(updatedSegments, _currentTranscription.SrtFilePath);

        SegmentTextEdited?.Invoke();
    }

    /// <summary>
    /// Updates which segment row is highlighted as currently playing.
    /// Called by <see cref="ClipDetailViewModel"/> on every player time-changed tick (UI thread).
    /// Only the two affected rows (old and new) have their property changed, keeping overhead minimal.
    /// </summary>
    /// <param name="positionMs">Current playback position in milliseconds.</param>
    public void UpdatePlaybackPosition(long positionMs)
    {
        var newIndex = -1;
        for (var i = 0; i < Segments.Count; i++)
        {
            var seg = Segments[i];
            if (positionMs >= seg.StartMs && positionMs < seg.EndMs)
            {
                newIndex = i;
                break;
            }
        }

        if (newIndex == _activeSegmentIndex) return;

        if (_activeSegmentIndex >= 0 && _activeSegmentIndex < Segments.Count)
            Segments[_activeSegmentIndex].IsCurrentlyPlaying = false;

        _activeSegmentIndex = newIndex;

        if (newIndex >= 0)
            Segments[newIndex].IsCurrentlyPlaying = true;
    }

    // ---- Private helpers ----

    /// <summary>
    /// Returns the FFmpeg track indices to use for the next transcription run.
    /// Uses all <see cref="CaptionTracks"/> whose <c>IsIncluded</c> flag is set.
    /// Falls back to index 0 when no tracks are explicitly included.
    /// </summary>
    private IReadOnlyList<int> ResolveTrackIndices()
    {
        var included = CaptionTracks
            .Where(t => t.IsIncluded)
            .Select(t => t.FfmpegStreamIndex)
            .ToList();

        return included.Count > 0 ? included : new[] { 0 };
    }

    private void CancelTranscription() => _transcribeCts?.Cancel();

    private void PopulateSegments(IEnumerable<TranscriptionSegment> segments)
    {
        Segments.Clear();
        foreach (var seg in segments)
        {
            Segments.Add(new TranscriptionSegmentViewModel(
                seg.Id,
                seg.IndexNumber,
                seg.StartMs,
                seg.EndMs,
                seg.Text,
                _seekRequested,
                SaveSegmentEditAsync));
        }
    }
}
