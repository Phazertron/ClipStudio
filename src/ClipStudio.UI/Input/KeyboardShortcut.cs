using System.Collections.Generic;
using Avalonia.Input;

namespace ClipStudio.UI.Input;

/// <summary>
/// One keyboard shortcut: what it does, and the key that does it.
/// </summary>
/// <param name="Id">A stable identifier, so a remapping could be stored against it later.</param>
/// <param name="Category">The group this shortcut is listed under.</param>
/// <param name="Description">What the shortcut does, in the words the help list shows.</param>
/// <param name="Key">The key that triggers it.</param>
/// <param name="Modifiers">The modifiers that must be held.</param>
/// <remarks>
/// The point of describing shortcuts as data is that the handler and the help list read the same
/// source. A hand-maintained list beside a <c>switch</c> drifts the first time someone adds a
/// shortcut and forgets the other half; there is nothing to forget here.
/// </remarks>
public sealed record KeyboardShortcut(
    string Id,
    ShortcutCategory Category,
    string Description,
    Key Key,
    KeyModifiers Modifiers = KeyModifiers.None)
{
    /// <summary>Gets the gesture as it is written in the help list, for example <c>Ctrl + F</c>.</summary>
    public string GestureDisplay
    {
        get
        {
            var parts = new List<string>(4);

            if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(KeyModifiers.Alt))     parts.Add("Alt");
            if (Modifiers.HasFlag(KeyModifiers.Shift))   parts.Add("Shift");

            parts.Add(KeyDisplay(Key));
            return string.Join(" + ", parts);
        }
    }

    /// <summary>Gets whether this shortcut matches a key press.</summary>
    /// <param name="key">The key that was pressed.</param>
    /// <param name="modifiers">The modifiers held at the time.</param>
    /// <returns><see langword="true"/> when the press should trigger this shortcut.</returns>
    public bool Matches(Key key, KeyModifiers modifiers) => Key == key && Modifiers == modifiers;

    /// <summary>Renders a key the way a keyboard prints it rather than the way the enum spells it.</summary>
    /// <param name="key">The key to render.</param>
    /// <returns>The display string.</returns>
    private static string KeyDisplay(Key key) => key switch
    {
        Avalonia.Input.Key.OemComma        => ",",
        Avalonia.Input.Key.OemPeriod       => ".",
        Avalonia.Input.Key.OemQuestion     => "/",
        Avalonia.Input.Key.OemMinus        => "-",
        Avalonia.Input.Key.OemPlus         => "=",
        Avalonia.Input.Key.OemOpenBrackets => "[",
        Avalonia.Input.Key.Left            => "Left arrow",
        Avalonia.Input.Key.Right           => "Right arrow",
        Avalonia.Input.Key.Up              => "Up arrow",
        Avalonia.Input.Key.Down            => "Down arrow",
        Avalonia.Input.Key.D0              => "0",
        Avalonia.Input.Key.D1              => "1",
        Avalonia.Input.Key.D2              => "2",
        Avalonia.Input.Key.D3              => "3",
        Avalonia.Input.Key.D4              => "4",
        Avalonia.Input.Key.D5              => "5",
        _                                  => key.ToString(),
    };
}
