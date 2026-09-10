using System.Collections.Generic;
using Material.Icons;

namespace ClipStudio.UI.ViewModels.Settings;

/// <summary>
/// The Keyboard Shortcuts section of the Settings page.
/// </summary>
/// <remarks>
/// Read-only, and built from the same registry the key handler reads, so it cannot describe a
/// shortcut the application does not have or miss one it does. Remapping is not offered yet; the
/// identifiers in <see cref="ClipStudio.UI.Input.KeyboardShortcuts"/> exist so it can be added
/// without the rest of the application changing.
/// </remarks>
public sealed class ShortcutsSectionViewModel : SettingsSectionViewModel
{
    /// <inheritdoc/>
    public override string Title => "Keyboard shortcuts";

    /// <inheritdoc/>
    public override MaterialIconKind Icon => MaterialIconKind.KeyboardOutline;

    /// <summary>Gets the shortcuts, grouped by category.</summary>
    public IReadOnlyList<ShortcutGroupViewModel> Groups { get; } = ShortcutGroupViewModel.BuildAll();

    /// <summary>Initialises a new <see cref="ShortcutsSectionViewModel"/>.</summary>
    /// <param name="host">The settings page hosting this section.</param>
    public ShortcutsSectionViewModel(ISettingsSectionHost host)
        : base(host)
    {
    }
}
