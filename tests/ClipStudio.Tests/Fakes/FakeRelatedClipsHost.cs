using ClipStudio.UI.ViewModels;

namespace ClipStudio.Tests.Fakes;

/// <summary>
/// Stands in for the clip detail view so <see cref="RelatedClipsViewModel"/> can be driven on its own.
/// </summary>
public sealed class FakeRelatedClipsHost : IRelatedClipsHost
{
    /// <inheritdoc/>
    public int? OpenClipId { get; set; } = 1;

    /// <summary>Gets the clips the panel asked to open, in order.</summary>
    public List<int> OpenedClipIds { get; } = [];

    /// <inheritdoc/>
    public void OpenClip(int clipId) => OpenedClipIds.Add(clipId);
}
