using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using ClipStudio.UI.Parsing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Owns the highlight range being written: the add form, the inline edit of an existing highlight,
/// and the timeline handles they share.
/// </summary>
/// <remarks>
/// Only one of the two is ever active. The add form holds its own range and label; an inline edit
/// writes through to the row's own edit fields. The timeline handles, the mark commands and the
/// drag setters all route to whichever is active, which is what
/// <see cref="EditingHighlight"/> decides. The highlight list itself stays with the parent.
/// </remarks>
public sealed partial class HighlightEditorViewModel : ViewModelBase
{
    private readonly IHighlightEditorHost _host;
    private readonly IHighlightService _highlights;

    private TimeSpan _addStart = TimeSpan.Zero;
    private TimeSpan _addEnd = TimeSpan.Zero;

    private TimeSpan _editStart;
    private TimeSpan _editEnd;

    /// <summary>Gets or sets a value indicating whether the add-highlight form is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HighlightStartFraction))]
    [NotifyPropertyChangedFor(nameof(HighlightEndFraction))]
    private bool _isAddingHighlight;

    /// <summary>Gets or sets the label typed into the add form.</summary>
    [ObservableProperty]
    private string _newHighlightLabel = string.Empty;

    /// <summary>Gets or sets the start readout of the add form, which the user may type into.</summary>
    [ObservableProperty]
    private string _highlightStartDisplay = "0:00";

    /// <summary>Gets or sets the end readout of the add form, which the user may type into.</summary>
    [ObservableProperty]
    private string _highlightEndDisplay = "0:00";

    /// <summary>Gets or sets the add form's validation message, or null when it is valid.</summary>
    [ObservableProperty]
    private string? _highlightAddError;

    /// <summary>Gets the tags queued up to apply when the new highlight is saved.</summary>
    public ObservableCollection<TagChipViewModel> PendingHighlightTags { get; } = new();

    /// <summary>Gets the highlight being edited inline, or null when none is.</summary>
    public HighlightViewModel? EditingHighlight { get; private set; }

    /// <summary>Gets a value indicating whether a highlight range is being written at all.</summary>
    public bool IsAddingOrEditingHighlight => IsAddingHighlight || EditingHighlight is not null;

    /// <summary>Gets where the start handle sits along the timeline, as a fraction from 0 to 1.</summary>
    public double HighlightStartFraction => Fraction(
        EditingHighlight is not null ? _editStart : _addStart);

    /// <summary>Gets where the end handle sits along the timeline, as a fraction from 0 to 1.</summary>
    public double HighlightEndFraction => Fraction(
        EditingHighlight is not null ? _editEnd : _addEnd);

    /// <summary>Gets the command that opens the add-highlight form.</summary>
    public IRelayCommand BeginAddHighlightCommand { get; }

    /// <summary>Gets the command that closes the add form and clears it.</summary>
    public IRelayCommand CancelAddHighlightCommand { get; }

    /// <summary>Gets the command that marks the range start at the current play head.</summary>
    public IRelayCommand MarkHighlightStartCommand { get; }

    /// <summary>Gets the command that marks the range end at the current play head.</summary>
    public IRelayCommand MarkHighlightEndCommand { get; }

    /// <summary>Gets the command that saves the new highlight.</summary>
    public IAsyncRelayCommand SaveHighlightCommand { get; }

    /// <summary>Initialises a new <see cref="HighlightEditorViewModel"/>.</summary>
    /// <param name="host">The surrounding clip detail context.</param>
    /// <param name="highlights">The highlight service used to create and tag highlights.</param>
    public HighlightEditorViewModel(IHighlightEditorHost host, IHighlightService highlights)
    {
        _host       = host;
        _highlights = highlights;

        BeginAddHighlightCommand  = new RelayCommand(BeginAddHighlight);
        CancelAddHighlightCommand = new RelayCommand(ResetForm);
        MarkHighlightStartCommand = new RelayCommand(MarkHighlightStart);
        MarkHighlightEndCommand   = new RelayCommand(MarkHighlightEnd);
        SaveHighlightCommand      = new AsyncRelayCommand(SaveHighlightAsync);
    }

    // ---- The add form ----

    /// <summary>Opens the add-highlight form.</summary>
    private void BeginAddHighlight()
    {
        // Never show the highlight handles and the trim handles at the same time.
        _host.PrepareForHighlightEdit();
        IsAddingHighlight = true;
    }

