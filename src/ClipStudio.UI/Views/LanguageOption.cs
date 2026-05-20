namespace ClipStudio.UI.Views;

/// <summary>
/// Represents a spoken language selectable in the transcription language picker.
/// Bundles the BCP-47 language code with a human-readable display name.
/// </summary>
/// <param name="Code">BCP-47 language code passed to Whisper (e.g. "en", "fr", "auto").</param>
/// <param name="DisplayName">Human-readable label shown in the ComboBox (e.g. "English").</param>
public sealed record LanguageOption(string Code, string DisplayName)
{
    /// <inheritdoc/>
    public override string ToString() => DisplayName;
}
