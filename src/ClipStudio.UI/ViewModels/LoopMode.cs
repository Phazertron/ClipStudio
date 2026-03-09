namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Controls playback loop behaviour at the end of a clip or highlight.
/// </summary>
public enum LoopMode
{
    /// <summary>Stop playback when the end is reached.</summary>
    Off,

    /// <summary>Restart the current clip or highlight from its beginning.</summary>
    LoopThis,

    /// <summary>Advance to the next item in the sequence; wrap back to the first when the last ends.</summary>
    LoopAll,
}
