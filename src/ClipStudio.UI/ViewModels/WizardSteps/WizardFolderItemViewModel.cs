using System;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels.WizardSteps;

/// <summary>
/// Represents a single source folder row in the setup wizard's source folders step.
/// Exposes the folder path and a command to remove it from the wizard list and the database.
/// </summary>
public sealed class WizardFolderItemViewModel : ViewModelBase
{
    /// <summary>Gets the database identifier of the source folder.</summary>
    public int FolderId { get; }

    /// <summary>Gets the absolute filesystem path of the folder.</summary>
    public string Path { get; }

    /// <summary>Gets the command that removes this folder entry.</summary>
    public IAsyncRelayCommand RemoveCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="WizardFolderItemViewModel"/>.
    /// </summary>
    /// <param name="folderId">Database identifier of the source folder.</param>
    /// <param name="path">Absolute path of the folder.</param>
    /// <param name="onRemove">Async callback invoked when the remove command is executed.</param>
    public WizardFolderItemViewModel(int folderId, string path, Func<WizardFolderItemViewModel, System.Threading.Tasks.Task> onRemove)
    {
        FolderId      = folderId;
        Path          = path;
        RemoveCommand = new AsyncRelayCommand(() => onRemove(this));
    }
}
