using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using ClipStudio.Core.Entities;

namespace ClipStudio.UI.Behaviors;

/// <summary>
/// Attaches a <see cref="TagPickerStateMachine"/> to a single tag <c>AutoCompleteBox</c>, translating
/// the control's events into the machine's notifications and the machine's requests back onto the
/// control.
/// </summary>
/// <remarks>
/// The two things that differ between pickers - where the available tags come from and what a
/// committed tag is applied to - are supplied as delegates, so game, general and per-highlight
/// pickers all share the one state machine. Both delegates receive the picker, which matters for
/// pickers realised from a <c>DataTemplate</c>: their <c>DataContext</c> is only known at commit time.
/// </remarks>
public class TagPickerBehavior : ITagPickerHost
{
    private readonly AutoCompleteBox _picker;
    private readonly Func<AutoCompleteBox, Tag?> _resolve;
    private readonly Action<AutoCompleteBox, Tag> _commit;
    private readonly TagPickerStateMachine _machine;

    private bool _attached;

    /// <summary>Initialises a new <see cref="TagPickerBehavior"/> for one picker.</summary>
    /// <param name="picker">The control to drive.</param>
    /// <param name="resolve">
    /// Resolves the tag to commit, checking the selection first and falling back to the typed text.
    /// </param>
    /// <param name="commit">Applies a resolved tag.</param>
    /// <param name="refocusAfterCommit">
    /// When <c>true</c>, focus returns to the picker after a click or Enter commit, or an Escape.
    /// A Tab commit keeps focus regardless; see <see cref="TagPickerStateMachine"/>.
    /// </param>
    public TagPickerBehavior(
        AutoCompleteBox picker,
        Func<AutoCompleteBox, Tag?> resolve,
        Action<AutoCompleteBox, Tag> commit,
        bool refocusAfterCommit = false)
    {
        _picker  = picker  ?? throw new ArgumentNullException(nameof(picker));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _commit  = commit  ?? throw new ArgumentNullException(nameof(commit));
        _machine = new TagPickerStateMachine(this, refocusAfterCommit);
    }

    /// <summary>Gets the picker this behaviour is attached to.</summary>
    public AutoCompleteBox Picker => _picker;

    /// <summary>
    /// Subscribes to the picker's events. Calling this twice is a no-op, so a control that is
    /// loaded more than once does not end up with duplicate handlers.
    /// </summary>
    public virtual void Attach()
    {
        if (_attached) return;
        _attached = true;

        _picker.SelectionChanged += OnSelectionChanged;
        _picker.DropDownClosed   += OnDropDownClosed;

        // Tunnel, and handledEventsToo, because the AutoCompleteBox template handles Enter, Escape
        // and the arrow keys itself before they would reach a bubbling handler.
        _picker.AddHandler(
            InputElement.KeyDownEvent,
            OnKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    /// <summary>Unsubscribes from the picker's events.</summary>
    public virtual void Detach()
    {
        if (!_attached) return;
        _attached = false;

        _picker.SelectionChanged -= OnSelectionChanged;
        _picker.DropDownClosed   -= OnDropDownClosed;
        _picker.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        _machine.NotifySelectionChanged();

    private void OnDropDownClosed(object? sender, EventArgs e) =>
        _machine.NotifyDropDownClosed();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_machine.NotifyKeyDown(e.Key)) return;

        // Marking the event handled is what keeps focus in the picker after a Tab commit.
        e.Handled = true;

        // The state machine has already cleared Text, but this runs in the tunnel phase - before
        // the AutoCompleteBox has finished with the key. Its text-completion bookkeeping tracks
        // what the user actually typed separately from what is displayed, and writing that back
        // afterwards restored the typed prefix: type "a", get the "aesthetic" completion, press
        // Tab, and the field was left holding "a". Reset once the control has finished.
        Dispatcher.UIThread.Post(ResetInput, DispatcherPriority.Background);
    }

    /// <summary>
    /// Returns the picker to an empty, closed state after a commit.
    /// </summary>
    protected virtual void ResetInput()
    {
        _picker.Text           = string.Empty;
        _picker.SelectedItem   = null;
        _picker.IsDropDownOpen = false;

        // Focus the inner text box, not the AutoCompleteBox itself. Focusing the outer control
        // leaves keyboard input going nowhere - the picker looks focused, but typing does not
        // reach the field, because the editable part is a TextBox inside the template.
        var textBox = _picker.FindDescendantOfType<TextBox>();
        if (textBox is not null)
        {
            textBox.Focus();
            textBox.CaretIndex = 0;
        }
        else
        {
            _picker.Focus();
        }
    }

    // ---- ITagPickerHost ----

    /// <inheritdoc />
    Tag? ITagPickerHost.SelectedTag
    {
        get => _picker.SelectedItem as Tag;
        set => _picker.SelectedItem = value;
    }

    /// <inheritdoc />
    string? ITagPickerHost.Text
    {
        get => _picker.Text;
        set => _picker.Text = value;
    }

    /// <inheritdoc />
    Tag? ITagPickerHost.ResolveTag() => _resolve(_picker);

    /// <inheritdoc />
    void ITagPickerHost.Commit(Tag tag) => _commit(_picker, tag);

    /// <inheritdoc />
    void ITagPickerHost.Focus() => _picker.Focus();
}
