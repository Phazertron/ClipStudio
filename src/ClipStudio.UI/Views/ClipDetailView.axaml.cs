using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ClipStudio.Core.Entities;
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

    /// <summary>Initialises a new <see cref="ClipDetailView"/> and wires up component events.</summary>
    public ClipDetailView()
    {
        InitializeComponent();

        AttachedToVisualTree   += (_, _) => { Focus(); SubscribeVm(); };
        DetachedFromVisualTree += (_, _) => UnsubscribeVm();

        HighlightHandleCanvas.SizeChanged += (_, _) => UpdateHandlePositions();
        TrimHandleCanvas.SizeChanged      += (_, _) => UpdateTrimHandlePositions();

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
        // Three-part strategy so that all commit paths work reliably:
        //
        //   SelectionChanged  — fires whenever SelectedItem changes (arrow nav, click,
        //                       or Enter-induced confirmation). Saves the tag into a
        //                       _pendingXxxTag field. Only marks the selection as
        //                       _confirmed when the change was NOT caused by an arrow key,
        //                       because arrow navigation should not commit on its own —
        //                       the user still needs to press Enter or click.
        //                       IMPORTANT: Avalonia's AutoCompleteBox clears SelectedItem
        //                       before firing DropDownClosed, so this is the only reliable
        //                       moment to capture the chosen item.
        //
        //   KeyDown (Tunnel)  — intercepts Up/Down (sets _justNavByKey so SelectionChanged
        //                       knows the next event is navigation, not a commit), Enter
        //                       (marks _confirmed if a pending tag exists, or falls back to
        //                       text-based resolution), Tab (commits immediately and sets
        //                       _tabCommitted so DropDownClosed skips), and Escape.
        //
        //   DropDownClosed    — commits _pendingXxxTag when _confirmed is true; otherwise
        //                       clears all state. This covers both click (confirmed by
        //                       SelectionChanged) and Enter (confirmed by KeyDown).

        GameTagPicker.SelectionChanged    += OnGameTagPickerSelectionChanged;
        GeneralTagPicker.SelectionChanged += OnGeneralTagPickerSelectionChanged;

        GameTagPicker.DropDownClosed    += OnGameTagPickerDropDownClosed;
        GeneralTagPicker.DropDownClosed += OnGeneralTagPickerDropDownClosed;

        GameTagPicker.AddHandler(
            InputElement.KeyDownEvent,
            OnGameTagPickerKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        GeneralTagPicker.AddHandler(
            InputElement.KeyDownEvent,
            OnGeneralTagPickerKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    // ---- DataContext wiring ----

    private void SubscribeVm()
    {
        _vm = DataContext as ClipDetailViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void UnsubscribeVm()
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = null;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ClipDetailViewModel.HighlightStartFraction)
                           or nameof(ClipDetailViewModel.HighlightEndFraction))
        {
            UpdateHandlePositions();
        }
        else if (e.PropertyName is nameof(ClipDetailViewModel.TrimStartFraction)
                                or nameof(ClipDetailViewModel.TrimEndFraction))
        {
            UpdateTrimHandlePositions();
        }
    }

    private void UpdateHandlePositions()
    {
        if (_vm is null) return;

        var width = HighlightHandleCanvas.Bounds.Width;
        if (width <= 0) return;

        Canvas.SetLeft(HandleStart, _vm.HighlightStartFraction * width - 8);
        Canvas.SetLeft(HandleEnd,   _vm.HighlightEndFraction   * width - 8);
    }

    private void UpdateTrimHandlePositions()
    {
        if (_vm is null) return;

        var width = TrimHandleCanvas.Bounds.Width;
        if (width <= 0) return;

        Canvas.SetLeft(TrimHandleStart, _vm.TrimStartFraction * width - 8);
        Canvas.SetLeft(TrimHandleEnd,   _vm.TrimEndFraction   * width - 8);
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
                vm.PlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                vm.SkipBackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
                vm.SkipForwardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.OemComma:
                vm.FrameBackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.OemPeriod:
                vm.FrameForwardCommand.Execute(null);
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
            vm.PlayPauseCommand.Execute(null);
    }

    // ---- AutoCompleteBox tag pickers ----
    //
    // Three-part strategy so that all commit paths work reliably:
    //
    //   SelectionChanged  — Avalonia's AutoCompleteBox fires this when SelectedItem changes
    //                       (arrow nav, click, or Enter). We capture the item here into
    //                       _pendingXxxTag, but only mark _confirmed=true when the change
    //                       was NOT caused by arrow-key navigation (flagged by _justNavByKey).
    //                       IMPORTANT: Avalonia clears SelectedItem before DropDownClosed fires,
    //                       so SelectionChanged is the only reliable window to capture the item.
    //
    //   KeyDown (Tunnel)  — Up/Down: sets _justNavByKey so SelectionChanged ignores that change.
    //                       Enter: marks _confirmed if a pending tag exists, or falls back to
    //                       text resolution. Tab: commits immediately via text fallback and sets
    //                       _tabCommitted so DropDownClosed skips. Escape: sets _escapePending.
    //
    //   DropDownClosed    — commits _pendingXxxTag when _confirmed is true; otherwise clears state.

    // Per-picker state — game tag.
    private Tag?  _pendingGameTag;
    private bool  _gameTagConfirmed;
    private bool  _gameTagJustNavByKey;
    private bool  _gameTagEscapePending;
    private bool  _gameTagTabCommitted;

    // Per-picker state — general tag.
    private Tag?  _pendingGeneralTag;
    private bool  _generalTagConfirmed;
    private bool  _generalTagJustNavByKey;
    private bool  _generalTagEscapePending;
    private bool  _generalTagTabCommitted;

    // ---- Game tag picker ----

    /// <summary>
    /// Captures the selected item (or marks it as navigation-only) before
    /// Avalonia clears <see cref="AutoCompleteBox.SelectedItem"/> prior to closing.
    /// </summary>
    private void OnGameTagPickerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Read directly from SelectedItem — AddedItems may contain display strings, not Tag objects.
        var tag = GameTagPicker.SelectedItem as Tag;
        if (tag is null)
        {
            // SelectedItem was cleared internally by Avalonia after closing.
            // Do NOT clear _pendingGameTag — DropDownClosed will commit or discard it.
            return;
        }

        _pendingGameTag = tag;
        if (_gameTagJustNavByKey) { _gameTagJustNavByKey = false; _gameTagConfirmed = false; }
        else                      { _gameTagConfirmed = true; }
    }

    /// <summary>
    /// Intercepts Up/Down (flag navigation), Enter (set confirmed or text-resolve),
    /// Tab (text-resolve commit), and Escape on the game-tag picker.
    /// </summary>
    private void OnGameTagPickerKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
            case Key.Down:
                _gameTagJustNavByKey = true;
                return;

            case Key.Enter:
                if (_pendingGameTag is not null)
                {
                    _gameTagConfirmed = true;
                }
                else
                {
                    var t = ResolveTag(GameTagPicker, isGame: true);
                    if (t is not null)
                    {
                        _gameTagTabCommitted       = true;
                        GameTagPicker.Text         = string.Empty;
                        GameTagPicker.SelectedItem = null;
                        if (DataContext is ClipDetailViewModel vm)
                            _ = vm.AddGameTagDirectlyAsync(t);
                    }
                }
                return;

            case Key.Escape:
                _gameTagEscapePending = true;
                return;

            case Key.Tab:
                var tag = ResolveTag(GameTagPicker, isGame: true);
                if (tag is not null)
                {
                    _gameTagTabCommitted       = true;
                    GameTagPicker.Text         = string.Empty;
                    GameTagPicker.SelectedItem = null;
                    if (DataContext is ClipDetailViewModel vm2)
                        _ = vm2.AddGameTagDirectlyAsync(tag);
                }
                // Do NOT set e.Handled — let Tab move focus naturally so DropDownClosed fires
                // and resets _gameTagTabCommitted.
                return;
        }
    }

    /// <summary>
    /// Commits the pending game tag when <c>_gameTagConfirmed</c> is true (Enter or click path).
    /// Escape and Tab paths are short-circuited by their guard flags.
    /// </summary>
    private void OnGameTagPickerDropDownClosed(object? sender, EventArgs e)
    {
        if (_gameTagEscapePending)
        {
            _gameTagEscapePending      = false;
            _pendingGameTag            = null;
            _gameTagConfirmed          = false;
            GameTagPicker.SelectedItem = null;
            return;
        }

        if (_gameTagTabCommitted)
        {
            _gameTagTabCommitted = false;
            return;
        }

        if (_gameTagConfirmed && _pendingGameTag is not null)
        {
            var tag = _pendingGameTag;
            _pendingGameTag            = null;
            _gameTagConfirmed          = false;
            GameTagPicker.Text         = string.Empty;
            GameTagPicker.SelectedItem = null;
            if (DataContext is ClipDetailViewModel vm)
                _ = vm.AddGameTagDirectlyAsync(tag);
        }
        else
        {
            _pendingGameTag   = null;
            _gameTagConfirmed = false;
        }
    }

    // ---- General tag picker ----

    /// <summary>
    /// Captures the selected item (or marks it as navigation-only) for the general-tag picker.
    /// </summary>
    private void OnGeneralTagPickerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var tag = GeneralTagPicker.SelectedItem as Tag;
        if (tag is null)
        {
            return;
        }

        _pendingGeneralTag = tag;
        if (_generalTagJustNavByKey) { _generalTagJustNavByKey = false; _generalTagConfirmed = false; }
        else                         { _generalTagConfirmed = true; }
    }

    /// <summary>
    /// Intercepts Up/Down, Enter, Tab, and Escape on the general-tag picker.
    /// </summary>
    private void OnGeneralTagPickerKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
            case Key.Down:
                _generalTagJustNavByKey = true;
                return;

            case Key.Enter:
                if (_pendingGeneralTag is not null)
                {
                    _generalTagConfirmed = true;
                }
                else
                {
                    var t = ResolveTag(GeneralTagPicker, isGame: false);
                    if (t is not null)
                    {
                        _generalTagTabCommitted       = true;
                        GeneralTagPicker.Text         = string.Empty;
                        GeneralTagPicker.SelectedItem = null;
                        if (DataContext is ClipDetailViewModel vm)
                            _ = vm.AddGeneralTagDirectlyAsync(t);
                    }
                }
                return;

            case Key.Escape:
                _generalTagEscapePending = true;
                return;

            case Key.Tab:
                var tag = ResolveTag(GeneralTagPicker, isGame: false);
                if (tag is not null)
                {
                    _generalTagTabCommitted       = true;
                    GeneralTagPicker.Text         = string.Empty;
                    GeneralTagPicker.SelectedItem = null;
                    if (DataContext is ClipDetailViewModel vm2)
                        _ = vm2.AddGeneralTagDirectlyAsync(tag);
                }
                return;
        }
    }

    /// <summary>
    /// Commits the pending general tag when <c>_generalTagConfirmed</c> is true.
    /// </summary>
    private void OnGeneralTagPickerDropDownClosed(object? sender, EventArgs e)
    {
        if (_generalTagEscapePending)
        {
            _generalTagEscapePending      = false;
            _pendingGeneralTag            = null;
            _generalTagConfirmed          = false;
            GeneralTagPicker.SelectedItem = null;
            return;
        }

        if (_generalTagTabCommitted)
        {
            _generalTagTabCommitted = false;
            return;
        }

        if (_generalTagConfirmed && _pendingGeneralTag is not null)
        {
            var tag = _pendingGeneralTag;
            _pendingGeneralTag            = null;
            _generalTagConfirmed          = false;
            GeneralTagPicker.Text         = string.Empty;
            GeneralTagPicker.SelectedItem = null;
            if (DataContext is ClipDetailViewModel vm)
                _ = vm.AddGeneralTagDirectlyAsync(tag);
        }
        else
        {
            _pendingGeneralTag   = null;
            _generalTagConfirmed = false;
        }
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

        var source = isGame ? vm.AvailableGameTags : vm.AvailableGeneralTags;

        // Prefer exact match; fall back to first Contains match (same as FilterMode=ContainsOrdinal).
        return source.FirstOrDefault(t => string.Equals(t.Name, text, StringComparison.OrdinalIgnoreCase))
            ?? source.FirstOrDefault(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    // ---- Highlight tag pickers (DataTemplate-hosted, per-instance state) ----

    /// <summary>Holds the state-machine flags for a single highlight AutoCompleteBox instance.</summary>
    private sealed class HighlightPickerState
    {
        public Tag? PendingTag;
        public bool Confirmed;
        public bool JustNavByKey;
        public bool EscapePending;
        public bool TabCommitted;
    }

    private readonly Dictionary<AutoCompleteBox, HighlightPickerState> _highlightPickerStates = new();

    /// <summary>
    /// Called when a highlight-row AutoCompleteBox enters the visual tree (DataTemplate realisation).
    /// Subscribes the three-phase state-machine events on the specific picker instance.
    /// </summary>
    private void OnHighlightTagPickerLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not AutoCompleteBox picker) return;
        if (_highlightPickerStates.ContainsKey(picker)) return;

        _highlightPickerStates[picker] = new HighlightPickerState();

        picker.SelectionChanged += OnHighlightTagPickerSelectionChanged;
        picker.DropDownClosed   += OnHighlightTagPickerDropDownClosed;
        picker.AddHandler(
            InputElement.KeyDownEvent,
            OnHighlightTagPickerKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    /// <summary>
    /// Called when a highlight-row AutoCompleteBox leaves the visual tree.
    /// Cleans up event subscriptions and removes per-instance state.
    /// </summary>
    private void OnHighlightTagPickerUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not AutoCompleteBox picker) return;

        _highlightPickerStates.Remove(picker);

        picker.SelectionChanged -= OnHighlightTagPickerSelectionChanged;
        picker.DropDownClosed   -= OnHighlightTagPickerDropDownClosed;
        picker.RemoveHandler(InputElement.KeyDownEvent, OnHighlightTagPickerKeyDown);
    }

    /// <summary>
    /// Captures the newly selected tag (or marks it as navigation-only) for a highlight picker.
    /// </summary>
    private void OnHighlightTagPickerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not AutoCompleteBox picker) return;
        if (!_highlightPickerStates.TryGetValue(picker, out var state)) return;

        var tag = picker.SelectedItem as Tag;
        if (tag is null)
        {
            // SelectedItem cleared internally; preserve pending state for DropDownClosed.
            return;
        }

        state.PendingTag = tag;
        if (state.JustNavByKey) { state.JustNavByKey = false; state.Confirmed = false; }
        else                    { state.Confirmed = true; }
    }

    /// <summary>
    /// Intercepts Up/Down, Enter, Tab, and Escape for a highlight tag picker.
    /// </summary>
    private void OnHighlightTagPickerKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not AutoCompleteBox picker) return;
        if (!_highlightPickerStates.TryGetValue(picker, out var state)) return;

        switch (e.Key)
        {
            case Key.Up:
            case Key.Down:
                state.JustNavByKey = true;
                return;

            case Key.Enter:
                if (state.PendingTag is not null)
                {
                    state.Confirmed = true;
                    // DropDownClosed will handle the actual commit and focus restoration.
                }
                else
                {
                    var t = ResolveHighlightTag(picker);
                    if (t is not null)
                    {
                        state.TabCommitted        = true;
                        picker.Text               = string.Empty;
                        picker.SelectedItem       = null;
                        CommitHighlightTag(picker, t);
                        picker.Focus();
                    }
                }
                return;

            case Key.Escape:
                state.EscapePending = true;
                return;

            case Key.Tab:
                var tag = ResolveHighlightTag(picker);
                if (tag is not null)
                {
                    state.TabCommitted        = true;
                    picker.Text               = string.Empty;
                    picker.SelectedItem       = null;
                    CommitHighlightTag(picker, tag);
                    // Tab moves focus naturally; no explicit focus() needed.
                }
                return;
        }
    }

    /// <summary>
    /// Commits the pending highlight tag when confirmed, or clears state on escape/abort.
    /// </summary>
    private void OnHighlightTagPickerDropDownClosed(object? sender, EventArgs e)
    {
        if (sender is not AutoCompleteBox picker) return;
        if (!_highlightPickerStates.TryGetValue(picker, out var state)) return;

        if (state.EscapePending)
        {
            state.EscapePending   = false;
            state.PendingTag      = null;
            state.Confirmed       = false;
            picker.SelectedItem   = null;
            picker.Focus();
            return;
        }

        if (state.TabCommitted)
        {
            state.TabCommitted = false;
            return;
        }

        if (state.Confirmed && state.PendingTag is not null)
        {
            var tag = state.PendingTag;
            state.PendingTag      = null;
            state.Confirmed       = false;
            picker.Text           = string.Empty;
            picker.SelectedItem   = null;
            CommitHighlightTag(picker, tag);
            picker.Focus();
        }
        else
        {
            state.PendingTag = null;
            state.Confirmed  = false;
        }
    }

    /// <summary>
    /// Resolves a tag from an AutoCompleteBox inside a highlight row using text-based fallback.
    /// </summary>
    private static Tag? ResolveHighlightTag(AutoCompleteBox picker)
    {
        if (picker.SelectedItem is Tag selected) return selected;

        var text = picker.Text;
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (picker.DataContext is not HighlightViewModel hvm) return null;

        return hvm.AvailableTags.FirstOrDefault(t => string.Equals(t.Name, text, StringComparison.OrdinalIgnoreCase))
            ?? hvm.AvailableTags.FirstOrDefault(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Invokes the highlight view model's direct tag-add method.</summary>
    private static void CommitHighlightTag(AutoCompleteBox picker, Tag tag)
    {
        if (picker.DataContext is HighlightViewModel hvm)
            _ = hvm.AddTagDirectlyAsync(tag);
    }

    // ---- Trim timestamp text boxes ----

    /// <summary>
    /// Commits the trim start value when the user leaves the start TextBox.
    /// </summary>
    private void OnTrimStartLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClipDetailViewModel vm)
            vm.CommitTrimStartCommand.Execute(null);
    }

    /// <summary>
    /// Commits the trim end value when the user leaves the end TextBox.
    /// </summary>
    private void OnTrimEndLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClipDetailViewModel vm)
            vm.CommitTrimEndCommand.Execute(null);
    }

    /// <summary>
    /// Commits the trim start value when the user presses Enter in the start TextBox.
    /// </summary>
    private void OnTrimStartKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ClipDetailViewModel vm)
        {
            vm.CommitTrimStartCommand.Execute(null);
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
            vm.CommitTrimEndCommand.Execute(null);
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

        var startDir = string.IsNullOrWhiteSpace(vm.TrimOutputPath)
            ? null
            : Path.GetDirectoryName(vm.TrimOutputPath);

        var suggestedName = string.IsNullOrWhiteSpace(vm.TrimOutputPath)
            ? "output.mp4"
            : Path.GetFileName(vm.TrimOutputPath);

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
            vm.TrimOutputPath = result.Path.LocalPath;
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
        var width = HighlightHandleCanvas.Bounds.Width;
        if (width <= 0) return;
        var fraction = Math.Clamp(e.GetPosition(HighlightHandleCanvas).X / width, 0.0, 1.0);
        if (_draggingHighlightHandle == "start")
            _vm.SetHighlightStartFromFraction(fraction);
        else
            _vm.SetHighlightEndFromFraction(fraction);
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
        var width = TrimHandleCanvas.Bounds.Width;
        if (width <= 0) return;
        var fraction = Math.Clamp(e.GetPosition(TrimHandleCanvas).X / width, 0.0, 1.0);
        if (_draggingTrimHandle == "start")
            _vm.SetTrimStartFromFraction(fraction);
        else
            _vm.SetTrimEndFromFraction(fraction);
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
            vm.BeginScrub();
    }

    /// <summary>
    /// Notifies the view model that the user has released the slider and seeks to the new position.
    /// Registered via <see cref="InputElement.AddHandler"/> with <c>handledEventsToo: true</c>.
    /// </summary>
    private void OnSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is ClipDetailViewModel vm)
            vm.EndScrub(PositionSlider.Value);
    }
}
