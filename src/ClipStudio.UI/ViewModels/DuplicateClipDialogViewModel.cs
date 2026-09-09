using System;
using System.IO;
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
