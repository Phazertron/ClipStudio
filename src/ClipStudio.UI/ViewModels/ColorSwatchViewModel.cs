using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for a single color swatch in the tag editor color palette.
/// Holds the hex color string and a command that applies it to the parent form.
/// </summary>
public sealed class ColorSwatchViewModel : ViewModelBase
{
    /// <summary>Gets the hex color string (e.g. "#E74C3C") for display and binding.</summary>
    public string Color { get; }

    /// <summary>Gets the command that selects this color in the parent edit form.</summary>
    public IRelayCommand SelectCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="ColorSwatchViewModel"/>.
    /// </summary>
    /// <param name="color">The hex color value.</param>
    /// <param name="onSelect">Callback invoked when the user clicks this swatch.</param>
    public ColorSwatchViewModel(string color, System.Action<string> onSelect)
    {
        Color         = color;
        SelectCommand = new RelayCommand(() => onSelect(color));
    }
}
