using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
// Referenced for the minimum term length, so the flyout and the service cannot disagree on it.
using ClipStudio.Application.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// The grouped search results shown under the library's search box.
/// </summary>
/// <remarks>
/// Deliberately free of Avalonia types. The tag picker's keyboard handling needed three separate
/// fixes to get right, and none of them would have been caught by a compiled binding or a unit
/// test over a view. Keeping the selection and activation logic here means the code-behind is a
/// thin translation of key presses into method calls, and everything that decides behaviour is
/// testable.
/// </remarks>
public sealed partial class SearchFlyoutViewModel : ViewModelBase
{
    /// <summary>How long typing has to stop before a search runs.</summary>
    /// <remarks>
    /// Long enough that typing a word does not run a query per letter, short enough not to feel
    /// laggy. The library reload behind the search box was previously un-debounced, so every
    /// keystroke reloaded the whole grid.
    /// </remarks>
    public static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);

    private readonly ISearchService _search;
    private readonly Func<bool> _captionsEnabled;
    private CancellationTokenSource? _pending;

    /// <summary>Gets the result groups, in the order the service returned them.</summary>
    public ObservableCollection<SearchResultGroupViewModel> Groups { get; } = new();

    /// <summary>
    /// Gets every result across all groups, in display order.
    /// </summary>
    /// <remarks>
    /// Navigation moves through this rather than through the groups, so arrow keys step from the
    /// last clip straight to the first highlight without stopping on a heading.
    /// </remarks>
    public IReadOnlyList<SearchResultRowViewModel> FlatResults { get; private set; } = [];

    /// <summary>Gets or sets the index of the highlighted result, or -1 when none is.</summary>
    [ObservableProperty]
    private int _selectedIndex = -1;

    /// <summary>Moves the highlight onto the row at the new index.</summary>
    /// <param name="value">The newly selected index.</param>
    /// <remarks>
    /// The rows carry the flag rather than the view comparing indices, because the results are
    /// drawn as nested lists and there is no sane way to ask "is this the nth item overall" from
    /// inside one.
    /// </remarks>
    partial void OnSelectedIndexChanged(int value)
    {
        for (var i = 0; i < FlatResults.Count; i++)
            FlatResults[i].IsSelected = i == value;
    }

    /// <summary>Gets or sets whether the flyout is showing.</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>Gets or sets whether a search is running.</summary>
    [ObservableProperty]
    private bool _isSearching;

    /// <summary>Gets or sets whether the last search found nothing.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>Gets the term the last completed search ran for.</summary>
    public string LastTerm { get; private set; } = string.Empty;

    /// <summary>Gets the command that closes the flyout.</summary>
    public IRelayCommand CloseCommand { get; }

    /// <summary>Gets the command that activates a result, whether clicked or chosen with Enter.</summary>
    public IRelayCommand<SearchResultRowViewModel> ActivateCommand { get; }

    /// <summary>Opens a clip, optionally seeking to a position within it.</summary>
    public Action<int, TimeSpan?>? ClipRequested { get; set; }

    /// <summary>Applies a general tag as a library filter.</summary>
    public Action<int>? TagFilterRequested { get; set; }

    /// <summary>Applies a game as a library filter.</summary>
    /// <remarks>
    /// Separate from <see cref="TagFilterRequested"/> because the library keeps game and general
    /// tag filters apart, and a game routed into the general filter would silently match nothing.
    /// </remarks>
    public Action<int>? GameFilterRequested { get; set; }

    /// <summary>Applies a player as a library filter.</summary>
    public Action<int>? PlayerFilterRequested { get; set; }

    /// <summary>Initialises a new <see cref="SearchFlyoutViewModel"/>.</summary>
    /// <param name="search">The search service.</param>
    /// <param name="captionsEnabled">
    /// Reads whether transcripts should be searched too, so the setting is consulted at search time
    /// rather than captured once.
    /// </param>
    public SearchFlyoutViewModel(ISearchService search, Func<bool> captionsEnabled)
    {
        _search          = search;
        _captionsEnabled = captionsEnabled;

        CloseCommand    = new RelayCommand(Close);
        ActivateCommand = new RelayCommand<SearchResultRowViewModel>(row => Activate(row?.Item));
    }

    // ---- Searching ----

    /// <summary>
    /// Runs a search for the given term after the debounce delay, cancelling any pending one.
    /// </summary>
    /// <param name="term">What to search for.</param>
    /// <remarks>
    /// Called on every keystroke. The previous pending search is cancelled rather than allowed to
    /// finish, so results from an older term can never overwrite a newer one.
    /// </remarks>
    public async Task QueueSearchAsync(string term)
    {
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = new CancellationTokenSource();
        var token = _pending.Token;

        if (term.Trim().Length < SearchService.MinimumTermLength)
        {
            Close();
            return;
        }

        try
        {
            await Task.Delay(DebounceDelay, token);
            await SearchAsync(term, token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke.
        }
    }

    /// <summary>Runs a search immediately, without debouncing.</summary>
    /// <param name="term">What to search for.</param>
    /// <param name="token">Cancels the search if a newer one starts.</param>
    public async Task SearchAsync(string term, CancellationToken token = default)
    {
        IsSearching = true;
        try
        {
            var groups = await _search.SearchAsync(term, _captionsEnabled(), token);
            token.ThrowIfCancellationRequested();

            Groups.Clear();
            foreach (var group in groups)
                Groups.Add(SearchResultGroupViewModel.From(group));

            FlatResults   = Groups.SelectMany(g => g.Rows).ToList();
            LastTerm      = term;
            IsEmpty       = FlatResults.Count == 0;
            IsOpen        = true;

            // Nothing is preselected: the first Down should land on the first result, and Enter
            // with nothing chosen should do nothing rather than open whatever happened to be first.
            SelectedIndex = -1;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Search for '{Term}' failed.", term);
            Close();
        }
        finally
        {
            IsSearching = false;
        }
    }

    // ---- Keyboard navigation ----

    /// <summary>Moves the highlight to the next result, wrapping at the end.</summary>
    /// <returns>Whether the key press was used.</returns>
    public bool MoveNext()
    {
        if (!IsOpen || FlatResults.Count == 0) return false;

        SelectedIndex = SelectedIndex + 1 >= FlatResults.Count ? 0 : SelectedIndex + 1;
        return true;
    }

    /// <summary>Moves the highlight to the previous result, wrapping at the start.</summary>
    /// <returns>Whether the key press was used.</returns>
    /// <remarks>
    /// From nothing selected this lands on the last result, so Up is a quick way to reach the
    /// bottom of a short list.
    /// </remarks>
    public bool MovePrevious()
    {
        if (!IsOpen || FlatResults.Count == 0) return false;

        SelectedIndex = SelectedIndex <= 0 ? FlatResults.Count - 1 : SelectedIndex - 1;
        return true;
    }

    /// <summary>Activates the highlighted result.</summary>
    /// <returns>Whether the key press was used.</returns>
    /// <remarks>
    /// With nothing highlighted this does nothing and reports the press unused, so Enter still
    /// reaches the search box and runs an ordinary filter.
    /// </remarks>
    public bool ActivateSelected()
    {
        if (!IsOpen || SelectedIndex < 0 || SelectedIndex >= FlatResults.Count) return false;

        Activate(FlatResults[SelectedIndex].Item);
        return true;
    }

    /// <summary>Closes the flyout in response to Escape.</summary>
    /// <returns>Whether the key press was used.</returns>
    public bool HandleEscape()
    {
        if (!IsOpen) return false;

        Close();
        return true;
    }

    // ---- Activation ----

    /// <summary>
    /// Acts on a result: opens a clip, or applies a tag, game or player as a filter.
    /// </summary>
    /// <param name="item">The result to act on.</param>
    private void Activate(SearchResultItem? item)
    {
        if (item is null) return;

        switch (item.Kind)
        {
            case SearchResultKind.Clip:
            case SearchResultKind.Highlight:
            case SearchResultKind.Caption:
                if (item.ClipId is { } clipId)
                    ClipRequested?.Invoke(clipId, item.SeekTo);
                break;

            case SearchResultKind.Tag:
                if (item.EntityId is { } tagId)
                    TagFilterRequested?.Invoke(tagId);
                break;

            case SearchResultKind.Game:
                if (item.EntityId is { } gameTagId)
                    GameFilterRequested?.Invoke(gameTagId);
                break;

            case SearchResultKind.Player:
                if (item.EntityId is { } playerId)
                    PlayerFilterRequested?.Invoke(playerId);
                break;
        }

        Close();
    }

    /// <summary>Closes the flyout and forgets the results.</summary>
    public void Close()
    {
        _pending?.Cancel();

        IsOpen        = false;
        IsEmpty       = false;
        SelectedIndex = -1;
        Groups.Clear();
        FlatResults = [];
    }
}
