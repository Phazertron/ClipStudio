using System;
using System.Linq;
using ClipStudio.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// One highlight offered to the survivor of a duplicate merge.
/// </summary>
/// <remarks>
/// The copies are the same recording, so every highlight from every copy is a valid range against
/// the survivor. That makes this a selection rather than a merge: the user ticks what to keep, and
/// nothing is reconciled or guessed at.
/// </remarks>
public sealed partial class MergeHighlightViewModel : ViewModelBase
{
    /// <summary>Gets the highlight's database identifier.</summary>
    public int HighlightId { get; }

    /// <summary>Gets the clip the highlight currently belongs to.</summary>
    public int OwnerClipId { get; }

    /// <summary>Gets whether the highlight already belongs to the chosen survivor.</summary>
    /// <remarks>
    /// Decides what Apply has to do: one that is already there is kept or deleted, one from a copy
    /// is recreated on the survivor.
    /// </remarks>
    public bool IsOnSurvivor { get; }

    /// <summary>Gets the highlight's label, or a placeholder when it has none.</summary>
    public string Label { get; }

    /// <summary>Gets the time range, formatted for display.</summary>
    public string RangeDisplay { get; }

    /// <summary>Gets the highlight's tags, comma separated, or an empty string.</summary>
    public string TagsDisplay { get; }

    /// <summary>Gets where the highlight came from, for display.</summary>
    public string OriginDisplay { get; }

    /// <summary>Gets or sets whether the survivor should end up with this highlight.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Gets the underlying highlight, so Apply can copy its range, rating and tags.</summary>
    public Highlight Source { get; }

    /// <summary>Initialises a new <see cref="MergeHighlightViewModel"/>.</summary>
    /// <param name="highlight">The highlight being offered.</param>
    /// <param name="isOnSurvivor">Whether it already belongs to the chosen survivor.</param>
    /// <param name="originFileName">The file name of the copy it came from.</param>
    public MergeHighlightViewModel(Highlight highlight, bool isOnSurvivor, string originFileName)
    {
        Source       = highlight;
        HighlightId  = highlight.Id;
        OwnerClipId  = highlight.ClipId;
        IsOnSurvivor = isOnSurvivor;
        Label        = string.IsNullOrWhiteSpace(highlight.Label) ? "(unlabelled)" : highlight.Label;
        RangeDisplay = $"{Format(highlight.StartTime)} - {Format(highlight.EndTime)}";

        TagsDisplay = string.Join(", ", highlight.HighlightTags
            .Where(ht => ht.Tag is not null)
            .Select(ht => ht.Tag!.Name));

        OriginDisplay = isOnSurvivor ? "on the surviving clip" : $"from {originFileName}";

        // Everything is kept unless the user says otherwise. A merge that silently dropped a
        // highlight would lose work that cannot be recovered from the file.
        _isSelected = true;
    }

    /// <summary>Formats a time span the way the highlight rows do.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>A <c>h:mm:ss</c> or <c>m:ss</c> string.</returns>
    private static string Format(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss")
        : value.ToString(@"m\:ss");
}
