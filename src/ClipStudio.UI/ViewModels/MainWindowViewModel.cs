using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClipStudio.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Microsoft.Extensions.DependencyInjection;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Top-level view model for the application's main window.
/// Owns the navigation model (sidebar items + current page) and manages the lifecycle
/// of the <see cref="ClipDetailViewModel"/> — including DI scope creation and disposal.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly ILibraryWatcherService _watcher;
    private readonly IClipService _clipService;
    private readonly IServiceProvider _serviceProvider;

    private LibraryViewModel? _library;
    private UnreviewedQueueViewModel? _unreviewedQueue;

    private ViewModelBase? _previousPage;
    private IServiceScope? _detailScope;
    private ClipDetailViewModel? _currentDetailVm;

    // References to specific nav items for badge and scanning state updates
    private NavigationItemViewModel? _libraryNavItem;
    private NavigationItemViewModel? _unreviewedNavItem;

    /// <summary>
    /// Gets the sidebar navigation items. Each item pairs a label and icon with a page view model.
    /// </summary>
    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    /// <summary>Gets or sets the navigation item that is currently highlighted in the sidebar.</summary>
    [ObservableProperty]
    private NavigationItemViewModel? _selectedNavigationItem;

    /// <summary>Gets or sets the view model of the page currently shown in the content area.</summary>
    [ObservableProperty]
    private ViewModelBase? _currentPage;

    /// <summary>
    /// Gets the number of unreviewed clips; drives the badge shown on the Unreviewed nav icon.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnreviewedClips))]
    private int _unreviewedCount;

    /// <summary>
    /// Gets or sets whether the application is still performing its startup initialisation.
    /// When true a loading overlay is shown over the content area.
    /// </summary>
    [ObservableProperty] private bool _isStartupLoading;

    /// <summary>Gets or sets a short description of the current startup step shown in the loading overlay.</summary>
    [ObservableProperty] private string _startupStatusText = string.Empty;

    /// <summary>Gets a value indicating whether there is at least one unreviewed clip.</summary>
    public bool HasUnreviewedClips => UnreviewedCount > 0;

    /// <summary>
    /// Initialises a new <see cref="MainWindowViewModel"/> and sets up navigation.
    /// </summary>
    /// <param name="library">The library page view model.</param>
    /// <param name="unreviewedQueue">The unreviewed-queue page view model.</param>
    /// <param name="tagManager">The tag manager page view model.</param>
    /// <param name="games">The games page view model.</param>
    /// <param name="players">The players page view model.</param>
    /// <param name="settings">The settings page view model.</param>
    /// <param name="exportQueue">The export queue page view model.</param>
    /// <param name="highlights">The highlights page view model.</param>
    /// <param name="trash">The trash page view model.</param>
    /// <param name="watcher">The library watcher service.</param>
    /// <param name="clipService">The clip service used to refresh the unreviewed count.</param>
    /// <param name="serviceProvider">The root DI service provider, used to create scoped detail VMs.</param>
    public MainWindowViewModel(
        LibraryViewModel library,
        UnreviewedQueueViewModel unreviewedQueue,
        TagManagerViewModel tagManager,
        GamesViewModel games,
        PlayersViewModel players,
        SettingsViewModel settings,
        ExportQueueViewModel exportQueue,
        HighlightsPageViewModel highlights,
        TrashPageViewModel trash,
        ILibraryWatcherService watcher,
        IClipService clipService,
        IServiceProvider serviceProvider)
    {
        _watcher         = watcher;
        _clipService     = clipService;
        _serviceProvider = serviceProvider;
        _library         = library;
        _unreviewedQueue = unreviewedQueue;

        // Wire navigation callbacks so child VMs can request clip-open with sequence context.
        library.ClipOpenRequested         = (id, seq, idx) => OpenClipDetail(id, seq, idx);
        unreviewedQueue.ClipOpenRequested = (id, seq, idx) => OpenClipDetail(id, seq, idx);

        // Wire highlights watch mode callback (includes sequence for prev/next navigation).
        highlights.HighlightWatchRequested = (clipId, start, end, sequence, idx) =>
            OpenHighlightWatchMode(clipId, start, end, sequence, idx);

        // Wire unreviewed-count refresh so Settings archive/wipe and queue bulk actions update the badge.
        settings.UnreviewedCountRefreshRequested      = () => _ = RefreshUnreviewedCountAsync();
        unreviewedQueue.UnreviewedCountRefreshRequested = () => _ = RefreshUnreviewedCountAsync();

        _libraryNavItem    = new NavigationItemViewModel("Library",    MaterialIconKind.LibraryMovie,    library);
        _unreviewedNavItem = new NavigationItemViewModel("Unreviewed", MaterialIconKind.InboxArrowDown,  unreviewedQueue);

        NavigationItems = new ObservableCollection<NavigationItemViewModel>
        {
            _libraryNavItem,
            _unreviewedNavItem,
            new NavigationItemViewModel("Highlights", MaterialIconKind.BookmarkMultiple, highlights),
            new NavigationItemViewModel("Tags",       MaterialIconKind.TagMultiple,      tagManager),
            new NavigationItemViewModel("Games",      MaterialIconKind.GamepadVariant,   games),
            new NavigationItemViewModel("Players",    MaterialIconKind.AccountMultiple,  players),
            new NavigationItemViewModel("Export",     MaterialIconKind.FileExport,       exportQueue),
            new NavigationItemViewModel("Trash",      MaterialIconKind.TrashCan,         trash),
            new NavigationItemViewModel("Settings",   MaterialIconKind.CogBox,           settings),
        };

        SelectedNavigationItem = NavigationItems[0];
        CurrentPage = NavigationItems[0].Page;

        _watcher.FileDetected += OnFileDetected;

        _ = RefreshUnreviewedCountAsync();
    }

    // ---- Navigation ----

    /// <summary>
    /// Responds to sidebar selection changes by updating <see cref="CurrentPage"/>
    /// and triggering a reload on the newly visible page.
    /// If a detail view is open, closing it first to return to the previously selected nav page.
    /// </summary>
    partial void OnSelectedNavigationItemChanged(NavigationItemViewModel? value)
    {
        if (value is null)
            return;

        // If a detail view is active, close it cleanly
        if (_currentDetailVm is not null)
            CloseClipDetail();

        CurrentPage = value.Page;

        switch (value.Page)
        {
            // Every page view fires LoadCommand.Execute(null) from its OnAttachedToVisualTree
            // handler when Avalonia creates and attaches the new view instance to the visual tree.
            // By calling LoadCommand.Execute here first, IsRunning is already true by the time
            // the view fires its own call, so the second call is silently dropped (CanExecute=false).
            // This guarantees exactly one LoadAsync per navigation and prevents two concurrent
            // operations from sharing the same root-scope DbContext.
            case LibraryViewModel lvm:
                lvm.LoadCommand.Execute(null);
                break;
            case UnreviewedQueueViewModel uvm:
                uvm.LoadCommand.Execute(null);
                break;
            case TagManagerViewModel tvm:
                tvm.LoadCommand.Execute(null);
                break;
            case GamesViewModel gvm:
                gvm.LoadCommand.Execute(null);
                break;
            case PlayersViewModel pvm:
                pvm.LoadCommand.Execute(null);
                break;
            case ExportQueueViewModel evm:
                evm.LoadCommand.Execute(null);
                break;
            case SettingsViewModel svm:
                svm.LoadCommand.Execute(null);
                break;
            case HighlightsPageViewModel hvm:
                hvm.LoadCommand.Execute(null);
                break;
            case TrashPageViewModel tpvm:
                tpvm.LoadCommand.Execute(null);
                break;
        }
    }

    /// <summary>
    /// Opens the clip detail / player view for the given clip.
    /// Creates a new DI scope so that all scoped services (DbContext etc.) are properly isolated.
    /// </summary>
    /// <param name="clipId">Database identifier of the clip to open.</param>
    /// <param name="sequence">
    /// Optional ordered list of clip IDs in the current view, enabling previous/next navigation.
    /// </param>
    /// <param name="sequenceIndex">The index of <paramref name="clipId"/> within <paramref name="sequence"/>.</param>
    public void OpenClipDetail(int clipId, IReadOnlyList<int>? sequence = null, int sequenceIndex = -1)
    {
        // Preserve loop state so it persists when navigating between clips
        var preserveLoopMode = _currentDetailVm?.LoopMode ?? LoopMode.Off;

        // Dispose any currently open detail view
        CloseClipDetail();

        _previousPage = CurrentPage;

        _detailScope     = _serviceProvider.CreateScope();
        _currentDetailVm = _detailScope.ServiceProvider.GetRequiredService<ClipDetailViewModel>();
        _currentDetailVm.BackRequested = CloseClipDetail;

        // Wire sequence navigation when a clip list is provided
        if (sequence is not null && sequenceIndex >= 0)
        {
            _currentDetailVm.HasPrevious = sequenceIndex > 0;
            _currentDetailVm.HasNext     = sequenceIndex < sequence.Count - 1;

            _currentDetailVm.PreviousClipRequested = () =>
            {
                if (sequenceIndex > 0)
                    OpenClipDetail(sequence[sequenceIndex - 1], sequence, sequenceIndex - 1);
            };

            _currentDetailVm.NextClipRequested = () =>
            {
                if (sequenceIndex < sequence.Count - 1)
                    OpenClipDetail(sequence[sequenceIndex + 1], sequence, sequenceIndex + 1);
            };
        }

        _currentDetailVm.LoopMode = preserveLoopMode;

        // Refresh unreviewed badge whenever clip status changes (mark reviewed, trash).
        _currentDetailVm.ClipStatusChanged = () => _ = RefreshUnreviewedCountAsync();

        // Propagate renames back to the library and unreviewed-queue card rows instantly.
        _currentDetailVm.ClipRenamed = (id, newName) =>
        {
            var libraryCard = _library?.Clips.FirstOrDefault(c => c.ClipId == id);
            if (libraryCard is not null) libraryCard.FileName = newName;

            var queueCard = _unreviewedQueue?.Clips.FirstOrDefault(c => c.ClipId == id);
            if (queueCard is not null) queueCard.FileName = newName;
        };

        CurrentPage = _currentDetailVm;

        // Defer LoadAsync until after the layout pass so that the VideoView can attach its
        // window handle to the new MediaPlayer before playback begins. Without this deferral,
        // LibVLC opens a standalone window on Windows when Play() is called without an HWND.
        var vmToLoad = _currentDetailVm;
        Dispatcher.UIThread.Post(
            () => _ = vmToLoad.LoadAsync(clipId),
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Opens the clip detail / player view in watch-only mode, constraining playback to the
    /// given highlight time range. The right-side editing panel is hidden and playback loops
    /// between <paramref name="start"/> and <paramref name="end"/> automatically.
    /// An "Open Clip" button inside the watch view calls <see cref="OpenClipDetail"/> to load
    /// the full clip.
    /// </summary>
    /// <param name="clipId">Database identifier of the clip whose highlight will be watched.</param>
    /// <param name="start">Highlight start time within the clip.</param>
    /// <param name="end">Highlight end time within the clip.</param>
    /// <param name="sequence">
    /// The ordered list of all visible highlight rows, enabling previous/next navigation.
    /// Null when watch mode is opened from a context without a list (e.g. the clip editor).
    /// </param>
    /// <param name="sequenceIndex">The index of the current highlight within <paramref name="sequence"/>.</param>
    public void OpenHighlightWatchMode(int clipId, TimeSpan start, TimeSpan end,
                                       IReadOnlyList<HighlightRowViewModel>? sequence = null,
                                       int sequenceIndex = -1)
    {
        CloseClipDetail();
        _previousPage = CurrentPage;

        _detailScope     = _serviceProvider.CreateScope();
        _currentDetailVm = _detailScope.ServiceProvider.GetRequiredService<ClipDetailViewModel>();

        _currentDetailVm.IsWatchMode         = true;
        _currentDetailVm.WatchStart          = start;
        _currentDetailVm.WatchEnd            = end;

        var currentRow = (sequence is not null && sequenceIndex >= 0) ? sequence[sequenceIndex] : null;
        _currentDetailVm.WatchHighlightLabel      = currentRow?.Label;
        _currentDetailVm.WatchHighlightId         = currentRow?.HighlightId;
        _currentDetailVm.WatchHighlightRating     = currentRow?.Rating ?? 0;
        _currentDetailVm.WatchHighlightIsFavorite = currentRow?.IsFavorite ?? false;

        _currentDetailVm.LoopMode            = LoopMode.LoopThis;   // default to looping the highlight
        _currentDetailVm.BackRequested       = CloseClipDetail;

        // Wire sequence navigation when a highlight list is provided.
        if (sequence is not null && sequenceIndex >= 0)
        {
            _currentDetailVm.HasPreviousHighlight = sequenceIndex > 0;
            _currentDetailVm.HasNextHighlight     = sequenceIndex < sequence.Count - 1;

            _currentDetailVm.PreviousHighlightRequested = () =>
            {
                if (sequenceIndex > 0)
                {
                    var prev = sequence[sequenceIndex - 1];
                    OpenHighlightWatchMode(prev.ClipId, prev.StartTime, prev.EndTime, sequence, sequenceIndex - 1);
                }
            };

            _currentDetailVm.NextHighlightRequested = () =>
            {
                if (sequenceIndex < sequence.Count - 1)
                {
                    var next = sequence[sequenceIndex + 1];
                    OpenHighlightWatchMode(next.ClipId, next.StartTime, next.EndTime, sequence, sequenceIndex + 1);
                }
            };
        }

        // "Open Clip" button inside the watch view navigates to the full clip.
        _currentDetailVm.OpenOriginalClipCommand = new RelayCommand(() => OpenClipDetail(clipId));

        CurrentPage = _currentDetailVm;

        // Defer LoadAsync until after the layout pass so the VideoView attaches its HWND first.
        var vmToLoad = _currentDetailVm;
        Dispatcher.UIThread.Post(
            () => _ = vmToLoad.LoadAsync(clipId),
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Closes the active clip detail view, disposes the DI scope, and restores the previous page.
    /// The dispose of the VM and scope is deferred until after Avalonia has detached the VideoView
    /// from the visual tree to avoid a native LibVLC crash.
    /// </summary>
    public void CloseClipDetail()
    {
        if (_currentDetailVm is null)
            return;

        var vmToDispose    = _currentDetailVm;
        var scopeToDispose = _detailScope;

        // Stop playback and clear media reference BEFORE changing CurrentPage,
        // so the VideoView can safely detach while the player is still alive.
        vmToDispose.PrepareForClose();

        _currentDetailVm = null;
        _detailScope     = null;
        CurrentPage      = _previousPage;
        _previousPage    = null;

        // Reload the previous page so that any changes made in the detail view
        // (e.g. trashing a clip, editing tags, renaming) are reflected immediately.
        // Use LoadCommand.Execute so that when the newly created view instance attaches to
        // the visual tree and fires its own LoadCommand.Execute(null), the second call is
        // blocked by CanExecute=false (IsRunning=true), preventing a concurrent DbContext access.
        switch (CurrentPage)
        {
            case LibraryViewModel lvm:
                lvm.LoadCommand.Execute(null);
                break;
            case UnreviewedQueueViewModel uvm:
                uvm.LoadCommand.Execute(null);
                break;
        }

        // Defer the actual dispose until after Avalonia finishes the visual-tree teardown.
        Dispatcher.UIThread.Post(() =>
        {
            vmToDispose.Dispose();
            scopeToDispose?.Dispose();
        });
    }

    // ---- Startup pre-load ----

    /// <summary>
    /// Loads the library in the background during application startup to populate the clip grid
    /// and warm up EF Core before the startup overlay is dismissed.
    /// Called from <see cref="App"/> after the database and video engine are ready.
    /// </summary>
    public Task PreloadLibraryAsync() => _library?.LoadAsync() ?? Task.CompletedTask;

    // ---- Event handlers ----

    private void OnFileDetected(object? sender, FileDetectedEventArgs e)
    {
        if (_libraryNavItem is not null)
            _libraryNavItem.IsScanning = true;
        _ = RefreshUnreviewedCountAsync();
    }

    private async Task RefreshUnreviewedCountAsync()
    {
        try
        {
            var clips = await _clipService.GetUnreviewedAsync();
            UnreviewedCount = clips.Count;

            if (_unreviewedNavItem is not null)
                _unreviewedNavItem.BadgeCount = clips.Count;
        }
        catch
        {
            // Non-fatal: count will be refreshed once the database is ready.
        }
        finally
        {
            if (_libraryNavItem is not null)
                _libraryNavItem.IsScanning = false;
        }
    }
}
