using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the transcription panel embedded in the clip detail view.
/// Manages the single-track picker, the transcription progress state, and the ordered list
/// of timed text segments produced by the local Whisper model.
/// </summary>
public sealed partial class TranscriptionViewModel : ViewModelBase
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly ITranscriptionRepository _transcriptionRepository;
    private readonly ISettingsService _settingsService;
    private readonly Action<long> _seekRequested;
    private CancellationTokenSource? _transcribeCts;

    // ---- State ----

    /// <summary>Gets or sets whether a transcription run is currently in progress.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTranscribe))]
    private bool _isTranscribing;

    /// <summary>Gets or sets the transcription progress (0 – 1).</summary>
    [ObservableProperty] private float _progress;

    /// <summary>Gets or sets a human-readable status message shown below the progress bar.</summary>
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>Gets or sets whether a transcription already exists for the current clip.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTranscribe))]
    private bool _hasExistingTranscription;

    /// <summary>Gets or sets the display date of the most recent transcription, if any.</summary>
    [ObservableProperty] private string _lastTranscriptionDate = string.Empty;

    // ---- Track selection ----

    /// <summary>Gets the display names of the audio tracks available in the current clip.</summary>
    public ObservableCollection<string> AvailableTracks { get; } = new();

    /// <summary>Gets or sets the 0-based index of the audio track selected for transcription.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTranscribe))]
    private int _selectedTrackIndex;

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
    /// Requires at least one track available and no run currently in progress.
    /// </summary>
    public bool CanTranscribe => !IsTranscribing && AvailableTracks.Count > 0;

    /// <summary>
    /// Raised when a transcription completes so that <see cref="ClipDetailViewModel"/>
    /// can update <c>HasTranscription</c> and reload the SRT path for the subtitle slave.
    /// </summary>
    public event Action<string>? TranscriptionCompleted;

    /// <summary>Gets or sets the clip identifier for which this panel is active.</summary>
    private int _clipId;

    /// <summary>
    /// Initialises a new <see cref="TranscriptionViewModel"/>.
    /// </summary>
    /// <param name="transcriptionService">Service that runs the Whisper pipeline.</param>
    /// <param name="transcriptionRepository">Repository used to load existing transcriptions.</param>
    /// <param name="settingsService">Application settings, used to read model path and backend.</param>
    /// <param name="seekRequested">
    /// Callback invoked when the user clicks a segment; receives the start position in milliseconds.
    /// </param>
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
    /// Also sets up the track list from the supplied display names.
    /// </summary>
    /// <param name="clipId">The clip whose transcription history to load.</param>
    /// <param name="trackDisplayNames">Display names of the audio tracks available for this clip.</param>
    public async Task LoadAsync(int clipId, System.Collections.Generic.IReadOnlyList<string> trackDisplayNames)
    {
        _clipId = clipId;

        AvailableTracks.Clear();
        foreach (var name in trackDisplayNames)
            AvailableTracks.Add(name);

        SelectedTrackIndex = 0;
        Segments.Clear();
        HasExistingTranscription = false;
        LastTranscriptionDate    = string.Empty;
        StatusMessage            = string.Empty;

        var latest = await _transcriptionRepository.GetLatestByClipIdAsync(clipId);
        if (latest is null) return;

        HasExistingTranscription = true;
        LastTranscriptionDate    = latest.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        PopulateSegments(latest.Segments);
    }

    /// <summary>
    /// Updates the available-tracks list from the audio tracks discovered by VLC after playback starts.
    /// Called by <see cref="ClipDetailViewModel"/> once <c>RefreshAudioTracksAsync</c> completes.
    /// </summary>
    /// <param name="trackDisplayNames">Ordered display names of the available audio tracks.</param>
    public void SetAvailableTracks(System.Collections.Generic.IReadOnlyList<string> trackDisplayNames)
    {
        AvailableTracks.Clear();
        foreach (var name in trackDisplayNames)
            AvailableTracks.Add(name);

        if (SelectedTrackIndex >= AvailableTracks.Count)
            SelectedTrackIndex = 0;

        OnPropertyChanged(nameof(CanTranscribe));
        TranscribeCommand.NotifyCanExecuteChanged();
    }

    // ---- Private helpers ----

    private async Task TranscribeAsync()
    {
        var settings = _settingsService.Current;

        if (string.IsNullOrWhiteSpace(settings.TranscriptionModelPath)
            || !File.Exists(settings.TranscriptionModelPath))
        {
            StatusMessage = "No model configured. Open Settings > Transcription to download or browse a model.";
            return;
        }

        _transcribeCts = new CancellationTokenSource();
        IsTranscribing = true;
        Progress       = 0f;
        StatusMessage  = "Extracting audio...";
        Segments.Clear();

        try
        {
            var progress = new Progress<float>(p =>
            {
                Progress = p;
                if (p < 1f)
                    StatusMessage = $"Transcribing... {p * 100:F0}%";
            });

            var result = await _transcriptionService.TranscribeAsync(
                clipId:           _clipId,
                ffmpegTrackIndex: SelectedTrackIndex,
                modelPath:        settings.TranscriptionModelPath,
                backend:          settings.TranscriptionBackend,
                language:         settings.TranscriptionLanguage,
                progress:         progress,
                cancellationToken: _transcribeCts.Token);

            PopulateSegments(result.Segments);
            HasExistingTranscription = true;
            LastTranscriptionDate    = result.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            StatusMessage            = $"Done — {result.Segments.Count} segments.";
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

    private void CancelTranscription()
    {
        _transcribeCts?.Cancel();
    }

    private void PopulateSegments(System.Collections.Generic.IEnumerable<ClipStudio.Core.Entities.TranscriptionSegment> segments)
    {
        Segments.Clear();
        foreach (var seg in segments)
        {
            Segments.Add(new TranscriptionSegmentViewModel(
                seg.IndexNumber, seg.StartMs, seg.EndMs, seg.Text, _seekRequested));
        }
    }
}
