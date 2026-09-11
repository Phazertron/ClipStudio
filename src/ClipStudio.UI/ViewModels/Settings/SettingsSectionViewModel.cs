using System.Threading.Tasks;
using ClipStudio.Application.Models;
using Material.Icons;

namespace ClipStudio.UI.ViewModels.Settings;

/// <summary>
/// Base class for one navigable section of the Settings page.
/// </summary>
/// <remarks>
/// A section owns its own observable state and commands, and is matched to its view by
/// <see cref="ViewLocator"/> through the namespace pair
/// <c>ClipStudio.UI.ViewModels.Settings</c> / <c>ClipStudio.UI.Views.Settings</c>.
/// Sections that map onto <see cref="AppSettings"/> override <see cref="LoadFrom"/> and
/// <see cref="ApplyTo"/>; sections backed by something else - the source folder table, the
/// library - override <see cref="RefreshAsync"/> instead. Both are no-ops by default so a
/// section only implements the half it actually has.
/// </remarks>
public abstract class SettingsSectionViewModel : ViewModelBase
{
    /// <summary>Initialises a new <see cref="SettingsSectionViewModel"/>.</summary>
    /// <param name="host">The settings page hosting this section.</param>
    protected SettingsSectionViewModel(ISettingsSectionHost host)
    {
        Host = host;
    }

    /// <summary>Gets the settings page hosting this section.</summary>
    protected ISettingsSectionHost Host { get; }

    /// <summary>Gets the label shown for this section in the settings navigation list.</summary>
    public abstract string Title { get; }

    /// <summary>Gets the Material icon shown beside <see cref="Title"/>.</summary>
    public abstract MaterialIconKind Icon { get; }

    /// <summary>
    /// Gets whether this section is currently asking to be visited, which colours its icon in the
    /// settings navigation list.
    /// </summary>
    /// <remarks>
    /// False for every section but Attention required, and false there too while the library needs
    /// nothing. A section that is permanently highlighted teaches the user to ignore the highlight.
    /// </remarks>
    public virtual bool NeedsAttention => false;

    /// <summary>
    /// Copies this section's fields out of the settings snapshot.
    /// </summary>
    /// <param name="settings">The current settings snapshot.</param>
    public virtual void LoadFrom(AppSettings settings)
    {
    }

    /// <summary>
    /// Writes this section's fields back into the settings snapshot, ready to be saved.
    /// </summary>
    /// <param name="settings">The settings snapshot to update in place.</param>
    public virtual void ApplyTo(AppSettings settings)
    {
    }

    /// <summary>
    /// Re-reads whatever this section shows that does not live in <see cref="AppSettings"/>.
    /// </summary>
    public virtual Task RefreshAsync() => Task.CompletedTask;
}
