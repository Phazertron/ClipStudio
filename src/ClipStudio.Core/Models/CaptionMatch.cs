namespace ClipStudio.Core.Models;

/// <summary>
/// One transcript line matching a search, with enough context to jump to it.
/// </summary>
/// <param name="ClipId">The clip the line belongs to.</param>
/// <param name="ClipFileName">That clip's file name, for display.</param>
/// <param name="StartTime">Where in the clip the line is spoken.</param>
/// <param name="Text">The line itself.</param>
/// <remarks>
/// The existing caption query returns clip identifiers only, which is enough to filter the library
/// but not to show what was said or to seek to it.
/// </remarks>
public sealed record CaptionMatch(int ClipId, string ClipFileName, TimeSpan StartTime, string Text);
