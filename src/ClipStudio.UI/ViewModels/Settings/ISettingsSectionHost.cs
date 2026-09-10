using System.Threading.Tasks;

namespace ClipStudio.UI.ViewModels.Settings;

/// <summary>
/// The context a settings section needs from the page that hosts it.
/// </summary>
/// <remarks>
/// Implemented by <see cref="SettingsViewModel"/>. The sections are otherwise independent - each
/// owns its own state and commands - but three things are page-wide and must not be duplicated:
/// the single status line, the reload that follows a change, and the loading flag a long repair
/// owns for its whole run. Routing those through one seam keeps the dependency one-way: a section
/// never reaches into another section, and every section stays testable against a fake host.
/// </remarks>
public interface ISettingsSectionHost
{
    /// <summary>Gets or sets the page-wide status line shown next to the Save button.</summary>
    string? StatusMessage { get; set; }

    /// <summary>Reloads every section from persistence so an applied change becomes visible.</summary>
    Task ReloadAsync();

    /// <summary>
    /// Marks a long-running library repair as started or finished.
    /// </summary>
    /// <param name="repairing">Whether a repair is now running.</param>
    /// <remarks>
    /// The repair owns the page's progress bar for its whole run. Navigating away and back
    /// re-enters the load, so the flag has to live on the page rather than on the section, or the
    /// bar disappears while the repair is still going.
    /// </remarks>
    void SetRepairing(bool repairing);

    /// <summary>
    /// Asks the main window to recount unreviewed clips, after an operation that changed how many
    /// there are (archiving or wiping a source folder).
    /// </summary>
    void RequestUnreviewedCountRefresh();

    /// <summary>
    /// Tells the page that a library repair finished, so sections holding library-derived counts
    /// can re-read them.
    /// </summary>
    Task NotifyLibraryRepairedAsync();
}
