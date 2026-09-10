using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// The related-clips panel on the clip detail view: what this clip is linked to, and the means to
/// link it to something else.
/// </summary>
/// <remarks>
/// A child of <see cref="ClipDetailViewModel"/> in the pattern established in Phase 0.2: it owns
/// its own state and commands and reaches the surrounding view through
/// <see cref="IRelatedClipsHost"/>, which is deliberately just "which clip is open" and "open a
/// different one".
/// </remarks>
public sealed partial class RelatedClipsViewModel : ViewModelBase
{
    /// <summary>How many search results the link picker offers at once.</summary>
    private const int MaxSearchResults = 30;

    private readonly IRelatedClipsHost _host;
    private readonly IClipLinkService _links;
    private readonly IClipService _clips;
    private readonly IMediaAssetProvider? _assets;

    /// <summary>Gets the clips linked to the open one.</summary>
    public ObservableCollection<RelatedClipRowViewModel> Related { get; } = new();

    /// <summary>Gets the candidate clips the link picker is offering.</summary>
    public ObservableCollection<ClipSearchResultViewModel> SearchResults { get; } = new();

    /// <summary>Gets the link types the picker offers, in menu order.</summary>
    public static IReadOnlyList<ClipLinkTypeOption> LinkTypeOptions { get; } =
    [
        new(ClipLinkType.SameMoment, "Same moment", "The same moment from another angle or player"),
        new(ClipLinkType.Sequel,     "Sequel",      "What happened next"),
        new(ClipLinkType.Reaction,   "Reaction",    "Someone reacting to this clip"),
        new(ClipLinkType.Variant,    "Variant",     "Another cut of the same footage"),
    ];

    /// <summary>Gets or sets whether the link picker is open.</summary>
    [ObservableProperty]
    private bool _isPickerOpen;

    /// <summary>Gets or sets the text typed into the link picker's search box.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Gets or sets the link type the picker will create.</summary>
    [ObservableProperty]
    private ClipLinkTypeOption _selectedLinkType = LinkTypeOptions[0];

    /// <summary>Gets or sets the note to attach to the link being created.</summary>
    [ObservableProperty]
    private string _newLinkNote = string.Empty;

    /// <summary>Gets or sets whether the panel is loading or writing.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Gets or sets an error to show in the panel, if anything went wrong.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Gets whether the open clip has no links.</summary>
    public bool HasNoLinks => Related.Count == 0;

    /// <summary>Gets the command that opens or closes the link picker.</summary>
    public IRelayCommand TogglePickerCommand { get; }

    /// <summary>Gets the command that searches for a clip to link to.</summary>
    public IAsyncRelayCommand SearchCommand { get; }

    /// <summary>Gets the command that opens a related clip.</summary>
    public IRelayCommand<RelatedClipRowViewModel> OpenCommand { get; }

    /// <summary>Gets the command that removes a link.</summary>
    public IAsyncRelayCommand<RelatedClipRowViewModel> UnlinkCommand { get; }

    /// <summary>Gets the command that links the open clip to a search result.</summary>
    public IAsyncRelayCommand<ClipSearchResultViewModel> LinkToCommand { get; }

