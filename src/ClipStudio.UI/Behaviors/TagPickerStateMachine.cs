using Avalonia.Input;
using ClipStudio.Core.Entities;

namespace ClipStudio.UI.Behaviors;

/// <summary>
/// The three-phase commit state machine shared by every tag <c>AutoCompleteBox</c> in the app.
/// </summary>
/// <remarks>
/// Avalonia's <c>AutoCompleteBox</c> clears <c>SelectedItem</c> BEFORE it raises
/// <c>DropDownClosed</c>, so the chosen tag has to be captured while the selection is still
/// readable and committed later. That splits a commit across three notifications:
/// <list type="bullet">
///   <item>
///     <description>
///     <see cref="NotifySelectionChanged"/> captures the selection into a pending slot. It marks
///     that pending tag as confirmed only when the change did NOT come from arrow-key navigation,
///     because moving the highlight through the list must not commit on its own - the user still
///     has to press Enter or click.
///     </description>
///   </item>
///   <item>
///     <description>
///     <see cref="NotifyKeyDown"/> flags arrow navigation, confirms on Enter (falling back to
///     resolving the typed text when nothing is selected), commits immediately on Tab, and
///     records an Escape so the following close discards instead of commits.
///     </description>
///   </item>
///   <item>
///     <description>
///     <see cref="NotifyDropDownClosed"/> performs the actual commit, which covers both the click
///     path (confirmed in <see cref="NotifySelectionChanged"/>) and the Enter path (confirmed in
///     <see cref="NotifyKeyDown"/>).
///     </description>
///   </item>
/// </list>
/// One instance drives exactly one picker; it holds that picker's state.
/// </remarks>
public class TagPickerStateMachine
{
    private readonly ITagPickerHost _host;
    private readonly bool _refocusAfterCommit;

    private Tag? _pendingTag;
    private bool _confirmed;
    private bool _justNavByKey;
    private bool _escapePending;
    private bool _tabCommitted;

    /// <summary>Initialises a new <see cref="TagPickerStateMachine"/>.</summary>
    /// <param name="host">The picker surface this machine drives.</param>
    /// <param name="refocusAfterCommit">
    /// When <c>true</c>, focus is returned to the picker after a commit and after an Escape, so the
    /// user can keep adding tags without reaching for the mouse. Pickers that sit in a form the user
    /// tabs out of pass <c>false</c>.
    /// </param>
    public TagPickerStateMachine(ITagPickerHost host, bool refocusAfterCommit = false)
    {
        _host               = host;
        _refocusAfterCommit = refocusAfterCommit;
    }

    /// <summary>
    /// Captures the newly selected tag, or ignores the change when Avalonia cleared the selection
    /// internally. A selection caused by arrow-key navigation is captured but left unconfirmed.
    /// </summary>
    public virtual void NotifySelectionChanged()
    {
        // Read the selection directly - the event's AddedItems may carry display strings, not tags.
        var tag = _host.SelectedTag;

        // A null selection is Avalonia clearing the item as it closes. Keep the pending tag so
        // NotifyDropDownClosed can still commit or discard it.
        if (tag is null) return;

        _pendingTag = tag;

        if (_justNavByKey)
        {
            _justNavByKey = false;
            _confirmed    = false;
        }
        else
        {
            _confirmed = true;
        }
    }

    /// <summary>
    /// Handles the keys that take part in a commit. Every other key is left alone.
    /// </summary>
    /// <param name="key">The key that was pressed.</param>
    public virtual void NotifyKeyDown(Key key)
    {
        switch (key)
        {
            case Key.Up:
            case Key.Down:
                // Tell NotifySelectionChanged that the selection about to change is navigation.
                _justNavByKey = true;
                return;

            case Key.Enter:
                if (_pendingTag is not null)
                {
                    // The close that follows performs the commit.
                    _confirmed = true;
                }
                else
                {
                    // Enter keeps the user in the picker so they can type the next tag.
                    CommitResolved(refocus: _refocusAfterCommit);
                }
                return;

            case Key.Escape:
                _escapePending = true;
                return;

            case Key.Tab:
                // Committed here rather than on close so the tag lands before focus leaves.
                // The event is deliberately left unhandled so Tab still moves focus, which raises
                // DropDownClosed and clears the guard flag. Focus is never taken back here - that
                // would fight the focus move Tab is being pressed for.
                CommitResolved(refocus: false);
                return;
        }
    }

    /// <summary>
    /// Commits the pending tag when it was confirmed, and otherwise discards it. Escape and the
    /// Tab commit short-circuit through their guard flags.
    /// </summary>
    public virtual void NotifyDropDownClosed()
    {
        if (_escapePending)
        {
            _escapePending    = false;
            _pendingTag       = null;
            _confirmed        = false;
            _host.SelectedTag = null;
            if (_refocusAfterCommit) _host.Focus();
            return;
        }

        if (_tabCommitted)
        {
            _tabCommitted = false;
            return;
        }

        if (_confirmed && _pendingTag is not null)
        {
            var tag     = _pendingTag;
            _pendingTag = null;
            _confirmed  = false;
            Apply(tag, _refocusAfterCommit);
        }
        else
        {
            _pendingTag = null;
            _confirmed  = false;
        }
    }

    /// <summary>
    /// Resolves a tag from the current selection or typed text and commits it immediately,
    /// guarding the following close so it does not commit a second time.
    /// </summary>
    /// <param name="refocus">Whether focus should be returned to the picker after the commit.</param>
    private void CommitResolved(bool refocus)
    {
        var tag = _host.ResolveTag();
        if (tag is null) return;

        _tabCommitted = true;
        Apply(tag, refocus);
    }

    /// <summary>Clears the picker and hands the tag to the host.</summary>
    /// <param name="tag">The tag to apply.</param>
    /// <param name="refocus">Whether focus should be returned to the picker afterwards.</param>
    private void Apply(Tag tag, bool refocus)
    {
        _host.Text        = string.Empty;
        _host.SelectedTag = null;
        _host.Commit(tag);
        if (refocus) _host.Focus();
    }
}
