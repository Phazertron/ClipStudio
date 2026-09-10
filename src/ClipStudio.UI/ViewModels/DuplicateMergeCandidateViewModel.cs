using System;
using System.Collections.Generic;
using System.Linq;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// One clip in a duplicate group, shown side by side with the others so the user can see what
/// they are choosing between.
/// </summary>
/// <remarks>
/// The files are identical, so nothing here describes the recording. Everything shown is
/// ClipStudio's own metadata, which is the only thing that actually differs.
/// </remarks>
public sealed partial class DuplicateMergeCandidateViewModel : ViewModelBase
{
    /// <summary>Gets the clip's database identifier.</summary>
    public int ClipId { get; }

    /// <summary>Gets the clip's file name.</summary>
    public string FileName { get; }

    /// <summary>Gets the clip's full path, which is what distinguishes the copies on disk.</summary>
    public string FilePath { get; }

    /// <summary>Gets when the clip was imported, formatted for display.</summary>
    public string ImportedAtDisplay { get; }

    /// <summary>Gets the clip's review status.</summary>
    public ClipStatus Status { get; }

    /// <summary>Gets the clip's star rating.</summary>
    public int Rating { get; }

    /// <summary>Gets the rating as stars.</summary>
    public string RatingDisplay => new string('★', Rating) + new string('☆', 5 - Rating);

    /// <summary>Gets whether the clip is marked as a favourite.</summary>
    public bool IsFavourite { get; }

    /// <summary>Gets the clip's notes, or an empty string.</summary>
    public string Notes { get; }

    /// <summary>Gets whether the clip has any notes.</summary>
    public bool HasNotes => Notes.Length > 0;

    /// <summary>Gets the clip's tags, comma separated.</summary>
    public string TagsDisplay { get; }

    /// <summary>Gets the clip's players, comma separated.</summary>
    public string PlayersDisplay { get; }

    /// <summary>Gets how many highlights the clip has, described for display.</summary>
    public string HighlightsDisplay { get; }

    /// <summary>Gets the tags on this clip, by identifier and name.</summary>
    public IReadOnlyList<(int Id, string Name)> Tags { get; }

    /// <summary>Gets the players on this clip, by identifier and name.</summary>
    public IReadOnlyList<(int Id, string Name)> Players { get; }

    /// <summary>Gets the clip's highlights.</summary>
    public IReadOnlyList<Highlight> Highlights { get; }

    /// <summary>Gets or sets whether this is the copy that survives the merge.</summary>
    [ObservableProperty]
    private bool _isSurvivor;

    /// <summary>Initialises a new <see cref="DuplicateMergeCandidateViewModel"/>.</summary>
    /// <param name="clip">The clip, loaded with its tags and highlights.</param>
    /// <param name="players">The players tagged on the clip.</param>
    public DuplicateMergeCandidateViewModel(Clip clip, IReadOnlyList<Player> players)
    {
        ClipId            = clip.Id;
        FileName          = clip.FileName;
        FilePath          = clip.FilePath;
        ImportedAtDisplay = clip.ImportedAt.ToLocalTime().ToString("dd MMM yyyy  HH:mm");
        Status            = clip.Status;
        Rating            = clip.Rating;
        IsFavourite       = clip.IsFavourite;
        Notes             = clip.Notes ?? string.Empty;
        Highlights        = clip.Highlights.ToList();

        var tags = clip.ClipTags.Where(ct => ct.Tag is not null).Select(ct => ct.Tag!).ToList();
        Tags        = tags.Select(t => (t.Id, t.Name)).ToList();
        TagsDisplay = tags.Count == 0 ? "none" : string.Join(", ", tags.Select(t => t.Name));

        Players        = players.Select(p => (p.Id, p.DisplayName)).ToList();
        PlayersDisplay = players.Count == 0 ? "none" : string.Join(", ", players.Select(p => p.DisplayName));

        HighlightsDisplay = Highlights.Count switch
        {
            0 => "none",
            1 => "1 highlight",
            _ => $"{Highlights.Count} highlights",
        };
    }
}
