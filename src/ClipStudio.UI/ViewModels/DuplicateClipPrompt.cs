using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// The question put to the user about one duplicate file: what is being imported, what the library
/// already holds, and how many more of these are waiting behind it.
/// </summary>
/// <param name="IncomingFilePath">Absolute path of the file being imported.</param>
/// <param name="ExistingClip">The clip already in the library with the same contents.</param>
/// <param name="RemainingCount">
/// How many further duplicates are still to be resolved after this one. Drives whether an
/// "apply to the rest" option is worth offering at all.
/// </param>
/// <param name="Match">
/// How the match was recognised. A shared name is a warning; identical contents is a fact, and the
/// user is told which so they can judge it.
/// </param>
public sealed record DuplicateClipPrompt(
    string IncomingFilePath,
    Clip ExistingClip,
    int RemainingCount,
    DuplicateMatchKind Match = DuplicateMatchKind.Content);
