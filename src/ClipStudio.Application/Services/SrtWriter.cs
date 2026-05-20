using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClipStudio.Core.Entities;

namespace ClipStudio.Application.Services;

/// <summary>
/// Formats <see cref="TranscriptionSegment"/> data as an SRT subtitle file.
/// </summary>
public static class SrtWriter
{
    /// <summary>
    /// Produces the full text content of an SRT subtitle file from the given segments.
    /// </summary>
    /// <param name="segments">
    /// The ordered collection of transcription segments. Each segment becomes one SRT block.
    /// </param>
    /// <returns>A UTF-8 string in valid SRT format.</returns>
    public static string Write(IEnumerable<TranscriptionSegment> segments)
    {
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            sb.AppendLine(seg.IndexNumber.ToString());
            sb.AppendLine($"{FormatTimestamp(seg.StartMs)} --> {FormatTimestamp(seg.EndMs)}");
            sb.AppendLine(seg.Text.Trim());
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Writes an SRT subtitle file to the specified path, creating the directory if necessary.
    /// </summary>
    /// <param name="segments">The ordered collection of transcription segments.</param>
    /// <param name="filePath">The absolute path of the output .srt file.</param>
    /// <param name="cancellationToken">Token to cancel the write operation.</param>
    public static async Task WriteToFileAsync(
        IEnumerable<TranscriptionSegment> segments,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var content = Write(segments);
        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
    }

    /// <summary>
    /// Converts a millisecond offset to an SRT timestamp string (HH:mm:ss,fff).
    /// </summary>
    private static string FormatTimestamp(long ms)
    {
        var hours   = ms / 3_600_000;
        var minutes = ms % 3_600_000 / 60_000;
        var seconds = ms % 60_000 / 1_000;
        var millis  = ms % 1_000;
        return $"{hours:D2}:{minutes:D2}:{seconds:D2},{millis:D3}";
    }
}
