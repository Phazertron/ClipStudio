using ClipStudio.UI.ViewModels.Settings;

namespace ClipStudio.Tests.Fakes;

/// <summary>
/// Stands in for the rest of the application so <see cref="AttentionSectionViewModel"/> can be
/// driven without navigation, source folder rows or a window.
/// </summary>
public sealed class FakeAttentionActionHost : IAttentionActionHost
{
    /// <summary>Gets the source folder identifiers a scan was requested for, in order.</summary>
    public List<int> ScannedFolderIds { get; } = [];

    /// <summary>Gets the number of times the Source Folders section was asked for.</summary>
    public int ShowSourceFoldersCalls { get; private set; }

    /// <summary>Gets the clip identifiers an entry asked to open, in order.</summary>
    public List<int> OpenedClipIds { get; } = [];

    /// <inheritdoc/>
    public Task ScanSourceFolderAsync(int sourceFolderId)
    {
        ScannedFolderIds.Add(sourceFolderId);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void ShowSourceFolders() => ShowSourceFoldersCalls++;

    /// <inheritdoc/>
    public bool OpenClip(int clipId)
    {
        OpenedClipIds.Add(clipId);
        return true;
    }
}
