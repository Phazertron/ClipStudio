using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClipStudio.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model representing a single highlight within a clip.
/// Provides display-ready properties and commands for jumping to, deleting, tagging,
/// and inline label/time editing of a highlight.
/// </summary>
public sealed partial class HighlightViewModel : ViewModelBase
{
    private readonly Func<HighlightViewModel, int, Task> _onAddTag;
    private readonly Func<HighlightViewModel, int, Task> _onRemoveTag;
    private readonly Func<HighlightViewModel, Task> _onExport;
    private readonly Func<HighlightViewModel, string, TimeSpan, TimeSpan, Task> _onUpdate;
    private readonly Func<HighlightViewModel, int, Task>? _onSetRating;
    private readonly Func<HighlightViewModel, Task>? _onToggleFavorite;
    private readonly Action<HighlightViewModel, bool>? _onEditingChanged;
    private readonly Func<TimeSpan>? _getPlayerPosition;

    /// <summary>Gets the database identifier of the highlight.</summary>
    public int HighlightId { get; }

    /// <summary>Gets or sets the star rating (0–5) for this highlight.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRated1OrMore))]
    [NotifyPropertyChangedFor(nameof(IsRated2OrMore))]
    [NotifyPropertyChangedFor(nameof(IsRated3OrMore))]
    [NotifyPropertyChangedFor(nameof(IsRated4OrMore))]
    [NotifyPropertyChangedFor(nameof(IsRated5OrMore))]
    private int _rating;

    /// <summary>Gets whether the rating is at least 1 star.</summary>
    public bool IsRated1OrMore => Rating >= 1;

    /// <summary>Gets whether the rating is at least 2 stars.</summary>
    public bool IsRated2OrMore => Rating >= 2;

    /// <summary>Gets whether the rating is at least 3 stars.</summary>
    public bool IsRated3OrMore => Rating >= 3;

    /// <summary>Gets whether the rating is at least 4 stars.</summary>
    public bool IsRated4OrMore => Rating >= 4;

    /// <summary>Gets whether the rating is at least 5 stars.</summary>
    public bool IsRated5OrMore => Rating >= 5;

    /// <summary>Gets or sets whether this highlight is marked as a favourite.</summary>
    [ObservableProperty] private bool _isFavorite;

    /// <summary>Gets or sets the human-readable label displayed for this highlight.</summary>
    [ObservableProperty] private string _label = string.Empty;

    /// <summary>Gets the highlight start position within the clip.</summary>
    public TimeSpan StartTime { get; private set; }

    /// <summary>Gets the highlight end position within the clip.</summary>
    public TimeSpan EndTime { get; private set; }

    /// <summary>Gets the duration of this highlight.</summary>
    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>
    /// Gets a formatted time-range string with sub-second precision, e.g. <c>1:23.4 - 1:45.7</c>.
    /// </summary>
    public string TimeRangeDisplay => $"{FormatTimePrecise(StartTime)} - {FormatTimePrecise(EndTime)}";

    /// <summary>
    /// Gets the proportional start offset of this highlight within the clip (0.0-1.0).
    /// Used to position the highlight band on the timeline.
    /// </summary>
    public double StartFraction { get; }

    /// <summary>
    /// Gets the proportional duration of this highlight relative to the clip (0.0-1.0).
    /// Used to size the highlight band on the timeline.
    /// </summary>
    public double DurationFraction { get; }

    /// <summary>
    /// Gets or sets whether the playback is currently locked to this highlight's time range.
    /// Set by the parent <see cref="ClipDetailViewModel"/> when <c>LockedHighlight</c> changes.
    /// </summary>
    [ObservableProperty]
    private bool _isLocked;

    /// <summary>Gets or sets whether the inline label/time edit form is currently visible.</summary>
    [ObservableProperty]
    private bool _isEditingLabel;

    /// <summary>Gets or sets the working copy of the label being edited.</summary>
    [ObservableProperty]
    private string _editLabel = string.Empty;

    /// <summary>Gets or sets the working copy of the start time being edited (e.g. "1:23").</summary>
    [ObservableProperty]
    private string _editStartDisplay = string.Empty;

    /// <summary>Gets or sets the working copy of the end time being edited (e.g. "1:45").</summary>
    [ObservableProperty]
    private string _editEndDisplay = string.Empty;

    /// <summary>Gets or sets an error message for the inline edit form, or null if no error.</summary>
    [ObservableProperty]
    private string? _editError;

    /// <summary>Gets the tag chips displayed for this highlight's assigned tags.</summary>
    public ObservableCollection<TagChipViewModel> Tags { get; } = new();

