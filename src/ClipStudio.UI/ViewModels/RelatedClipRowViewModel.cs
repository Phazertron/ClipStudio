using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// One related clip, as the panel on the clip detail view shows it.
/// </summary>
public sealed partial class RelatedClipRowViewModel : ViewModelBase
{
    private readonly IMediaAssetProvider? _assets;

    /// <summary>Gets the link's identifier, so the row can remove it.</summary>
    public int LinkId { get; }

    /// <summary>Gets the identifier of the clip at the other end.</summary>
    public int ClipId { get; }

    /// <summary>Gets that clip's file name.</summary>
    public string FileName { get; }

    /// <summary>Gets the game tag on that clip, or an empty string.</summary>
    public string GameName { get; }

    /// <summary>Gets how the relationship reads from the open clip's side.</summary>
    /// <remarks>
    /// Resolved by the service, because it depends on which end you are looking from: the clip you
    /// marked as a Sequel shows its origin as a Prequel.
    /// </remarks>
    public string RelationshipLabel { get; }

    /// <summary>Gets the kind of relationship, for the badge colour.</summary>
    public ClipLinkType LinkType { get; }

    /// <summary>Gets the note the user attached to the link, if any.</summary>
    public string? Note { get; }

    /// <summary>Gets whether the link carries a note.</summary>
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    /// <summary>Gets that clip's duration, formatted for display.</summary>
    public string DurationDisplay { get; }

    /// <summary>Gets or sets the thumbnail, once decoded.</summary>
    [ObservableProperty]
    private Bitmap? _thumbnail;

    /// <summary>Gets the path to that clip's thumbnail, if it has one.</summary>
    private string? ThumbnailPath { get; set; }

    /// <summary>Initialises a new <see cref="RelatedClipRowViewModel"/>.</summary>
    /// <param name="related">The related clip, as the service described it.</param>
    /// <param name="assets">Produces a missing thumbnail on demand. Null in tests.</param>
    public RelatedClipRowViewModel(RelatedClip related, IMediaAssetProvider? assets = null)
    {
        _assets           = assets;
        LinkId            = related.LinkId;
        ClipId            = related.Clip.Id;
        FileName          = related.Clip.FileName;
        RelationshipLabel = related.RelationshipLabel;
        LinkType          = related.LinkType;
        Note              = related.Note;
        ThumbnailPath     = related.Clip.ThumbnailPath;

        var game = related.Clip.ClipTags
            .FirstOrDefault(ct => ct.Tag?.Type == TagType.Game)?.Tag?.Name;
        GameName = game ?? string.Empty;

        DurationDisplay = related.Clip.Duration.TotalHours >= 1
            ? related.Clip.Duration.ToString(@"h\:mm\:ss")
            : related.Clip.Duration.ToString(@"m\:ss");
    }

    /// <summary>
    /// Decodes the thumbnail on a background thread, generating it when the file is missing.
    /// </summary>
    public async Task LoadThumbnailAsync()
    {
        if (string.IsNullOrEmpty(ThumbnailPath) || !File.Exists(ThumbnailPath))
        {
            if (_assets is null) return;

            ThumbnailPath = await _assets.EnsureClipThumbnailAsync(ClipId);
            if (string.IsNullOrEmpty(ThumbnailPath)) return;
        }

        try
        {
            Thumbnail = await Task.Run(() => new Bitmap(ThumbnailPath));
        }
        catch
        {
            // A thumbnail that will not decode is not worth failing the panel over.
        }
    }
}
