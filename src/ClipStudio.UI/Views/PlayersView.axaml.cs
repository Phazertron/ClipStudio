using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ClipStudio.Core.Entities;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for <see cref="PlayersView"/>.
/// Triggers initial data load and handles the icon file picker and alias removal.
/// </summary>
public partial class PlayersView : UserControl
{
    /// <summary>Initialises a new <see cref="PlayersView"/> and wires up component events.</summary>
    public PlayersView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DataContextChanged   += OnDataContextChanged;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is PlayersViewModel vm)
            vm.LoadCommand.Execute(null);
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is PlayersViewModel vm)
            vm.BrowseIconRequested += OnBrowseIconRequested;
    }

    private void OnBrowseIconRequested()
    {
        _ = BrowseIconAsync();
    }

    private async Task BrowseIconAsync()
    {
        if (DataContext is not PlayersViewModel vm)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Player Icon",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Image files") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] },
                FilePickerFileTypes.All
            ]
        });

        if (files.Count == 1)
            vm.SetIconPath(files[0].TryGetLocalPath());
    }

    /// <summary>
    /// Opens the edit form for the tapped player row, unless the tap originated on a Button
    /// (e.g. the Delete button).
    /// </summary>
    private void OnPlayerRowTapped(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Visual src && src.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;

        if (sender is Border { DataContext: PlayerRowViewModel row })
            row.EditCommand.Execute(null);
    }

    /// <summary>
    /// Handles the Remove button click on each alias chip.
    /// The button's <c>Tag</c> property holds the <see cref="PlayerAlias"/> to remove.
    /// </summary>
    private void OnRemoveAliasClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PlayerAlias alias && DataContext is PlayersViewModel vm)
            _ = vm.RemoveAliasAsync(alias);
    }
}
