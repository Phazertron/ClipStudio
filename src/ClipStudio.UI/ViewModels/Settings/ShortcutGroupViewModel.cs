using System.Collections.Generic;
using System.Linq;
using ClipStudio.UI.Input;

namespace ClipStudio.UI.ViewModels.Settings;

/// <summary>
/// One category of keyboard shortcuts, as the help list groups them.
/// </summary>
/// <param name="Title">The heading shown above the group.</param>
/// <param name="Shortcuts">The shortcuts in the group, in declaration order.</param>
public sealed record ShortcutGroupViewModel(string Title, IReadOnlyList<KeyboardShortcut> Shortcuts)
{
    /// <summary>
    /// Builds one group per category, skipping any category with nothing in it.
    /// </summary>
    /// <returns>The groups, in category order.</returns>
    /// <remarks>
    /// Read straight from <see cref="KeyboardShortcuts"/>, which is also what the key handler
    /// reads. There is no second list to keep in step.
    /// </remarks>
    public static IReadOnlyList<ShortcutGroupViewModel> BuildAll()
        =>
        [
            .. new[]
            {
                (ShortcutCategory.Playback,   "Playback"),
                (ShortcutCategory.Navigation, "Navigation"),
                (ShortcutCategory.Marking,    "Rating and marking"),
                (ShortcutCategory.Highlights, "Highlights"),
            }
            .Select(c => new ShortcutGroupViewModel(c.Item2, KeyboardShortcuts.InCategory(c.Item1).ToList()))
            .Where(g => g.Shortcuts.Count > 0)
        ];
}