    /// <summary>
    /// Gets the available general tags that can be applied to this highlight.
    /// Populated from the parent view model and shared across all highlights.
    /// </summary>
    public IReadOnlyList<Tag> AvailableTags { get; }

    /// <summary>Gets the command that seeks the player to the start of this highlight.</summary>
    public IRelayCommand JumpCommand { get; }

    /// <summary>Gets the command that queues a quick export job for this highlight's time range.</summary>
    public IAsyncRelayCommand ExportCommand { get; }

    /// <summary>Gets the command that deletes this highlight from the database.</summary>
    public IAsyncRelayCommand DeleteCommand { get; }

    /// <summary>Gets the command that opens the inline label-edit form.</summary>
    public IRelayCommand BeginEditLabelCommand { get; }

    /// <summary>Gets the command that cancels label/time editing without saving.</summary>
    public IRelayCommand CancelEditLabelCommand { get; }

    /// <summary>Gets the command that saves the edited label and times to the database.</summary>
    public IAsyncRelayCommand ConfirmEditLabelCommand { get; }

    /// <summary>Gets the command that sets the rating to the given value (0–5).</summary>
    public IRelayCommand<string> SetRatingCommand { get; }

    /// <summary>Gets the command that toggles the favourite flag on this highlight.</summary>
    public IAsyncRelayCommand ToggleFavoriteCommand { get; }

    /// <summary>Gets the command that sets <see cref="EditStartDisplay"/> to the current player position.</summary>
    public IRelayCommand MarkEditStartCommand { get; }

    /// <summary>Gets the command that sets <see cref="EditEndDisplay"/> to the current player position.</summary>
    public IRelayCommand MarkEditEndCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="HighlightViewModel"/>.
    /// </summary>
    /// <param name="highlight">The domain entity to project.</param>
    /// <param name="clipDuration">Total clip duration, used to compute fractional positions.</param>
    /// <param name="onJump">Callback invoked when the user requests to jump to this highlight.</param>
    /// <param name="onDelete">Async callback invoked when the user requests to delete this highlight.</param>
    /// <param name="availableTags">The pool of general tags available to apply.</param>
    /// <param name="onAddTag">Async callback invoked when a tag is added; receives this VM and the tag ID.</param>
    /// <param name="onRemoveTag">Async callback invoked when a tag is removed; receives this VM and the tag ID.</param>
    /// <param name="onExport">Async callback invoked when the user requests a quick export of this highlight.</param>
    /// <param name="onUpdate">
    /// Async callback invoked when the user saves an edit; receives this VM, new label, new start time, and new end time.
    /// </param>
    public HighlightViewModel(
        Highlight highlight,
        TimeSpan clipDuration,
        Action<HighlightViewModel> onJump,
        Func<HighlightViewModel, Task> onDelete,
        IReadOnlyList<Tag> availableTags,
        Func<HighlightViewModel, int, Task> onAddTag,
        Func<HighlightViewModel, int, Task> onRemoveTag,
        Func<HighlightViewModel, Task> onExport,
        Func<HighlightViewModel, string, TimeSpan, TimeSpan, Task> onUpdate,
        Func<HighlightViewModel, int, Task>? onSetRating = null,
        Func<HighlightViewModel, Task>? onToggleFavorite = null,
        Action<HighlightViewModel, bool>? onEditingChanged = null,
        Func<TimeSpan>? getPlayerPosition = null)
    {
        _onAddTag          = onAddTag;
        _onRemoveTag       = onRemoveTag;
        _onExport          = onExport;
        _onUpdate          = onUpdate;
        _onSetRating       = onSetRating;
        _onToggleFavorite  = onToggleFavorite;
        _onEditingChanged  = onEditingChanged;
        _getPlayerPosition = getPlayerPosition;

        HighlightId   = highlight.Id;
        _label        = string.IsNullOrWhiteSpace(highlight.Label) ? "(unlabelled)" : highlight.Label;
        StartTime     = highlight.StartTime;
        EndTime       = highlight.EndTime;
        AvailableTags = availableTags;
        _rating       = highlight.Rating;
        _isFavorite   = highlight.IsFavorite;

        var totalSeconds = clipDuration.TotalSeconds;
        StartFraction    = totalSeconds > 0 ? StartTime.TotalSeconds / totalSeconds : 0;
        DurationFraction = totalSeconds > 0 ? Duration.TotalSeconds  / totalSeconds : 0;

        foreach (var ht in highlight.HighlightTags.Where(ht => ht.Tag is not null))
        {
            var tagId   = ht.TagId;
            var tagName = ht.Tag!.Name;
            Tags.Add(new TagChipViewModel(tagId, tagName, async chip => await RemoveTagAsync(chip)));
        }

        JumpCommand             = new RelayCommand(() => onJump(this));
        ExportCommand           = new AsyncRelayCommand(() => _onExport(this));
        DeleteCommand           = new AsyncRelayCommand(() => onDelete(this));
        BeginEditLabelCommand   = new RelayCommand(BeginEditLabel);
        CancelEditLabelCommand  = new RelayCommand(() => IsEditingLabel = false);
        ConfirmEditLabelCommand = new AsyncRelayCommand(ConfirmEditLabelAsync);
        SetRatingCommand        = new RelayCommand<string>(param =>
        {
            if (int.TryParse(param, out var r))
            {
                Rating = (Rating == r) ? 0 : r;   // clicking active rating clears it
                if (_onSetRating is not null)
                    _ = _onSetRating(this, Rating);
            }
        });
        ToggleFavoriteCommand   = new AsyncRelayCommand(async () =>
        {
            IsFavorite = !IsFavorite;
            if (_onToggleFavorite is not null)
                await _onToggleFavorite(this);
        });
        MarkEditStartCommand = new RelayCommand(() =>
        {
            if (_getPlayerPosition is not null)
                EditStartDisplay = FormatTimePrecise(_getPlayerPosition());
        });
        MarkEditEndCommand = new RelayCommand(() =>
        {
            if (_getPlayerPosition is not null)
                EditEndDisplay = FormatTimePrecise(_getPlayerPosition());
        });
    }

