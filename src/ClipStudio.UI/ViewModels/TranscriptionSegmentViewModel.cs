using System;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for a single timed text segment in the transcription panel.
/// Exposes formatted timestamp strings and a seek callback so the user can click a
/// segment to jump the player to that position.
/// </summary>
public sealed class TranscriptionSegmentViewModel : ViewModelBase
{
    private readonly Action<long> _seekRequested;

    /// <summary>Gets the 1-based sequential index of this segment.</summary>
    public int IndexNumber { get; }

    /// <summary>Gets the start position of this segment in milliseconds.</summary>
    public long StartMs { get; }

    /// <summary>Gets the end position of this segment in milliseconds.</summary>
    public long EndMs { get; }

    /// <summary>Gets the transcribed text content.</summary>
    public string Text { get; }

    /// <summary>Gets a formatted start timestamp, e.g. <c>0:12</c>.</summary>
    public string StartDisplay => FormatMs(StartMs);

    /// <summary>Gets a formatted end timestamp, e.g. <c>0:15</c>.</summary>
    public string EndDisplay => FormatMs(EndMs);

    /// <summary>Gets a combined timestamp range label, e.g. <c>0:12 – 0:15</c>.</summary>
    public string TimestampRange => $"{StartDisplay} – {EndDisplay}";

    /// <summary>
    /// Initialises a new <see cref="TranscriptionSegmentViewModel"/>.
    /// </summary>
    /// <param name="indexNumber">The 1-based sequential index.</param>
    /// <param name="startMs">Segment start in milliseconds.</param>
    /// <param name="endMs">Segment end in milliseconds.</param>
    /// <param name="text">The transcribed text.</param>
    /// <param name="seekRequested">
    /// Callback invoked when the user taps this segment; receives the start position in milliseconds.
    /// </param>
    public TranscriptionSegmentViewModel(
        int indexNumber,
        long startMs,
        long endMs,
        string text,
        Action<long> seekRequested)
    {
        IndexNumber    = indexNumber;
        StartMs        = startMs;
        EndMs          = endMs;
        Text           = text;
        _seekRequested = seekRequested;
    }

    /// <summary>Invokes the seek callback with this segment's start position.</summary>
    public void RequestSeek() => _seekRequested(StartMs);

    private static string FormatMs(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }
}
