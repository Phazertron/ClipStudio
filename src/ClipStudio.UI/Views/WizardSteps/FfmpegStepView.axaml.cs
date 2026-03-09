using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ClipStudio.UI.ViewModels.WizardSteps;

namespace ClipStudio.UI.Views.WizardSteps;

/// <summary>
/// Code-behind for <see cref="FfmpegStepView"/>.
/// Subscribes to the view model's <see cref="FfmpegStepViewModel.BrowseRequested"/> event
/// to open a native folder picker dialog.
/// </summary>
public partial class FfmpegStepView : UserControl
{
    private FfmpegStepViewModel? _vm;

    /// <summary>Initialises a new <see cref="FfmpegStepView"/>.</summary>
    public FfmpegStepView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null)
            _vm.BrowseRequested -= OnBrowseRequested;

        _vm = DataContext as FfmpegStepViewModel;

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
            Title       = "Select FFmpeg binary folder",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
            _vm.ManualFolder = folders[0].Path.LocalPath;
    }
}
