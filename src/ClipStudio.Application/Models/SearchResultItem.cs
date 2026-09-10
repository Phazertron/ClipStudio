namespace ClipStudio.Application.Models;

/// <summary>
/// One search result, in the terms the flyout needs to show it and act on it.
/// </summary>
/// <param name="Kind">What kind of thing this is.</param>
/// <param name="Title">The main line, usually the name of the thing found.</param>
/// <param name="Subtitle">Supporting context - which clip, when, what was said.</param>
/// <param name="ClipId">The clip to open, when activating this result opens one.</param>
/// <param name="EntityId">The tag or player to filter by, when activating this result filters.</param>
/// <param name="SeekTo">Where in the clip to start, for a highlight or a caption.</param>
public sealed record SearchResultItem(
    SearchResultKind Kind,
    string Title,
    string? Subtitle = null,
    int? ClipId = null,
    int? EntityId = null,
    TimeSpan? SeekTo = null);
