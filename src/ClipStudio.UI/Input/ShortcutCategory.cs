namespace ClipStudio.UI.Input;

/// <summary>
/// The groups keyboard shortcuts are listed under.
/// </summary>
/// <remarks>
/// A shortcut belongs to a category rather than to a page, because the same key can mean the same
/// thing in more than one place and the list is read by someone looking for a capability, not for
/// a screen.
/// </remarks>
public enum ShortcutCategory
{
    /// <summary>Moving around the application.</summary>
    Navigation = 0,

    /// <summary>Controlling video playback in the clip editor.</summary>
    Playback = 1,

    /// <summary>Marking and rating the clip being viewed.</summary>
    Marking = 2,

    /// <summary>Working with highlights.</summary>
    Highlights = 3,
}
