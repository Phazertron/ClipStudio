namespace ClipStudio.Application.Models;

/// <summary>
/// One thing the library health check found that needs a person to decide about.
/// </summary>
/// <param name="Kind">What kind of problem this is.</param>
/// <param name="Summary">A one-line description, ready to show without further formatting.</param>
/// <param name="SourceFolderId">The source folder involved, when the finding is about one.</param>
/// <param name="ClipId">The clip involved, when the finding is about one.</param>
/// <param name="Path">The folder or file path involved, when the finding names one.</param>
/// <param name="Count">
/// How many items the finding covers. One for a finding about a single clip; the number of files
/// or affected clips for a finding about a folder.
/// </param>
/// <remarks>
/// Deliberately a plain description rather than an action: the check reports, and the attention
/// list decides what to offer. That keeps the rule that nothing about the user's library is
/// guessed at or resolved without being asked.
/// </remarks>
public sealed record LibraryHealthFinding(
    LibraryHealthFindingKind Kind,
    string Summary,
    int? SourceFolderId = null,
    int? ClipId = null,
    string? Path = null,
    int Count = 1);
