using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ClipStudio.Core.Entities;
using ClipStudio.UI.Behaviors;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for <see cref="ClipDetailView"/>.
/// Handles pointer events on the position slider, keyboard shortcuts for transport control,
/// auto-focus on attach, positioning of the timeline handle markers, and tag-picker interactions.
/// </summary>
public partial class ClipDetailView : UserControl
{
    private ClipDetailViewModel? _vm;

    private readonly TagPickerBehavior _gameTagPicker;
    private readonly TagPickerBehavior _generalTagPicker;

    /// <summary>Initialises a new <see cref="ClipDetailView"/> and wires up component events.</summary>
    public ClipDetailView()
    {
        InitializeComponent();

        AttachedToVisualTree   += (_, _) => { Focus(); SubscribeVm(); };
        DetachedFromVisualTree += (_, _) => UnsubscribeVm();

        HighlightHandleCanvas.SizeChanged += (_, _) => UpdateHandlePositions();
        TrimHandleCanvas.SizeChanged      += (_, _) => UpdateTrimHandlePositions();

        // Re-measure handle positions once the slider's template is applied so the
        // thumb element exists in the visual tree and GetSliderTrackMetrics() returns
        // the real thumb half-width instead of the fallback.
        PositionSlider.TemplateApplied += (_, _) =>
        {
            UpdateHandlePositions();
            UpdateTrimHandlePositions();
        };

        // The Slider template marks pointer events as Handled internally (thumb capture, track click).
        // Register with handledEventsToo: true so our BeginScrub/EndScrub handlers always fire.
        PositionSlider.AddHandler(
            InputElement.PointerPressedEvent,
            OnSliderPointerPressed,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        PositionSlider.AddHandler(
            InputElement.PointerReleasedEvent,
            OnSliderPointerReleased,
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        // ---- AutoCompleteBox tag pickers ----
        //
        // The commit state machine lives in TagPickerBehavior; see TagPickerStateMachine for why a
        // commit has to be spread across SelectionChanged, KeyDown and DropDownClosed. Each picker
        // supplies only its tag source and what a committed tag is applied to.

        _gameTagPicker = new TagPickerBehavior(
            GameTagPicker,
            picker => ResolveTag(picker, isGame: true),
            (_, tag) => AddGameTag(tag));
        _gameTagPicker.Attach();

        _generalTagPicker = new TagPickerBehavior(
            GeneralTagPicker,
            picker => ResolveTag(picker, isGame: false),
            (_, tag) => AddGeneralTag(tag));
        _generalTagPicker.Attach();

        // The new-highlight picker is static in the template, so it is wired here rather than on
        // Loaded, but it behaves like the per-highlight rows and keeps focus after each commit.
        AttachHighlightPicker(NewHighlightTagPicker);
    }

    /// <summary>Applies a committed game tag to the current clip.</summary>
    /// <param name="tag">The tag to add.</param>
    private void AddGameTag(Tag tag)
    {
        if (DataContext is ClipDetailViewModel vm) _ = vm.AddGameTagDirectlyAsync(tag);
    }

    /// <summary>Applies a committed general tag to the current clip.</summary>
    /// <param name="tag">The tag to add.</param>
    private void AddGeneralTag(Tag tag)
    {
        if (DataContext is ClipDetailViewModel vm) _ = vm.AddGeneralTagDirectlyAsync(tag);
    }

    // ---- DataContext wiring ----

    /// <summary>
    /// Subscribes to the two editors that drive the timeline handles. The parent view model no
    /// longer raises anything this view watches - both handle sets are owned by children.
    /// </summary>
    private void SubscribeVm()
    {
        _vm = DataContext as ClipDetailViewModel;
        if (_vm is null) return;

        _vm.Trim.PropertyChanged            += OnTrimPropertyChanged;
        _vm.HighlightEditor.PropertyChanged += OnHighlightEditorPropertyChanged;
    }

    /// <summary>Detaches the handle-position subscriptions.</summary>
    private void UnsubscribeVm()
    {
        if (_vm is not null)
        {
            _vm.Trim.PropertyChanged            -= OnTrimPropertyChanged;
            _vm.HighlightEditor.PropertyChanged -= OnHighlightEditorPropertyChanged;
        }

        _vm = null;
    }

    /// <summary>Keeps the highlight handles following the editor's range.</summary>
    /// <param name="sender">The highlight editor.</param>
    /// <param name="e">The property that changed.</param>
    private void OnHighlightEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HighlightEditorViewModel.HighlightStartFraction)
                           or nameof(HighlightEditorViewModel.HighlightEndFraction))
        {
            UpdateHandlePositions();
        }
    }

    /// <summary>Keeps the trim handles following the editor's range.</summary>
    /// <param name="sender">The trim editor.</param>
    /// <param name="e">The property that changed.</param>
    private void OnTrimPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TrimEditorViewModel.TrimStartFraction)
                           or nameof(TrimEditorViewModel.TrimEndFraction))
        {
            UpdateTrimHandlePositions();
        }
    }

    private void UpdateHandlePositions()
    {
        if (_vm is null) return;
        var (inset, usable) = GetSliderTrackMetrics();
        if (usable <= 0) return;

        Canvas.SetLeft(HandleStart, inset + _vm.HighlightEditor.HighlightStartFraction * usable - 8);
        Canvas.SetLeft(HandleEnd,   inset + _vm.HighlightEditor.HighlightEndFraction   * usable - 8);
    }

    private void UpdateTrimHandlePositions()
    {
        if (_vm is null) return;
        var (inset, usable) = GetSliderTrackMetrics();
        if (usable <= 0) return;

        Canvas.SetLeft(TrimHandleStart, inset + _vm.Trim.TrimStartFraction * usable - 8);
        Canvas.SetLeft(TrimHandleEnd,   inset + _vm.Trim.TrimEndFraction   * usable - 8);
    }

    /// <summary>
    /// Returns the pixel inset from the canvas left edge to where the slider thumb centre
    /// sits at fraction 0, and the total distance the thumb centre can travel (usable width).
    /// Derived from the thumb's actual current rendered position so it is accurate regardless
    /// of theme, DPI, or internal Fluent template padding.
    /// Falls back to 8 px on each side if layout has not yet completed.
    /// </summary>
    private (double inset, double usableWidth) GetSliderTrackMetrics()
    {
        var sliderWidth = PositionSlider.Bounds.Width;
        var fallback    = (8.0, Math.Max(1.0, sliderWidth - 16.0));

        var track = PositionSlider.GetVisualDescendants().OfType<Track>().FirstOrDefault();
        if (track?.Thumb is null) return fallback;

        var thumb = track.Thumb;

        // Prefer Bounds (post-arrange); fall back to DesiredSize (post-measure).
        var thumbWidth = thumb.Bounds.Width > 0 ? thumb.Bounds.Width : thumb.DesiredSize.Width;
        if (thumbWidth <= 0) return fallback;

        var trackWidth = track.Bounds.Width > 0 ? track.Bounds.Width : sliderWidth;
        if (trackWidth <= thumbWidth) return fallback;

        var usableWidth = trackWidth - thumbWidth;

        // Walk up from the thumb to PositionSlider summing Bounds.X at each level.
        // This gives the thumb's left-edge position in slider-local coordinates without
        // relying on TransformToVisual (which can fail across template boundaries).
        var thumbXInSlider = 0.0;
        for (Visual? v = thumb; v is not null && !ReferenceEquals(v, PositionSlider); v = v.GetVisualParent())
            thumbXInSlider += v.Bounds.X;

        // At the current value fraction f: thumbLeft = trackOriginX + f * usableWidth
        // Therefore: trackOriginX = thumbLeft - f * usableWidth
        // And: inset (thumb centre at f=0) = trackOriginX + thumbWidth/2
        var fraction    = PositionSlider.Maximum > 0
            ? Math.Clamp(PositionSlider.Value / PositionSlider.Maximum, 0.0, 1.0)
            : 0.0;
        var trackOriginX = thumbXInSlider - fraction * usableWidth;
        var inset        = trackOriginX + thumbWidth / 2.0;

        // Sanity check — inset must be a small positive offset (the thumb half-width).
        if (inset < 0 || inset > sliderWidth / 4.0) return fallback;

        return (inset, usableWidth);
    }

    // ---- Keyboard shortcuts ----

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Do not steal keyboard events when a text-input control has focus,
        // so that the user can type spaces and navigate text normally.
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        if (focused is TextBox or NumericUpDown) return;
        // Also suppress transport shortcuts when focus is inside an AutoCompleteBox
        // (its inner TextBox has focus; arrow keys should navigate the dropdown, not seek).
        if (focused?.FindAncestorOfType<AutoCompleteBox>(includeSelf: true) is not null) return;

        if (DataContext is not ClipDetailViewModel vm) return;

        switch (e.Key)
        {
            case Key.Space:
                vm.Playback.PlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                vm.Playback.SkipBackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
                vm.Playback.SkipForwardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.OemComma:
                vm.Playback.FrameBackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.OemPeriod:
                vm.Playback.FrameForwardCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // ---- Video click-to-play ----

    /// <summary>
    /// Toggles playback when the user clicks directly on the video surface.
    /// </summary>
    private void OnVideoPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is ClipDetailViewModel vm)
            vm.Playback.PlayPauseCommand.Execute(null);
    }

    // ---- Shared tag-resolution helper ----

    /// <summary>
    /// Resolves the tag to commit from an <see cref="AutoCompleteBox"/>. Checks
    /// <see cref="AutoCompleteBox.SelectedItem"/> first; if null, falls back to a
    /// text-based search of the appropriate available-tags collection using the same
    /// ContainsOrdinal logic as the dropdown filter (exact match preferred).
    /// </summary>
    private Tag? ResolveTag(AutoCompleteBox picker, bool isGame)
    {
        if (picker.SelectedItem is Tag selected)
            return selected;

        var text = picker.Text;
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (DataContext is not ClipDetailViewModel vm) return null;

        // The same collections the pickers show, so a keyboard commit can never land on a tag the
        // dropdown was not offering - these exclude what the clip already has.
        var source = isGame ? vm.ClipGameTagOptions : vm.ClipGeneralTagOptions;

        // Prefer exact match; fall back to first Contains match (same as FilterMode=ContainsOrdinal).
        return source.FirstOrDefault(t => string.Equals(t.Name, text, StringComparison.OrdinalIgnoreCase))
            ?? source.FirstOrDefault(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    // ---- Highlight tag pickers (DataTemplate-hosted, per-instance behaviour) ----

    /// <summary>
    /// One behaviour per realised highlight picker. Rows come and go with the DataTemplate, so the
    /// entries are added on Loaded and removed on Unloaded.
    /// </summary>
    private readonly Dictionary<AutoCompleteBox, TagPickerBehavior> _highlightPickers = new();

    /// <summary>
    /// Creates and attaches a <see cref="TagPickerBehavior"/> for a highlight picker, unless one is
    /// already attached.
    /// </summary>
    /// <param name="picker">The picker to wire up.</param>
    private void AttachHighlightPicker(AutoCompleteBox picker)
    {
        if (_highlightPickers.ContainsKey(picker)) return;

        var behavior = new TagPickerBehavior(
            picker,
            ResolveHighlightTag,
            CommitHighlightTag,
            refocusAfterCommit: true);

        _highlightPickers[picker] = behavior;
        behavior.Attach();
    }

    /// <summary>
    /// Called when a highlight-row AutoCompleteBox enters the visual tree (DataTemplate realisation).
    /// </summary>
    private void OnHighlightTagPickerLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not AutoCompleteBox picker) return;
        AttachHighlightPicker(picker);
    }

    /// <summary>
    /// Called when a highlight-row AutoCompleteBox leaves the visual tree. Detaches its behaviour so
    /// the handlers do not outlive the row.
    /// </summary>
    private void OnHighlightTagPickerUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not AutoCompleteBox picker) return;
        if (!_highlightPickers.Remove(picker, out var behavior)) return;

        behavior.Detach();
    }

    /// <summary>
    /// Resolves a tag from a highlight-row or new-highlight AutoCompleteBox using text-based fallback.
    /// </summary>
    private Tag? ResolveHighlightTag(AutoCompleteBox picker)
    {
        if (picker.SelectedItem is Tag selected) return selected;

        var text = picker.Text;
        if (string.IsNullOrWhiteSpace(text)) return null;

        IReadOnlyList<Tag> source;
        if (ReferenceEquals(picker, NewHighlightTagPicker) && DataContext is ClipDetailViewModel cdvm)
            source = cdvm.AvailableGeneralTags;
        else if (picker.DataContext is HighlightViewModel hvm)
            source = hvm.AvailableTags;
        else
            return null;

        return source.FirstOrDefault(t => string.Equals(t.Name, text, StringComparison.OrdinalIgnoreCase))
            ?? source.FirstOrDefault(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Commits a tag to the new-highlight pending list or to an existing highlight.</summary>
    private void CommitHighlightTag(AutoCompleteBox picker, Tag tag)
    {
        if (ReferenceEquals(picker, NewHighlightTagPicker) && DataContext is ClipDetailViewModel vm)
            vm.HighlightEditor.AddPendingHighlightTag(tag);
        else if (picker.DataContext is HighlightViewModel hvm)
            _ = hvm.AddTagDirectlyAsync(tag);
    }

    // ---- Trim timestamp text boxes ----

    /// <summary>
    /// Commits the trim start value when the user leaves the start TextBox.
    /// </summary>
    private void OnTrimStartLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClipDetailViewModel vm)
            vm.Trim.CommitTrimStartCommand.Execute(null);
    }

    /// <summary>
    /// Commits the trim end value when the user leaves the end TextBox.
    /// </summary>
    private void OnTrimEndLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClipDetailViewModel vm)
            vm.Trim.CommitTrimEndCommand.Execute(null);
    }

    /// <summary>
    /// Commits the trim start value when the user presses Enter in the start TextBox.
    /// </summary>
    private void OnTrimStartKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ClipDetailViewModel vm)
        {
            vm.Trim.CommitTrimStartCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Commits the trim end value when the user presses Enter in the end TextBox.
    /// </summary>
    private void OnTrimEndKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ClipDetailViewModel vm)
        {
            vm.Trim.CommitTrimEndCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ---- Trim output browse ----

    /// <summary>
    /// Opens a save-file dialog so the user can pick the output path for a trim export.
    /// </summary>
    private async void OnBrowseTrimOutputClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ClipDetailViewModel vm) return;

        var startDir = string.IsNullOrWhiteSpace(vm.Trim.TrimOutputPath)
            ? null
            : Path.GetDirectoryName(vm.Trim.TrimOutputPath);

        var suggestedName = string.IsNullOrWhiteSpace(vm.Trim.TrimOutputPath)
            ? "output.mp4"
            : Path.GetFileName(vm.Trim.TrimOutputPath);

        IStorageFolder? folder = null;
        if (startDir is not null && Directory.Exists(startDir))
            folder = await TopLevel.GetTopLevel(this)!.StorageProvider
                .TryGetFolderFromPathAsync(startDir);

        var result = await TopLevel.GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title             = "Choose output file",
                SuggestedStartLocation = folder,
                SuggestedFileName = suggestedName,
                FileTypeChoices   =
                [
                    new FilePickerFileType("MP4 video") { Patterns = ["*.mp4"] },
                    new FilePickerFileType("MKV video") { Patterns = ["*.mkv"] },
                    new FilePickerFileType("All files") { Patterns  = ["*.*"] },
                ],
            });

        if (result is not null)
            vm.Trim.TrimOutputPath = result.Path.LocalPath;
    }

    // ---- Highlight handle drag ----

    private string? _draggingHighlightHandle;
    private string? _draggingTrimHandle;

    /// <summary>Begins dragging the highlight start handle.</summary>
    private void OnHighlightHandleStartPressed(object? sender, PointerPressedEventArgs e)
    {
        _draggingHighlightHandle = "start";
        e.Pointer.Capture(HighlightHandleCanvas);
        e.Handled = true;
    }

    /// <summary>Begins dragging the highlight end handle.</summary>
    private void OnHighlightHandleEndPressed(object? sender, PointerPressedEventArgs e)
    {
        _draggingHighlightHandle = "end";
        e.Pointer.Capture(HighlightHandleCanvas);
        e.Handled = true;
    }

    /// <summary>Updates the highlight start or end fraction while dragging.</summary>
    private void OnHighlightCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingHighlightHandle is null || _vm is null) return;
        var (inset, usable) = GetSliderTrackMetrics();
        if (usable <= 0) return;
        var pointerX  = e.GetPosition(HighlightHandleCanvas).X;
        var fraction  = Math.Clamp((pointerX - inset) / usable, 0.0, 1.0);
        if (_draggingHighlightHandle == "start")
            _vm.HighlightEditor.SetHighlightStartFromFraction(fraction);
        else
            _vm.HighlightEditor.SetHighlightEndFromFraction(fraction);
    }

    /// <summary>Ends the highlight handle drag and releases pointer capture.</summary>
    private void OnHighlightCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _draggingHighlightHandle = null;
        e.Pointer.Capture(null);
    }

    // ---- Trim handle drag ----

    /// <summary>Begins dragging the trim start handle.</summary>
    private void OnTrimHandleStartPressed(object? sender, PointerPressedEventArgs e)
    {
        _draggingTrimHandle = "start";
        e.Pointer.Capture(TrimHandleCanvas);
        e.Handled = true;
    }

    /// <summary>Begins dragging the trim end handle.</summary>
    private void OnTrimHandleEndPressed(object? sender, PointerPressedEventArgs e)
    {
        _draggingTrimHandle = "end";
        e.Pointer.Capture(TrimHandleCanvas);
        e.Handled = true;
    }

    /// <summary>Updates the trim start or end fraction while dragging.</summary>
    private void OnTrimCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingTrimHandle is null || _vm is null) return;
        var (inset, usable) = GetSliderTrackMetrics();
        if (usable <= 0) return;
        var pointerX  = e.GetPosition(TrimHandleCanvas).X;
        var fraction  = Math.Clamp((pointerX - inset) / usable, 0.0, 1.0);
        if (_draggingTrimHandle == "start")
            _vm.Trim.SetTrimStartFromFraction(fraction);
        else
            _vm.Trim.SetTrimEndFromFraction(fraction);
    }

    /// <summary>Ends the trim handle drag and releases pointer capture.</summary>
    private void OnTrimCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _draggingTrimHandle = null;
        e.Pointer.Capture(null);
    }

    // ---- Slider scrub support ----

    /// <summary>
    /// Notifies the view model that the user has begun dragging the position slider,
    /// so that incoming player time-change events do not overwrite the slider value.
    /// Registered via <see cref="InputElement.AddHandler"/> with <c>handledEventsToo: true</c>
    /// to ensure it fires even when the Slider template has already marked the event as handled.
    /// </summary>
    private void OnSliderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ClipDetailViewModel vm)
            vm.Playback.BeginScrub();
    }

    /// <summary>
    /// Notifies the view model that the user has released the slider and seeks to the new position.
    /// Registered via <see cref="InputElement.AddHandler"/> with <c>handledEventsToo: true</c>.
    /// </summary>
    private void OnSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is ClipDetailViewModel vm)
            vm.Playback.EndScrub(PositionSlider.Value);
    }
}
