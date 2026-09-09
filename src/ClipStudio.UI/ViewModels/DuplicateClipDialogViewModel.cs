using System;
using System.IO;
using ClipStudio.Application.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the dialog shown when an imported file already exists in the library by content.
/// </summary>
/// <remarks>
/// Deliberately states both sides. The two files usually differ in name, folder and date - that is
/// why the duplicate was not caught by path - so the user needs to see what is being compared
/// before choosing, not just be told "this is a duplicate".
/// </remarks>
public sealed partial class DuplicateClipDialogViewModel : ViewModelBase
{
    /// <summary>Gets the dialog's headline, which differs by how the match was recognised.</summary>
    public string Headline { get; }

    /// <summary>Gets the explanation of what was matched and what it does or does not prove.</summary>
    public string Explanation { get; }

    /// <summary>Gets the label above the existing clip, naming what it shares with the new file.</summary>
    public string ExistingLabel { get; }

    /// <summary>Gets the file name of the file being imported.</summary>
    public string IncomingFileName { get; }

    /// <summary>Gets the folder holding the file being imported.</summary>
    public string IncomingFolder { get; }

    /// <summary>Gets the file name of the clip already in the library.</summary>
    public string ExistingFileName { get; }

    /// <summary>Gets the folder holding the clip already in the library.</summary>
    public string ExistingFolder { get; }

    /// <summary>Gets when the existing clip was recorded, for telling two similar files apart.</summary>
    public string ExistingRecordedAt { get; }

    /// <summary>
    /// Gets a value indicating whether more duplicates are waiting behind this one, which is when
    /// answering for all of them is worth offering.
    /// </summary>
    public bool HasRemaining { get; }

    /// <summary>Gets the label for the "apply to the rest" option, naming how many are left.</summary>
    public string RemainingLabel { get; }

    /// <summary>
    /// Gets or sets whether the chosen action should apply to every remaining duplicate without
    /// asking again.
    /// </summary>
    [ObservableProperty] private bool _applyToRemaining;

    /// <summary>
    /// Callback set by the dialog code-behind that closes the window and returns the resolution.
    /// </summary>
    public Action<DuplicateResolution>? CloseRequested { get; set; }

    /// <summary>Gets the command that leaves the file out of the library.</summary>
    public IRelayCommand SkipCommand { get; }

    /// <summary>Gets the command that imports the file despite the duplicate.</summary>
    public IRelayCommand ImportAnywayCommand { get; }

    /// <summary>Initialises a new <see cref="DuplicateClipDialogViewModel"/>.</summary>
    /// <param name="prompt">The duplicate being asked about.</param>
    public DuplicateClipDialogViewModel(DuplicateClipPrompt prompt)
    {
        var byName = prompt.Match == DuplicateMatchKind.FileName;

        Headline = byName
            ? "A clip with this name is already in your library"
            : "This clip is already in your library";

        // Said plainly, because the two mean different things: one file name can belong to two
        // unrelated recordings, whereas identical contents are identical contents.
        Explanation = byName
            ? "A clip in a different folder has the same file name. The recordings may or may not be the same - check the folders below before deciding."
            : "The file below has the same contents as a clip you already have. It was not caught by name, so the two differ somewhere - check which one you want before deciding.";

        ExistingLabel = byName ? "Already in the library, same name" : "Already in the library";

        IncomingFileName = Path.GetFileName(prompt.IncomingFilePath);
        IncomingFolder   = Path.GetDirectoryName(prompt.IncomingFilePath) ?? string.Empty;

        ExistingFileName = prompt.ExistingClip.FileName;
        ExistingFolder   = Path.GetDirectoryName(prompt.ExistingClip.FilePath) ?? string.Empty;
        ExistingRecordedAt = prompt.ExistingClip.CreatedAt == default
            ? "Unknown date"
            : prompt.ExistingClip.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        HasRemaining   = prompt.RemainingCount > 0;
        RemainingLabel = prompt.RemainingCount == 1
            ? "Do the same for the 1 other duplicate"
            : $"Do the same for the other {prompt.RemainingCount} duplicates";

        SkipCommand = new RelayCommand(() => CloseRequested?.Invoke(
            new DuplicateResolution(DuplicateClipDecision.Skip, ApplyToRemaining)));

        ImportAnywayCommand = new RelayCommand(() => CloseRequested?.Invoke(
            new DuplicateResolution(DuplicateClipDecision.ImportAnyway, ApplyToRemaining)));
    }
}
