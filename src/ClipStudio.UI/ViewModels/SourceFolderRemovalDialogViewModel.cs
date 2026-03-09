using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the source folder removal confirmation dialog.
/// Presents the user with Archive, Wipe, and Cancel options and requires
/// a second confirmation before a destructive Wipe action is committed.
/// </summary>
public sealed partial class SourceFolderRemovalDialogViewModel : ViewModelBase
{
    /// <summary>Gets the absolute path of the source folder being removed.</summary>
    public string FolderPath { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the second-confirmation panel for the
    /// destructive Wipe action is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isWipeConfirmVisible;

    /// <summary>
    /// Callback set by the dialog code-behind that closes the window and returns the chosen result.
    /// </summary>
    public Action<SourceFolderRemovalResult>? CloseRequested { get; set; }

    /// <summary>Gets the command that archives all clips from the folder and closes the dialog.</summary>
    public IRelayCommand ArchiveCommand { get; }

    /// <summary>Gets the command that reveals the Wipe second-confirmation panel.</summary>
    public IRelayCommand ShowWipeConfirmCommand { get; }

    /// <summary>
    /// Gets the command that performs the destructive Wipe after the user has seen the warning
    /// and clicked the second confirmation.
    /// </summary>
    public IRelayCommand ConfirmWipeCommand { get; }

    /// <summary>Gets the command that hides the Wipe confirmation panel and returns to the initial choice.</summary>
    public IRelayCommand BackFromWipeCommand { get; }

    /// <summary>Gets the command that cancels the removal entirely and closes the dialog.</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="SourceFolderRemovalDialogViewModel"/>.
    /// </summary>
    /// <param name="folderPath">The absolute path of the source folder being removed.</param>
    public SourceFolderRemovalDialogViewModel(string folderPath)
    {
        FolderPath = folderPath;

        ArchiveCommand       = new RelayCommand(() => CloseRequested?.Invoke(SourceFolderRemovalResult.Archive));
        ShowWipeConfirmCommand = new RelayCommand(() => IsWipeConfirmVisible = true);
        ConfirmWipeCommand   = new RelayCommand(() => CloseRequested?.Invoke(SourceFolderRemovalResult.Wipe));
        BackFromWipeCommand  = new RelayCommand(() => IsWipeConfirmVisible = false);
        CancelCommand        = new RelayCommand(() => CloseRequested?.Invoke(SourceFolderRemovalResult.Cancel));
    }
}
