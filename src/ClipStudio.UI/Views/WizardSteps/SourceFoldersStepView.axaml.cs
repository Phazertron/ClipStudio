using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using ClipStudio.UI.ViewModels.WizardSteps;

namespace ClipStudio.UI.Views.WizardSteps;

/// <summary>
/// Code-behind for <see cref="SourceFoldersStepView"/>.
/// Handles the Enter key shortcut to submit the folder path, and subscribes to the view
/// model's <see cref="SourceFoldersStepViewModel.BrowseRequested"/> event to open a native
/// folder picker dialog.
/// </summary>
public partial class SourceFoldersStepView : UserControl
{
    private SourceFoldersStepViewModel? _vm;

    /// <summary>Initialises a new <see cref="SourceFoldersStepView"/>.</summary>
    public SourceFoldersStepView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null)
            _vm.BrowseRequested -= OnBrowseRequested;

        _vm = DataContext as SourceFoldersStepViewModel;

        if (_vm is not null)
            _vm.BrowseRequested += OnBrowseRequested;
    }

    private async void OnBrowseRequested()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || _vm is null)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title         = "Select source folder",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
            _vm.NewFolderPath = folders[0].Path.LocalPath;
    }

    /// <summary>Triggers the Add command when the user presses Enter in the path text box.</summary>
    private void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SourceFoldersStepViewModel vm)
            vm.AddFolderCommand.Execute(null);
    }
}
