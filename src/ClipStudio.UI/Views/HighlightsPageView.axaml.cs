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
using ClipStudio.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for <see cref="HighlightsPageView"/>.
/// Handles the sort ComboBox selection and row-tap events that cannot be expressed purely in AXAML.
/// Also manages resizable column widths via custom pointer-driven resize handles and persists
/// them to <see cref="ClipStudio.Application.Models.AppSettings.HighlightsColumnWidths"/>.
/// </summary>
public partial class HighlightsPageView : UserControl
{
    // Column key names indexed parallel to resizable columns 1-7 in HighlightsHeaderGrid.
    private static readonly string[] HighlightColKeys = ["Label", "Tags", "Game", "Players", "Start", "End", "Duration"];

    // Minimum and maximum pixel width for any resizable column.
    private const double ColMinWidth = 40.0;
    private const double ColMaxWidth = 600.0;

    // ---- Active drag state ----

    private bool _isResizing;
    private int _resizeColIndex;
    private double _resizeDragStartX;
    private double _resizeStartWidth;

    private HighlightsPageViewModel? _vm;

    /// <summary>Initialises a new <see cref="HighlightsPageView"/>.</summary>
    public HighlightsPageView()
    {
        InitializeComponent();
        AttachedToVisualTree   += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ApplyPersistedColumnWidthsToHeader();
        // Data row widths are applied after load completes (via IsLoading → false) so the rows
        // are in the visual tree. No early call here.

        // Use AddHandler with handledEventsToo=true so that taps absorbed by child controls
        // (text, icons, etc.) still bubble up and open the row in watch mode.
        if (HighlightsItemsControl is not null)
        {
            HighlightsItemsControl.AddHandler(
                TappedEvent,
                OnHighlightRowTappedHandledToo,
                Avalonia.Interactivity.RoutingStrategies.Bubble,
                handledEventsToo: true);
        }

        _vm = DataContext as HighlightsPageViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (HighlightsItemsControl is not null)
        {
            HighlightsItemsControl.RemoveHandler(TappedEvent, OnHighlightRowTappedHandledToo);
        }

        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = null;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HighlightsPageViewModel.IsLoading) &&
            _vm is { IsLoading: false })
        {
            // Double-post to ensure a second layout pass has run and all row containers are
            // fully realised in the visual tree before applying persisted column widths.
            Dispatcher.UIThread.Post(
                () => Dispatcher.UIThread.Post(ApplyPersistedColumnWidthsToDataRows, DispatcherPriority.Loaded),
                DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// Propagates the sort ComboBox selection to <see cref="HighlightsPageViewModel.SetSortCommand"/>.
    /// </summary>
    private void OnSortChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not HighlightsPageViewModel vm) return;
        if (SortComboBox.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is string key)
            vm.SetSortCommand.Execute(key);
    }

    /// <summary>
    /// Called when the user taps a highlight row; opens the highlight in watch mode.
    /// Registered in AXAML (standard, non-handledEventsToo path).
    /// </summary>
    private void OnHighlightRowTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Border { DataContext: HighlightRowViewModel row })
            row.RequestWatch();
    }

    /// <summary>
    /// Supplementary tap handler registered via <see cref="Avalonia.Interactivity.Interactive.AddHandler"/>
    /// with <c>handledEventsToo: true</c>. Catches taps on child controls (text, icons) that
    /// would otherwise mark the routed event as handled before it reaches the row Border.
    /// </summary>
    private void OnHighlightRowTappedHandledToo(object? sender, RoutedEventArgs e)
    {
        // Walk up the source visual tree to find the row Border whose DataContext is HighlightRowViewModel.
        Avalonia.Visual? visual = e.Source as Avalonia.Visual;
        while (visual is not null)
        {
            if (visual is Border b && b.DataContext is HighlightRowViewModel row)
            {
                row.RequestWatch();
                return;
            }
            visual = visual.GetVisualParent();
        }
    }

    // ---- Column resize: custom pointer-driven handles ----

    /// <summary>
    /// Called when the pointer is pressed on a resize handle Rectangle.
    /// Records the drag start position and the current column width.
    /// </summary>
    private void OnHighlightResizeHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Rectangle handle) return;
        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
        if (handle.Tag is not string tagStr || !int.TryParse(tagStr, out var colIndex)) return;

        _isResizing       = true;
        _resizeColIndex   = colIndex;
        _resizeDragStartX = e.GetPosition(HighlightsHeaderGrid).X;
        _resizeStartWidth = HighlightsHeaderGrid.ColumnDefinitions[colIndex].ActualWidth;

        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    /// <summary>
    /// Called while the pointer moves over (or is captured by) a resize handle.
    /// Applies the drag delta to both the header column and all visible data rows
    /// to provide a live column-width preview.
    /// </summary>
    private void OnHighlightResizeHandleMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizing) return;
        if (!e.GetCurrentPoint(sender as Visual).Properties.IsLeftButtonPressed)
        {
            CommitHighlightResize();
            return;
        }

        var delta    = e.GetPosition(HighlightsHeaderGrid).X - _resizeDragStartX;
        var newWidth = Math.Clamp(_resizeStartWidth + delta, ColMinWidth, ColMaxWidth);
        ApplyHighlightColumnWidth(_resizeColIndex, newWidth);
        e.Handled = true;
    }

    /// <summary>
    /// Called when the pointer is released after a column resize drag.
    /// Releases pointer capture and persists the new widths to <see cref="ISettingsService"/>.
    /// </summary>
    private void OnHighlightResizeHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isResizing) return;
        e.Pointer.Capture(null);
        CommitHighlightResize();
        e.Handled = true;
    }

    private void CommitHighlightResize()
    {
        _isResizing = false;
        var settings = App.Services.GetRequiredService<ISettingsService>();
        settings.Current.HighlightsColumnWidths = ReadHeaderColumnWidths();
        _ = settings.SaveAsync();
    }

    // ---- Column width persistence helpers ----

    private void ApplyPersistedColumnWidthsToHeader()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var widths = settings.Current.HighlightsColumnWidths;
        if (widths is null) return;
        SetHighlightGridColumnWidths(HighlightsHeaderGrid.ColumnDefinitions, widths);
    }

    private void ApplyPersistedColumnWidthsToDataRows()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var widths = settings.Current.HighlightsColumnWidths;
        if (widths is null) return;
        ApplyColumnWidthsToDataRows(widths);
    }

    private Dictionary<string, double> ReadHeaderColumnWidths()
    {
        var defs = HighlightsHeaderGrid.ColumnDefinitions;
        var widths = new Dictionary<string, double>();
        for (int i = 0; i < HighlightColKeys.Length; i++)
            widths[HighlightColKeys[i]] = defs[i + 1].ActualWidth;
        return widths;
    }

    /// <summary>
    /// Applies a single column width to the header grid and all visible data row grids
    /// immediately, providing a live preview while the user drags.
    /// </summary>
    private void ApplyHighlightColumnWidth(int colIndex, double width)
    {
        HighlightsHeaderGrid.ColumnDefinitions[colIndex].Width = new GridLength(width);

        foreach (var grid in HighlightsItemsControl.GetVisualDescendants()
                     .OfType<Grid>()
                     .Where(g => g.Name == "HighlightRowGrid"))
        {
            grid.ColumnDefinitions[colIndex].Width = new GridLength(width);
        }
    }

    private void ApplyColumnWidthsToDataRows(Dictionary<string, double> widths)
    {
        foreach (var grid in HighlightsItemsControl.GetVisualDescendants()
                     .OfType<Grid>()
                     .Where(g => g.Name == "HighlightRowGrid"))
        {
            SetHighlightGridColumnWidths(grid.ColumnDefinitions, widths);
        }
    }

    private static void SetHighlightGridColumnWidths(ColumnDefinitions defs, Dictionary<string, double> widths)
    {
        for (int i = 0; i < HighlightColKeys.Length; i++)
        {
            if (widths.TryGetValue(HighlightColKeys[i], out var w) && w > 0)
                defs[i + 1].Width = new GridLength(w);
        }
    }
}
