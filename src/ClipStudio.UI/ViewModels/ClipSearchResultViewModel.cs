using System.Linq;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// One clip offered as a link target in the link picker.
/// </summary>
public sealed class ClipSearchResultViewModel : ViewModelBase
{
    /// <summary>Gets the clip's identifier.</summary>
    public int ClipId { get; }

    /// <summary>Gets the clip's file name.</summary>
    public string FileName { get; }

    /// <summary>Gets the game tag on the clip, or an empty string.</summary>
    public string GameName { get; }

    /// <summary>Gets when the clip was recorded, formatted for display.</summary>
    public string RecordedAtDisplay { get; }

    /// <summary>Gets the clip's duration, formatted for display.</summary>
    public string DurationDisplay { get; }

    /// <summary>Initialises a new <see cref="ClipSearchResultViewModel"/>.</summary>
    /// <param name="clip">The candidate clip.</param>
    public ClipSearchResultViewModel(Clip clip)
    {
        ClipId            = clip.Id;
        FileName          = clip.FileName;
        RecordedAtDisplay = clip.CreatedAt.ToLocalTime().ToString("dd MMM yyyy  HH:mm");

        GameName = clip.ClipTags
            .FirstOrDefault(ct => ct.Tag?.Type == TagType.Game)?.Tag?.Name ?? string.Empty;

        DurationDisplay = clip.Duration.TotalHours >= 1
            ? clip.Duration.ToString(@"h\:mm\:ss")
            : clip.Duration.ToString(@"m\:ss");
    }
}
