namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Identifies the UI sound effect to play via <see cref="Services.ISoundService"/>.
/// </summary>
public enum SoundEffect
{
    /// <summary>Short confirmation tick played when a new highlight is created.</summary>
    HighlightCreated,

    /// <summary>Ascending notification chime played when a scan or import completes.</summary>
    ImportComplete,

    /// <summary>Soft descending tone played when a clip is sent to the trash.</summary>
    ClipTrashed,

    /// <summary>Success chime played when an export job finishes successfully.</summary>
    ExportComplete,
}
