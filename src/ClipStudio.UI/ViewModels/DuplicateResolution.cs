namespace ClipStudio.UI.ViewModels;

/// <summary>
/// The user's answer to a <see cref="DuplicateClipPrompt"/>.
/// </summary>
/// <param name="Decision">What to do with this file.</param>
/// <param name="ApplyToRemaining">
/// Whether the same decision should be applied to every remaining duplicate without asking again.
/// A scan can turn up dozens at once, and asking about each one separately is its own problem.
/// </param>
public sealed record DuplicateResolution(DuplicateClipDecision Decision, bool ApplyToRemaining = false)
{
    /// <summary>A resolution that skips this file only.</summary>
    public static DuplicateResolution Skip { get; } = new(DuplicateClipDecision.Skip);

    /// <summary>A resolution that imports this file only.</summary>
    public static DuplicateResolution ImportAnyway { get; } = new(DuplicateClipDecision.ImportAnyway);

    /// <summary>A resolution that imports this file and links it to the clip it duplicates.</summary>
    public static DuplicateResolution ImportAndLink { get; } = new(DuplicateClipDecision.ImportAndLink);
}
