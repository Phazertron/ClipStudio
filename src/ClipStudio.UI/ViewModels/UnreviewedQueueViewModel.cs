using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the Unreviewed queue — the inbox of newly imported clips that have not
/// yet been reviewed and tagged by the user.
/// </summary>
public sealed partial class UnreviewedQueueViewModel : ViewModelBase
{
    private readonly IClipService _clipService;

    /// <summary>Gets the observable collection of unreviewed clip cards.</summary>
    public ObservableCollection<ClipCardViewModel> Clips { get; } = new();

    /// <summary>Gets or sets a value indicating that the queue is currently loading from the database.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Gets the number of unreviewed clips currently in the queue.
    /// Bound to the badge on the sidebar navigation item.
    /// </summary>
    public int UnreviewedCount => Clips.Count;

    /// <summary>Gets the command that loads or reloads the unreviewed clip list.</summary>
    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>
    /// Optional callback set by <see cref="MainWindowViewModel"/> to open a clip's detail view.
    /// Receives the clip ID, the full ordered sequence of clip IDs in the current view,
    /// and the index of the requested clip within that sequence.
    /// </summary>
    public Action<int, IReadOnlyList<int>, int>? ClipOpenRequested { get; set; }

    /// <summary>
    /// Optional callback invoked after bulk status changes (e.g. archive all) so that the main window
    /// can refresh the unreviewed clip count badge immediately.
    /// </summary>
    public Action? UnreviewedCountRefreshRequested { get; set; }

    /// <summary>
    /// Initialises a new <see cref="UnreviewedQueueViewModel"/>.
    /// </summary>
    /// <param name="clipService">Application service used to retrieve unreviewed clips.</param>
    public UnreviewedQueueViewModel(IClipService clipService)
    {
        _clipService = clipService;
        LoadCommand  = new AsyncRelayCommand(LoadAsync);
        Clips.CollectionChanged += (_, _) => OnPropertyChanged(nameof(UnreviewedCount));
    }

    /// <summary>
    /// Requests that the given clip be opened in the detail/player view.
    /// Passes the full current queue sequence so the detail view can offer previous/next navigation.
    /// </summary>
    /// <param name="clipId">Database identifier of the clip to open.</param>
    public void OpenClip(int clipId)
    {
        var sequence = Clips.Select(c => c.ClipId).ToList();
        var index    = sequence.IndexOf(clipId);
        ClipOpenRequested?.Invoke(clipId, sequence, index);
    }

    /// <summary>
    /// Asynchronously loads all unreviewed clips and populates <see cref="Clips"/>.
    /// </summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        Clips.Clear();

        try
        {
            var results = await _clipService.GetUnreviewedAsync();

            foreach (var clip in results)
                Clips.Add(new ClipCardViewModel(clip));

            // Load thumbnails asynchronously so the list appears immediately and images fade in.
            _ = Task.WhenAll(Clips.Select(c => c.LoadThumbnailAsync()));
        }
        finally
        {
            IsLoading = false;
            UnreviewedCountRefreshRequested?.Invoke();
        }
    }
}
