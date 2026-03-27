using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for a single timed text segment in the transcription panel.
/// Exposes formatted timestamp strings and a seek callback so the user can click a
/// segment to jump the player to that position.
/// Supports inline text editing: the user can correct the transcribed text and persist
/// it back to the database via the <see cref="SaveEditCommand"/>.
/// </summary>
public sealed partial class TranscriptionSegmentViewModel : ViewModelBase
{
    private readonly Action<long> _seekRequested;
    private readonly Func<int, string, Task> _saveRequested;

    /// <summary>Gets the database primary key of the underlying <c>TranscriptionSegment</c>.</summary>
    public int Id { get; }

    /// <summary>Gets the 1-based sequential index of this segment.</summary>
    public int IndexNumber { get; }

    /// <summary>Gets the start position of this segment in milliseconds.</summary>
    public long StartMs { get; }

    /// <summary>Gets the end position of this segment in milliseconds.</summary>
    public long EndMs { get; }

    /// <summary>Gets or sets the transcribed text content, updated after a successful edit save.</summary>
    [ObservableProperty]
    private string _text = string.Empty;

    /// <summary>Gets or sets whether the segment row is currently in inline-edit mode.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    private bool _isEditing;

    /// <summary>Gets or sets the draft text being edited; pre-populated from <see cref="Text"/> on edit start.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    private string _editedText = string.Empty;

    /// <summary>Gets or sets whether a save operation is currently in progress.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    private bool _isSaving;

    /// <summary>
    /// Gets or sets whether this segment overlaps the current playback position.
    /// Set by <see cref="TranscriptionViewModel.UpdatePlaybackPosition"/> on every player tick.
    /// </summary>
    [ObservableProperty]
    private bool _isCurrentlyPlaying;

    /// <summary>Gets a formatted start timestamp, e.g. <c>0:12</c>.</summary>
    public string StartDisplay => FormatMs(StartMs);

    /// <summary>Gets a formatted end timestamp, e.g. <c>0:15</c>.</summary>
    public string EndDisplay => FormatMs(EndMs);

    /// <summary>Gets a combined timestamp range label, e.g. <c>0:12 – 0:15</c>.</summary>
    public string TimestampRange => $"{StartDisplay} \u2013 {EndDisplay}";

    /// <summary>
    /// Initialises a new <see cref="TranscriptionSegmentViewModel"/>.
    /// </summary>
    /// <param name="id">The database primary key of the segment.</param>
    /// <param name="indexNumber">The 1-based sequential index.</param>
    /// <param name="startMs">Segment start in milliseconds.</param>
    /// <param name="endMs">Segment end in milliseconds.</param>
    /// <param name="text">The transcribed text.</param>
    /// <param name="seekRequested">
    /// Callback invoked when the user taps this segment outside edit mode; receives the start
    /// position in milliseconds.
    /// </param>
    /// <param name="saveRequested">
    /// Async callback invoked when the user confirms an edit; receives the segment's database
    /// identifier and the corrected text.  Responsible for persisting to the DB and rewriting
    /// the SRT file.
    /// </param>
    public TranscriptionSegmentViewModel(
        int id,
        int indexNumber,
        long startMs,
        long endMs,
        string text,
        Action<long> seekRequested,
        Func<int, string, Task> saveRequested)
    {
        Id             = id;
        IndexNumber    = indexNumber;
        StartMs        = startMs;
        EndMs          = endMs;
        _text          = text;
        _seekRequested = seekRequested;
        _saveRequested = saveRequested;
    }

    /// <summary>Invokes the seek callback with this segment's start position.</summary>
    public void RequestSeek() => _seekRequested(StartMs);

    /// <summary>
    /// Enters inline-edit mode, pre-populating <see cref="EditedText"/> with the current
    /// <see cref="Text"/> so the user can make targeted corrections.
    /// </summary>
    [RelayCommand]
    private void BeginEdit()
    {
        EditedText = Text;
        IsEditing  = true;
    }

    /// <summary>
    /// Persists the edited text to the database and rewrites the SRT file,
    /// then exits edit mode and updates the displayed <see cref="Text"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveEdit))]
    private async Task SaveEditAsync()
    {
        var trimmed = EditedText.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return;

        IsSaving = true;
        try
        {
            await _saveRequested(Id, trimmed);
            Text      = trimmed;
            IsEditing = false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>Discards any pending edit and returns the row to display mode.</summary>
    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing  = false;
        EditedText = string.Empty;
    }

    /// <summary>
    /// Returns <see langword="true"/> when a save operation is currently valid:
    /// the row is in edit mode, the draft text is non-empty, and no save is already running.
    /// </summary>
    private bool CanSaveEdit() =>
        IsEditing && !string.IsNullOrWhiteSpace(EditedText) && !IsSaving;

    private static string FormatMs(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }
}
