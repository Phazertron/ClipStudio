namespace ClipStudio.UI.ViewModels;

/// <summary>
/// The context <see cref="RelatedClipsViewModel"/> needs from the clip detail view.
/// </summary>
/// <remarks>
/// Implemented by <see cref="ClipDetailViewModel"/>. Links are defined between the open clip and
/// another, so the child needs to know which clip is open and to be able to send the user to a
/// different one - and nothing else. Keeping it to that means the panel never reaches into
/// playback, tags or the export queue.
/// </remarks>
public interface IRelatedClipsHost
{
    /// <summary>Gets the identifier of the clip currently open, or null when none is.</summary>
    int? OpenClipId { get; }

    /// <summary>Opens a different clip in the detail view.</summary>
    /// <param name="clipId">The clip to open.</param>
    void OpenClip(int clipId);
}