    /// <summary>Closes the add form and clears everything it held.</summary>
    public void ResetForm()
    {
        IsAddingHighlight     = false;
        NewHighlightLabel     = string.Empty;
        _addStart             = TimeSpan.Zero;
        _addEnd               = TimeSpan.Zero;
        HighlightStartDisplay = "0:00";
        HighlightEndDisplay   = "0:00";
        HighlightAddError     = null;
        PendingHighlightTags.Clear();
        NotifyHandlesMoved();
    }

    /// <summary>Called by the source generator when the add form opens or closes.</summary>
    /// <param name="value">Whether the form is now showing.</param>
    partial void OnIsAddingHighlightChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAddingOrEditingHighlight));
        _host.OnEditingStateChanged();
    }

    /// <summary>
    /// Called by the source generator when the start box changes, so a typed value moves the handle.
    /// </summary>
    /// <param name="value">The new text.</param>
    partial void OnHighlightStartDisplayChanged(string value)
    {
        if (!IsAddingHighlight) return;
        if (TimestampInput.TryParse(value, out var parsed))
        {
            _addStart = parsed;
            OnPropertyChanged(nameof(HighlightStartFraction));
        }
    }

    /// <summary>
    /// Called by the source generator when the end box changes, so a typed value moves the handle.
    /// </summary>
    /// <param name="value">The new text.</param>
    partial void OnHighlightEndDisplayChanged(string value)
    {
        if (!IsAddingHighlight) return;
        if (TimestampInput.TryParse(value, out var parsed))
        {
            _addEnd = parsed;
            OnPropertyChanged(nameof(HighlightEndFraction));
        }
    }

    /// <summary>Queues a tag to apply when the new highlight is saved. Ignores duplicates.</summary>
    /// <param name="tag">The tag to queue.</param>
    public void AddPendingHighlightTag(Tag tag)
    {
        if (PendingHighlightTags.Any(c => c.TagId == tag.Id)) return;

        PendingHighlightTags.Add(new TagChipViewModel(
            tag.Id, tag.Name,
            chip => { PendingHighlightTags.Remove(chip); return Task.CompletedTask; }));
    }

    /// <summary>Creates the highlight from the add form, then applies the queued tags.</summary>
    /// <returns>A task that completes once the highlight exists and the list has been refreshed.</returns>
    private async Task SaveHighlightAsync()
    {
        var clip = _host.CurrentClip;
        if (clip is null) return;

        if (_addStart >= _addEnd)
        {
            HighlightAddError = "End time must be after start time.";
            return;
        }

        HighlightAddError = null;

        var created = await _highlights.CreateAsync(
            clip.Id,
            _addStart,
            _addEnd,
            string.IsNullOrWhiteSpace(NewHighlightLabel) ? null : NewHighlightLabel.Trim());

        foreach (var chip in PendingHighlightTags.ToList())
            await _highlights.AddTagAsync(created.Id, chip.TagId);

        ResetForm();
        await _host.OnHighlightCreatedAsync();
    }

    // ---- Inline edit of an existing highlight ----

    /// <summary>
    /// Tracks a highlight row entering or leaving inline edit, so the handles and mark commands
    /// route to it rather than to the add form.
    /// </summary>
    /// <param name="highlight">The row whose edit state changed.</param>
    /// <param name="isEditing">Whether that row is now being edited.</param>
    public void OnHighlightEditingChanged(HighlightViewModel highlight, bool isEditing)
    {
        if (isEditing)
        {
            EditingHighlight = highlight;
            _editStart       = highlight.StartTime;
            _editEnd         = highlight.EndTime;
            highlight.PropertyChanged += OnEditingHighlightPropertyChanged;
        }
        else if (EditingHighlight == highlight)
        {
            highlight.PropertyChanged -= OnEditingHighlightPropertyChanged;
            EditingHighlight = null;
        }

        OnPropertyChanged(nameof(IsAddingOrEditingHighlight));
        NotifyHandlesMoved();
        _host.OnEditingStateChanged();
    }

    /// <summary>
    /// Stops tracking whichever highlight is being edited. Called before the list is rebuilt, since
    /// the row view models are about to be replaced.
    /// </summary>
    /// <returns><see langword="true"/> when a highlight was being edited.</returns>
    public bool StopEditing()
    {
        if (EditingHighlight is null) return false;

        EditingHighlight.PropertyChanged -= OnEditingHighlightPropertyChanged;
        EditingHighlight = null;

        OnPropertyChanged(nameof(IsAddingOrEditingHighlight));
        NotifyHandlesMoved();
        return true;
    }

    /// <summary>Keeps the handles following the edit boxes as the user types in a row.</summary>
    /// <param name="sender">The highlight being edited.</param>
    /// <param name="e">The property that changed.</param>
    private void OnEditingHighlightPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HighlightViewModel.EditStartDisplay))
        {
            if (TimestampInput.TryParse(EditingHighlight!.EditStartDisplay, out var parsed))
                _editStart = parsed;
            OnPropertyChanged(nameof(HighlightStartFraction));
        }
        else if (e.PropertyName == nameof(HighlightViewModel.EditEndDisplay))
        {
            if (TimestampInput.TryParse(EditingHighlight!.EditEndDisplay, out var parsed))
                _editEnd = parsed;
            OnPropertyChanged(nameof(HighlightEndFraction));
        }
    }

    // ---- Moving the range, whichever form is active ----

    /// <summary>Marks the range start at the current play head.</summary>
    private void MarkHighlightStart()
        // The play head, not the player's own clock, which lags after a seek: this way the handle
        // lands exactly on the slider thumb.
        => ApplyStart(TimeSpan.FromSeconds(_host.CurrentPositionSeconds));

    /// <summary>Marks the range end at the current play head.</summary>
    private void MarkHighlightEnd()
        => ApplyEnd(TimeSpan.FromSeconds(_host.CurrentPositionSeconds));

    /// <summary>Moves the range start to a fraction along the timeline, used while dragging.</summary>
    /// <param name="fraction">The position along the timeline, from 0 to 1.</param>
    public void SetHighlightStartFromFraction(double fraction)
        => ApplyStart(FromFraction(fraction));

    /// <summary>Moves the range end to a fraction along the timeline, used while dragging.</summary>
    /// <param name="fraction">The position along the timeline, from 0 to 1.</param>
    public void SetHighlightEndFromFraction(double fraction)
        => ApplyEnd(FromFraction(fraction));

    /// <summary>Writes a new range start to whichever form is active.</summary>
    /// <param name="value">The new start.</param>
    private void ApplyStart(TimeSpan value)
    {
        if (EditingHighlight is not null)
        {
            EditingHighlight.EditStartDisplay = PlaybackViewModel.FormatPrecise(value);
            _editStart = value;
        }
        else
        {
            _addStart             = value;
            HighlightStartDisplay = PlaybackViewModel.FormatPrecise(value);
        }

        OnPropertyChanged(nameof(HighlightStartFraction));
    }

    /// <summary>Writes a new range end to whichever form is active.</summary>
    /// <param name="value">The new end.</param>
    private void ApplyEnd(TimeSpan value)
    {
        if (EditingHighlight is not null)
        {
            EditingHighlight.EditEndDisplay = PlaybackViewModel.FormatPrecise(value);
            _editEnd = value;
        }
        else
        {
            _addEnd             = value;
            HighlightEndDisplay = PlaybackViewModel.FormatPrecise(value);
        }

        OnPropertyChanged(nameof(HighlightEndFraction));
    }

    /// <summary>Converts a timeline fraction to a position within the clip.</summary>
    /// <param name="fraction">The position along the timeline, from 0 to 1.</param>
    /// <returns>The clamped position.</returns>
    private TimeSpan FromFraction(double fraction) => TimeSpan.FromSeconds(
        Math.Clamp(fraction * _host.DurationSeconds, 0, _host.DurationSeconds));

    /// <summary>Converts a position within the clip to a timeline fraction.</summary>
    /// <param name="value">The position to convert.</param>
    /// <returns>The fraction, or 0 when the duration is not known yet.</returns>
    private double Fraction(TimeSpan value) =>
        _host.DurationSeconds > 0 ? value.TotalSeconds / _host.DurationSeconds : 0;

    private void NotifyHandlesMoved()
    {
        OnPropertyChanged(nameof(HighlightStartFraction));
        OnPropertyChanged(nameof(HighlightEndFraction));
    }
}
