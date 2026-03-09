using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels.WizardSteps;

/// <summary>
/// View model for the source folders wizard step.
/// Allows the user to add one or more watched folders before entering the main app.
/// Folders are persisted to the database immediately as they are added.
/// This step is always ready to proceed; folders can also be configured later in Settings.
/// </summary>
public sealed partial class SourceFoldersStepViewModel : WizardStepViewModel
{
    private readonly ISourceFolderRepository _folders;
    private readonly ILibraryWatcherService _watcher;

    /// <inheritdoc/>
    public override string Title => "Source Folders";

    /// <inheritdoc/>
    public override int StepNumber => 2;

    /// <summary>Gets the collection of folder rows added during the wizard.</summary>
    public ObservableCollection<WizardFolderItemViewModel> AddedFolders { get; } = new();

    /// <summary>Gets or sets the path entered in the add-folder text box.</summary>
    [ObservableProperty]
    private string _newFolderPath = string.Empty;

    /// <summary>Gets or sets the validation error from the last add attempt, if any.</summary>
    [ObservableProperty]
    private string? _folderError;

    /// <summary>Gets the command that validates and saves the typed folder path.</summary>
    public IAsyncRelayCommand AddFolderCommand { get; }

    /// <summary>Gets the command that asks the code-behind to open a native folder picker dialog.</summary>
    public IRelayCommand BrowseCommand { get; }

    /// <summary>
    /// Raised when the user clicks Browse so the code-behind can open a folder picker
    /// and set <see cref="NewFolderPath"/> with the result.
    /// </summary>
    public event Action? BrowseRequested;

    /// <summary>
    /// Initialises a new <see cref="SourceFoldersStepViewModel"/>.
    /// </summary>
    /// <param name="folders">Source folder repository for persistence.</param>
    /// <param name="watcher">Library watcher service to start watching the new folder.</param>
    public SourceFoldersStepViewModel(ISourceFolderRepository folders, ILibraryWatcherService watcher)
    {
        _folders         = folders;
        _watcher         = watcher;
        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync);
        BrowseCommand    = new RelayCommand(() => BrowseRequested?.Invoke());
    }

    private async Task AddFolderAsync()
    {
        FolderError = null;

        var path = NewFolderPath.Trim();

        if (string.IsNullOrEmpty(path))
        {
            FolderError = "Please enter a folder path.";
            return;
        }

        if (!Directory.Exists(path))
        {
            FolderError = "Directory does not exist.";
            return;
        }

        foreach (var existing in AddedFolders)
        {
            if (string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                FolderError = "This folder has already been added.";
                return;
            }
        }

        try
        {
            var folder = new SourceFolder { Path = path, IsActive = true };
            await _folders.AddAsync(folder);
            _watcher.StartWatching(folder.Path, folder.Id);
            AddedFolders.Add(new WizardFolderItemViewModel(folder.Id, path, RemoveFolderAsync));
            NewFolderPath = string.Empty;
        }
        catch (Exception ex)
        {
            FolderError = $"Failed to add folder: {ex.Message}";
        }
    }

    private async Task RemoveFolderAsync(WizardFolderItemViewModel item)
    {
        try
        {
            _watcher.StopWatching(item.Path);
            await _folders.DeleteAsync(item.FolderId);
            AddedFolders.Remove(item);
        }
        catch (Exception ex)
        {
            FolderError = $"Failed to remove folder: {ex.Message}";
        }
    }
}