    /// <summary>
    /// Called by the source generator when <see cref="IsEditingLabel"/> changes.
    /// Notifies the parent view model so it can show or hide the timeline edit handles.
    /// </summary>
    partial void OnIsEditingLabelChanged(bool value) => _onEditingChanged?.Invoke(this, value);

    private void BeginEditLabel()
    {
        EditLabel        = Label == "(unlabelled)" ? string.Empty : Label;
        EditStartDisplay = FormatTimePrecise(StartTime);
        EditEndDisplay   = FormatTimePrecise(EndTime);
        EditError        = null;
        IsEditingLabel   = true;
    }

    private async Task ConfirmEditLabelAsync()
    {
        var newLabel = EditLabel.Trim();

        if (!TryParseTime(EditStartDisplay, out var newStart))
        {
            EditError = "Invalid start time (use m:ss or h:mm:ss).";
            return;
        }

        if (!TryParseTime(EditEndDisplay, out var newEnd))
        {
            EditError = "Invalid end time (use m:ss or h:mm:ss).";
            return;
        }

        if (newEnd <= newStart)
        {
            EditError = "End time must be after start time.";
            return;
        }

        await _onUpdate(this, newLabel, newStart, newEnd);

        Label     = string.IsNullOrWhiteSpace(newLabel) ? "(unlabelled)" : newLabel;
        StartTime = newStart;
        EndTime   = newEnd;
        OnPropertyChanged(nameof(TimeRangeDisplay));

        EditError      = null;
        IsEditingLabel = false;
    }

    /// <summary>
    /// Directly applies <paramref name="tag"/> to this highlight.
    /// Called by the parent view's AutoCompleteBox state machine after the user confirms a selection.
    /// </summary>
    public async Task AddTagDirectlyAsync(Tag tag) => await _onAddTag(this, tag.Id);

    private async Task RemoveTagAsync(TagChipViewModel chip)
    {
        await _onRemoveTag(this, chip.TagId);
        Tags.Remove(chip);
    }

    /// <summary>
    /// Attempts to parse a user-entered time string in <c>m:ss</c>, <c>m:ss.f</c>,
    /// or <c>h:mm:ss</c> / <c>h:mm:ss.f</c> format.
    /// </summary>
    private static bool TryParseTime(string? input, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(input)) return false;

        if (TimeSpan.TryParseExact(input.Trim(),
            [
                @"m\:ss", @"mm\:ss", @"h\:mm\:ss",
                @"m\:ss\.f", @"mm\:ss\.f", @"h\:mm\:ss\.f",
                @"m\:ss\.ff", @"mm\:ss\.ff", @"h\:mm\:ss\.ff",
                @"m\:ss\.fff", @"mm\:ss\.fff", @"h\:mm\:ss\.fff",
            ], null, out result))
            return true;

        return TimeSpan.TryParse(input.Trim(), out result);
    }

    private static string FormatTime(TimeSpan ts) =>
        ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");

    private static string FormatTimePrecise(TimeSpan ts) =>
        ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss\.f") : ts.ToString(@"m\:ss\.f");
}
