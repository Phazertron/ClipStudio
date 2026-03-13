using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Enums;
using ClipStudio.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for <see cref="LibraryView"/>.
/// Triggers an initial data load when the view is attached to the visual tree,
/// routes card tap events to the <see cref="LibraryViewModel"/>, and maps
/// code-behind ComboBox selections to VM filter/sort properties.
/// Also synchronises the sort ComboBox when <see cref="LibraryViewModel.SortBy"/> changes
/// via the sortable column header buttons so both controls always reflect the same value.
/// Manages resizable detail-view column widths via custom pointer-driven resize handles and
/// persists them to <see cref="ClipStudio.Application.Models.AppSettings.LibraryColumnWidths"/>.
/// </summary>
public partial class LibraryView : UserControl
{
    private static readonly string[] SortKeys =
    [
        "DateDesc", "DateAsc", "NameAsc", "NameDesc", "DurationDesc", "DurationAsc", "RatingDesc"
    ];

    // Column key names indexed parallel to resizable columns in DetailsHeaderGrid.
    // Indices 2-8 map to keys 0-6.
    private static readonly string[] DetailColKeys = ["Name", "Game", "Tags", "Players", "Date", "Duration", "Rating"];

    // Minimum and maximum pixel width for any resizable column.
    private const double ColMinWidth = 40.0;
    private const double ColMaxWidth = 600.0;

    // ---- Active drag state ----

    private bool _isResizing;
    private int _resizeColIndex;
    private double _resizeDragStartX;
    private double _resizeStartWidth;

    private LibraryViewModel? _vm;

    /// <summary>Initialises a new <see cref="LibraryView"/> and wires up component events.</summary>
    public LibraryView()
    {
        InitializeComponent();
        AttachedToVisualTree   += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // Ensure ComboBoxes always show their default item, even if AXAML initialisation
        // fired the SelectionChanged handler before the DataContext was available.
        if (SortComboBox.SelectedIndex < 0)
            SortComboBox.SelectedIndex = 0;
        if (StatusComboBox.SelectedIndex < 0)
            StatusComboBox.SelectedIndex = 0;

        _vm = DataContext as LibraryViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;

            // Apply any persisted column widths to the header grid immediately.
            // Data rows are applied after the first load completes (via IsLoading → false).
            ApplyPersistedColumnWidthsToHeader();

            _vm.LoadCommand.Execute(null);
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = null;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Keep the sort ComboBox in sync when SortBy is changed via the column header buttons.
        if (e.PropertyName == nameof(LibraryViewModel.SortBy) && _vm is not null)
        {
            var idx = Array.IndexOf(SortKeys, _vm.SortBy);
            if (idx >= 0 && SortComboBox.SelectedIndex != idx)
                SortComboBox.SelectedIndex = idx;
        }

        // After loading completes, defer column-width sync to after the layout pass so that
        // all data row grids are in the visual tree before the widths are applied.
        if (e.PropertyName == nameof(LibraryViewModel.IsLoading) && _vm is { IsLoading: false })
            Dispatcher.UIThread.Post(ApplyPersistedColumnWidthsToDataRows, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Handles a tap on a clip card. In multi-select mode the tap toggles the card's selection
    /// instead of opening the clip; otherwise the clip detail view is opened.
    /// Taps that originate on the selection checkbox are suppressed here so they only fire
    /// the checkbox-specific handler.
    /// </summary>
    private void OnClipCardTapped(object? sender, RoutedEventArgs e)
    {
        // Don't handle taps that originated from the checkbox or any of its inner visuals.
        if (e.Source is Visual src &&
            src.FindAncestorOfType<CheckBox>(includeSelf: true) is not null)
            return;

        if (sender is Control control &&
            control.DataContext is ClipCardViewModel card &&
            DataContext is LibraryViewModel vm)
        {
            if (card.IsArchived)
            {
                card.IsArchivedNoticeVisible = true;
                return;
            }

            vm.HandleCardTapped(card);
        }
    }

    /// <summary>
    /// Handles a click on a clip card's selection checkbox.
    /// Toggles the clip's selection state in the view model.
    /// </summary>
    private void OnClipCheckBoxClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb &&
            cb.DataContext is ClipCardViewModel card &&
            DataContext is LibraryViewModel vm)
        {
            var newSelected = cb.IsChecked == true;
            vm.OnClipSelectionChanged(card, newSelected);
        }
    }