    /// <summary>Initialises a new <see cref="RelatedClipsViewModel"/>.</summary>
    /// <param name="host">The clip detail view hosting this panel.</param>
    /// <param name="links">The link service.</param>
    /// <param name="clips">The clip service, used to search for something to link to.</param>
    /// <param name="assets">Produces a missing thumbnail on demand. Null in tests.</param>
    public RelatedClipsViewModel(
        IRelatedClipsHost host,
        IClipLinkService links,
        IClipService clips,
        IMediaAssetProvider? assets = null)
    {
        _host   = host;
        _links  = links;
        _clips  = clips;
        _assets = assets;

        TogglePickerCommand = new RelayCommand(TogglePicker);
        SearchCommand       = new AsyncRelayCommand(SearchAsync);
        OpenCommand         = new RelayCommand<RelatedClipRowViewModel>(Open);
        UnlinkCommand       = new AsyncRelayCommand<RelatedClipRowViewModel>(UnlinkAsync);
        LinkToCommand       = new AsyncRelayCommand<ClipSearchResultViewModel>(LinkToAsync);

        Related.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoLinks));
    }

    /// <summary>
    /// Reloads the panel for whichever clip is open.
    /// </summary>
    public async Task LoadAsync()
    {
        Related.Clear();
        ErrorMessage = null;

        if (_host.OpenClipId is not { } clipId) return;

        IsBusy = true;
        try
        {
            foreach (var related in await _links.GetRelatedAsync(clipId))
            {
                var row = new RelatedClipRowViewModel(related, _assets);
                Related.Add(row);
                _ = row.LoadThumbnailAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load related clips for {ClipId}.", clipId);
            ErrorMessage = "The related clips could not be loaded.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens the picker pre-filled to link the open clip to a particular one.
    /// </summary>
    /// <param name="clipId">The clip to offer as the link target.</param>
    /// <param name="linkType">The relationship to preselect.</param>
    /// <remarks>
    /// This is the entry point the duplicate dialog uses for "import and link": the clip is already
    /// known, so the user should not have to search for what they just told the application about.
    /// </remarks>
    public async Task OpenPickerForAsync(int clipId, ClipLinkType linkType = ClipLinkType.Variant)
    {
        SelectedLinkType = LinkTypeOptions.FirstOrDefault(o => o.Type == linkType) ?? LinkTypeOptions[0];
        IsPickerOpen     = true;
        SearchResults.Clear();

        var clip = await _clips.GetByIdAsync(clipId);
        if (clip is not null)
            SearchResults.Add(new ClipSearchResultViewModel(clip));
    }

    private void TogglePicker()
    {
        IsPickerOpen = !IsPickerOpen;

        if (!IsPickerOpen)
        {
            SearchText  = string.Empty;
            NewLinkNote = string.Empty;
            SearchResults.Clear();
        }
    }

    private async Task SearchAsync()
    {
        SearchResults.Clear();
        ErrorMessage = null;

        var term = SearchText.Trim();
        if (term.Length == 0) return;

        IsBusy = true;
        try
        {
            var matches = await _clips.SearchAsync(new ClipSearchQuery { SearchText = term });

            foreach (var clip in matches.Where(NotAlreadyRelated).Take(MaxSearchResults))
                SearchResults.Add(new ClipSearchResultViewModel(clip));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Searching for a clip to link failed.");
            ErrorMessage = "The search could not be completed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Decides whether a search result is worth offering.
    /// </summary>
    /// <param name="clip">The candidate.</param>
    /// <returns>Whether it should appear in the picker.</returns>
    /// <remarks>
    /// The open clip is excluded because a clip cannot link to itself, and clips it is already
    /// linked to are excluded because choosing one would be a no-op the user could not see the
    /// result of.
    /// </remarks>
    private bool NotAlreadyRelated(Clip clip)
        => clip.Id != _host.OpenClipId
           && Related.All(r => r.ClipId != clip.Id);

    private void Open(RelatedClipRowViewModel? row)
    {
        if (row is null) return;
        _host.OpenClip(row.ClipId);
    }

    private async Task UnlinkAsync(RelatedClipRowViewModel? row)
    {
        if (row is null) return;

        try
        {
            await _links.UnlinkAsync(row.LinkId);
            Related.Remove(row);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Removing link {LinkId} failed.", row.LinkId);
            ErrorMessage = "The link could not be removed.";
        }
    }

    private async Task LinkToAsync(ClipSearchResultViewModel? result)
    {
        if (result is null || _host.OpenClipId is not { } clipId) return;

        IsBusy = true;
        try
        {
            await _links.LinkAsync(clipId, result.ClipId, SelectedLinkType.Type, NewLinkNote);

            SearchText   = string.Empty;
            NewLinkNote  = string.Empty;
            IsPickerOpen = false;
            SearchResults.Clear();

            await LoadAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Linking clip {ClipId} to {TargetId} failed.", clipId, result.ClipId);
            ErrorMessage = "The link could not be created.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
