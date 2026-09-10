namespace ClipStudio.Core.Models;

/// <summary>
/// A highlight's time range alongside the duration of the clip it has to fit inside.
/// </summary>
/// <param name="Id">The highlight's database identifier.</param>
/// <param name="ClipId">The clip the highlight belongs to.</param>
/// <param name="Label">The highlight's label, for messages. May be null or empty.</param>
/// <param name="ClipFileName">The clip's file name, for messages.</param>
/// <param name="StartTime">The highlight's stored start.</param>
/// <param name="EndTime">The highlight's stored end.</param>
/// <param name="ClipDuration">The clip's duration.</param>
/// <remarks>
/// A highlight's range is stored independently of its clip's duration, so the two can disagree
/// after a clip is relocated to a shorter file or trimmed. Deciding whether they do needs only
/// these columns, not every tag on the highlight and its clip.
/// </remarks>
public sealed record HighlightRangeSnapshot(
    int Id,
    int ClipId,
    string? Label,
    string ClipFileName,
    TimeSpan StartTime,
    TimeSpan EndTime,
    TimeSpan ClipDuration);
