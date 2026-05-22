using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the Relocate Clip dialog.
/// Offers two modes: physically move the file back to its original location, or register
/// the new folder as a watched source and update the database record.
/// </summary>
public sealed partial class RelocateClipDialogViewModel : ObservableObject
{
    private readonly IReadOnlyList<string> _existingSourcePaths;

    /// <summary>Gets the display name of the clip being relocated.</summary>
    public string FileName { get; }

    /// <summary>Gets the original (now-broken) file path stored in the database.</summary>
    public string OriginalPath { get; }

    /// <summary>Gets the parent folder of the original path.</summary>
    public string OriginalFolder => Path.GetDirectoryName(OriginalPath) ?? string.Empty;

    /// <summary>Gets or sets which relocation strategy the user has chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMoveBack))]
    [NotifyPropertyChangedFor(nameof(IsRegisterNewSource))]
    [NotifyPropertyChangedFor(nameof(WillAddNewSource))]
    private RelocateMode _mode = RelocateMode.RegisterNewSource;

    /// <summary>Gets or sets the absolute path to the file selected by the user (its current location).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(WillAddNewSource))]
    [NotifyPropertyChangedFor(nameof(NewFolderPath))]
    [NotifyPropertyChangedFor(nameof(NewFolderName))]
    private string _selectedFilePath = string.Empty;

    /// <summary>Gets a value indicating whether the Move Back mode is active.</summary>
    public bool IsMoveBack
    {
        get => Mode == RelocateMode.MoveBack;
        set { if (value) Mode = RelocateMode.MoveBack; }
    }

    /// <summary>Gets a value indicating whether the Register New Source mode is active.</summary>
    public bool IsRegisterNewSource
    {
        get => Mode == RelocateMode.RegisterNewSource;
        set { if (value) Mode = RelocateMode.RegisterNewSource; }
    }

    /// <summary>Gets the parent folder of the currently selected file.</summary>
    public string NewFolderPath => string.IsNullOrEmpty(SelectedFilePath)
        ? string.Empty
        : Path.GetDirectoryName(SelectedFilePath) ?? string.Empty;

    /// <summary>Gets the display name (last segment) of the new folder.</summary>
    public string NewFolderName => string.IsNullOrEmpty(NewFolderPath)
        ? string.Empty
        : Path.GetFileName(NewFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>Gets a value indicating whether the Confirm button should be enabled.</summary>
    public bool CanConfirm => File.Exists(SelectedFilePath);

    /// <summary>
    /// Gets a value indicating whether confirming in <see cref="RelocateMode.RegisterNewSource"/> mode
    /// will register the chosen folder as a new watched source.
    /// Always <see langword="false"/> in <see cref="RelocateMode.MoveBack"/> mode.
    /// </summary>
    public bool WillAddNewSource =>
        IsRegisterNewSource &&
        CanConfirm &&
        !string.IsNullOrEmpty(NewFolderPath) &&
        !_existingSourcePaths.Any(p =>
        {
            try { return string.Equals(Path.GetFullPath(p), Path.GetFullPath(NewFolderPath), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        });

    /// <summary>Raised when the user confirms the relocation.</summary>
    public event Action? Confirmed;

    /// <summary>Raised when the user cancels the dialog.</summary>
    public event Action? Cancelled;

    /// <summary>
    /// Initialises a new <see cref="RelocateClipDialogViewModel"/>.
    /// </summary>
    /// <param name="fileName">The display name of the clip.</param>
    /// <param name="originalPath">The original (broken) file path stored in the database.</param>
    /// <param name="existingSourcePaths">Paths of all currently registered source folders.</param>
    public RelocateClipDialogViewModel(
        string fileName,
        string originalPath,
        IReadOnlyList<string> existingSourcePaths)
    {
        FileName             = fileName;
        OriginalPath         = originalPath;
        _existingSourcePaths = existingSourcePaths;
    }

    /// <summary>Called by the code-behind after the user picks a file via the file picker.</summary>
    public void SetBrowsedPath(string path) => SelectedFilePath = path;

    /// <summary>Confirms the dialog and raises <see cref="Confirmed"/>.</summary>
    public void Confirm() => Confirmed?.Invoke();

    /// <summary>Cancels the dialog and raises <see cref="Cancelled"/>.</summary>
    public void Cancel() => Cancelled?.Invoke();
}