    /// <summary>Maps the sort ComboBox selection index to <see cref="LibraryViewModel.SortBy"/>.</summary>
    private void OnSortChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb &&
            DataContext is LibraryViewModel vm &&
            cb.SelectedIndex >= 0 &&
            cb.SelectedIndex < SortKeys.Length)
        {
            vm.SortBy = SortKeys[cb.SelectedIndex];
        }
    }

    /// <summary>Maps the status ComboBox selection to <see cref="LibraryViewModel.FilterStatus"/>.</summary>
    private void OnStatusFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && DataContext is LibraryViewModel vm)
        {
            vm.FilterStatus = cb.SelectedIndex switch
            {
                1 => ClipStatus.Unreviewed,
                2 => ClipStatus.Reviewed,
                3 => ClipStatus.Archived,
                _ => (ClipStatus?)null,
            };
        }
    }

    // ---- Hover-scrub pointer handlers ----

    /// <summary>
    /// Marks the card as hovered and lazily loads the preview strip sprite-sheet on first hover.
    /// </summary>
    private void OnCardPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Panel p && p.DataContext is ClipCardViewModel vm)
        {
            vm.IsHovering = true;
            if (vm.StripBitmap is null && vm.StripPath is not null)
                _ = vm.LoadStripAsync();
        }
    }

    /// <summary>Clears the hover state so the static thumbnail is shown again.</summary>
    private void OnCardPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Panel p && p.DataContext is ClipCardViewModel vm)
            vm.IsHovering = false;
    }

    /// <summary>
    /// Updates the scrub fraction based on the pointer's horizontal position within the card,
    /// causing the preview strip to advance to the corresponding frame.
    /// </summary>
    private void OnCardPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is Panel p && p.DataContext is ClipCardViewModel vm)
            vm.ScrubFraction = Math.Clamp(e.GetPosition(p).X / p.Bounds.Width, 0.0, 1.0);
    }

    // ---- Column resize: custom pointer-driven handles ----

    /// <summary>
    /// Called when the pointer is pressed on a resize handle Rectangle.
    /// Records the drag start position and the current column width.
    /// </summary>
    private void OnDetailResizeHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Rectangle handle) return;
        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
        if (handle.Tag is not string tagStr || !int.TryParse(tagStr, out var colIndex)) return;

        _isResizing      = true;
        _resizeColIndex  = colIndex;
        _resizeDragStartX = e.GetPosition(DetailsHeaderGrid).X;
        _resizeStartWidth = DetailsHeaderGrid.ColumnDefinitions[colIndex].ActualWidth;

        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    /// <summary>
    /// Called while the pointer moves over (or is captured by) a resize handle.
    /// Applies the drag delta to both the header column and all visible data rows
    /// to provide a live column-width preview.
    /// </summary>
    private void OnDetailResizeHandleMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizing) return;
        if (!e.GetCurrentPoint(sender as Visual).Properties.IsLeftButtonPressed)
        {
            CommitResize();
            return;
        }

        var delta    = e.GetPosition(DetailsHeaderGrid).X - _resizeDragStartX;
        var newWidth = Math.Clamp(_resizeStartWidth + delta, ColMinWidth, ColMaxWidth);
        ApplyDetailColumnWidth(_resizeColIndex, newWidth);
        e.Handled = true;
    }

    /// <summary>
    /// Called when the pointer is released after a column resize drag.
    /// Releases pointer capture and persists the new widths to <see cref="ISettingsService"/>.
    /// </summary>
    private void OnDetailResizeHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isResizing) return;
        e.Pointer.Capture(null);
        CommitResize();
        e.Handled = true;
    }

    private void CommitResize()
    {
        _isResizing = false;
        var settings = App.Services.GetRequiredService<ISettingsService>();
        settings.Current.LibraryColumnWidths = ReadHeaderColumnWidths();
        _ = settings.SaveAsync();
    }

    // ---- Column width persistence helpers ----

    /// <summary>
    /// Reads persisted column widths from <see cref="ISettingsService"/> and applies them
    /// to the details header grid. Called once on view attach.
    /// </summary>
    private void ApplyPersistedColumnWidthsToHeader()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var widths = settings.Current.LibraryColumnWidths;
        if (widths is null) return;
        SetDetailGridColumnWidths(DetailsHeaderGrid.ColumnDefinitions, widths);
    }

    /// <summary>
    /// Reads persisted column widths and applies them to all rendered detail row grids.
    /// Called after the view model finishes loading (all rows are now in the visual tree).
    /// </summary>
    private void ApplyPersistedColumnWidthsToDataRows()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var widths = settings.Current.LibraryColumnWidths;
        if (widths is null) return;
        ApplyDetailColumnWidthsToDataRows(widths);
    }

    /// <summary>Reads the current pixel widths of all resizable detail columns from the header grid.</summary>
    private Dictionary<string, double> ReadHeaderColumnWidths()
    {
        var defs = DetailsHeaderGrid.ColumnDefinitions;
        var widths = new Dictionary<string, double>();
        for (int i = 0; i < DetailColKeys.Length; i++)
            widths[DetailColKeys[i]] = defs[i + 2].ActualWidth;
        return widths;
    }

    /// <summary>
    /// Applies a single column width to the header grid and all rendered data row grids,
    /// providing a live preview while the user drags.
    /// </summary>
    private void ApplyDetailColumnWidth(int colIndex, double width)
    {
        DetailsHeaderGrid.ColumnDefinitions[colIndex].Width = new GridLength(width);

        foreach (var grid in DetailsItemsControl.GetVisualDescendants()
                     .OfType<Grid>()
                     .Where(g => g.Name == "DetailRowGrid"))
        {
            grid.ColumnDefinitions[colIndex].Width = new GridLength(width);
        }
    }

    /// <summary>
    /// Iterates all rendered detail row grids in the ItemsControl visual tree and sets their
    /// column widths to match <paramref name="widths"/>.
    /// </summary>
    private void ApplyDetailColumnWidthsToDataRows(Dictionary<string, double> widths)
    {
        foreach (var grid in DetailsItemsControl.GetVisualDescendants()
                     .OfType<Grid>()
                     .Where(g => g.Name == "DetailRowGrid"))
        {
            SetDetailGridColumnWidths(grid.ColumnDefinitions, widths);
        }
    }

    /// <summary>
    /// Applies a column-width dictionary to a <see cref="ColumnDefinitions"/> collection.
    /// Updates the seven resizable columns (indices 2-8).
    /// </summary>
    private static void SetDetailGridColumnWidths(ColumnDefinitions defs, Dictionary<string, double> widths)
    {
        for (int i = 0; i < DetailColKeys.Length; i++)
        {
            if (widths.TryGetValue(DetailColKeys[i], out var w) && w > 0)
                defs[i + 2].Width = new GridLength(w);
        }
    }
}
