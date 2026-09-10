using System.Threading.Tasks;

namespace ClipStudio.UI.ViewModels.Settings;

/// <summary>
/// The actions an attention entry can offer, which belong to other parts of the application.
/// </summary>
/// <remarks>
/// Implemented by <see cref="SettingsViewModel"/>. The attention list is derivative: every problem
/// it shows is fixed somewhere that already exists - the source folder rows, the clip editor - and
/// duplicating any of that here would give the user two ways to do one thing. Routing the actions
/// through one seam keeps the section a reader of the library rather than a second editor of it,
/// and keeps it testable with a fake host.
/// </remarks>
public interface IAttentionActionHost
{
    /// <summary>Scans one source folder, importing anything in it that is not yet in the library.</summary>
    /// <param name="sourceFolderId">The folder to scan.</param>
    Task ScanSourceFolderAsync(int sourceFolderId);

    /// <summary>
    /// Shows the Source Folders section, where a folder can be archived, disabled or removed.
    /// </summary>
    /// <remarks>
    /// An unreachable folder has no single right answer - reconnect the drive, archive its clips,
    /// or drop the folder - so the entry takes the user to the controls rather than choosing.
    /// </remarks>
    void ShowSourceFolders();

    /// <summary>Opens a clip in the detail view.</summary>
    /// <param name="clipId">The clip to open.</param>
    /// <returns>
    /// <see langword="true"/> when the request could be routed; <see langword="false"/> when there
    /// is nowhere to open it, as in a test.
    /// </returns>
    bool OpenClip(int clipId);
}
