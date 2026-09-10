using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Imports game titles from the launchers installed on this machine, as a preview the user ticks
/// rather than a batch that simply happens.
/// </summary>
/// <remarks>
/// A real Steam library holds hundreds of titles, so importing everything would flood the tag list
/// with games the user has no clips of. Nothing is created without being ticked, and the default
/// ticks are a starting point: real games the library does not already have. Servers, soundtracks
/// and the rest are listed but unticked, saying why - hiding them would be guessing on the user's
/// behalf, which is the standard the rest of the application holds to.
/// </remarks>
public sealed partial class InstalledGameImportViewModel : ViewModelBase
{
    private readonly IEnumerable<IInstalledGameScanner> _scanners;
    private readonly ITagService _tags;

    /// <summary>Gets the games found, in the order the scanners returned them.</summary>
    public ObservableCollection<InstalledGameRowViewModel> Games { get; } = new();

    /// <summary>Gets or sets whether a scan is running.</summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>Gets or sets whether an import is running.</summary>
    [ObservableProperty]
    private bool _isImporting;

    /// <summary>Gets or sets a summary of what the last scan or import did.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Gets or sets whether the panel is open.</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>Gets whether any launcher is installed and readable.</summary>
    public bool AnyLauncherAvailable => _scanners.Any(s => s.IsAvailable);

    /// <summary>Gets the names of the launchers that were found, for display.</summary>
    public string AvailableLaunchersDisplay
    {
        get
        {
            var names = _scanners.Where(s => s.IsAvailable).Select(s => s.Launcher.ToString()).ToList();
            return names.Count == 0 ? "no launchers found" : string.Join(", ", names);
        }
    }

    /// <summary>Gets how many games are ticked.</summary>
    public int SelectedCount => Games.Count(g => g.IsSelected);

    /// <summary>Gets the label on the import button.</summary>
    public string ImportLabel => SelectedCount == 1
        ? "Import 1 game"
        : $"Import {SelectedCount} games";

    /// <summary>Gets whether there is anything to import.</summary>
    public bool CanImport => SelectedCount > 0 && !IsImporting;

    /// <summary>Gets the command that opens the panel and scans.</summary>
    public IAsyncRelayCommand ScanCommand { get; }

    /// <summary>Gets the command that closes the panel without importing.</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>Gets the command that creates a Game tag for every ticked title.</summary>
    public IAsyncRelayCommand ImportCommand { get; }

    /// <summary>Gets the command that ticks every row that is not already in the library.</summary>
    public IRelayCommand SelectAllCommand { get; }

    /// <summary>Gets the command that unticks every row.</summary>
    public IRelayCommand SelectNoneCommand { get; }

    /// <summary>Raised after an import so the games page can reload.</summary>
    public Func<Task>? Imported { get; set; }

    /// <summary>Initialises a new <see cref="InstalledGameImportViewModel"/>.</summary>
    /// <param name="scanners">One scanner per supported launcher.</param>
    /// <param name="tags">The tag service, used to create the Game tags.</param>
    public InstalledGameImportViewModel(IEnumerable<IInstalledGameScanner> scanners, ITagService tags)
    {
        _scanners = scanners;
        _tags     = tags;

        ScanCommand       = new AsyncRelayCommand(ScanAsync);
        CancelCommand     = new RelayCommand(Close);
        ImportCommand     = new AsyncRelayCommand(ImportAsync, () => CanImport);
        SelectAllCommand  = new RelayCommand(SelectAll);
        SelectNoneCommand = new RelayCommand(SelectNone);
    }

    /// <summary>
    /// Reads every available launcher and builds the preview.
    /// </summary>
    private async Task ScanAsync()
    {
        IsScanning    = true;
        StatusMessage = null;
        Games.Clear();

        try
        {
            var existingNames = await ExistingGameNamesAsync();
            var found = 0;

            foreach (var scanner in _scanners.Where(s => s.IsAvailable))
            {
                foreach (var game in await scanner.ScanAsync())
                {
                    found++;
                    Games.Add(Track(new InstalledGameRowViewModel(
                        game, existingNames.Contains(game.Name.Trim()))));
                }
            }

            IsOpen = true;
            StatusMessage = found == 0
                ? "No installed games were found."
                : $"Found {found} installed title(s). {SelectedCount} ticked.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Scanning for installed games failed.");
            StatusMessage = "The launcher scan could not be completed.";
        }
        finally
        {
            IsScanning = false;
            RaiseSelectionChanged();
        }
    }

    /// <summary>
    /// Creates a Game tag for each ticked title.
    /// </summary>
    /// <remarks>
    /// A Steam title carries its AppId, so it is created the same way a searched-for game is - with
    /// its cover art already resolved. A title without one falls back to a plain custom game tag.
    /// </remarks>
    private async Task ImportAsync()
    {
        IsImporting = true;
        var created = 0;
        var failed  = 0;

        try
        {
            foreach (var row in Games.Where(g => g.IsSelected).ToList())
            {
                try
                {
                    if (row.Launcher == GameLauncher.Steam
                        && int.TryParse(row.StoreAppId, out var appId))
                    {
                        await _tags.CreateFromSteamAsync(new SteamGame
                        {
                            AppId    = appId,
                            Name     = row.Name,
                            CoverUrl = string.Format(SteamCoverUrlTemplate, appId),
                        });
                    }
                    else
                    {
                        await _tags.CreateCustomGameTagAsync(row.Name);
                    }

                    created++;
                }
                catch (Exception ex)
                {
                    // One bad title should not cost the rest of the import.
                    Log.Warning(ex, "Could not create a Game tag for '{Name}'.", row.Name);
                    failed++;
                }
            }

            StatusMessage = failed == 0
                ? $"Created {created} game tag(s)."
                : $"Created {created} game tag(s), {failed} failed.";

            if (Imported is not null)
                await Imported();

            IsOpen = false;
        }
        finally
        {
            IsImporting = false;
            RaiseSelectionChanged();
        }
    }

    /// <summary>The Steam cover art URL, which is derived from the AppId alone.</summary>
    private const string SteamCoverUrlTemplate =
        "https://cdn.cloudflare.steamstatic.com/steam/apps/{0}/library_600x900.jpg";

    /// <summary>Returns the names of the Game tags the library already has.</summary>
    /// <returns>The names, compared case-insensitively.</returns>
    private async Task<HashSet<string>> ExistingGameNamesAsync()
    {
        var all = await _tags.GetAllAsync();

        return new HashSet<string>(
            all.Where(t => t.Type == TagType.Game).Select(t => t.Name.Trim()),
            StringComparer.CurrentCultureIgnoreCase);
    }

    private void SelectAll()
    {
        // Still skips what is already in the library: ticking those would create duplicates.
        foreach (var row in Games.Where(g => !g.AlreadyInLibrary))
            row.IsSelected = true;

        RaiseSelectionChanged();
    }

    private void SelectNone()
    {
        foreach (var row in Games)
            row.IsSelected = false;

        RaiseSelectionChanged();
    }

    private void Close()
    {
        IsOpen = false;
        Games.Clear();
        StatusMessage = null;
    }

    /// <summary>Watches a row so the counts follow its tick state.</summary>
    /// <param name="row">The row to watch.</param>
    /// <returns>The same row.</returns>
    private InstalledGameRowViewModel Track(InstalledGameRowViewModel row)
    {
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InstalledGameRowViewModel.IsSelected))
                RaiseSelectionChanged();
        };

        return row;
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(ImportLabel));
        OnPropertyChanged(nameof(CanImport));
        ImportCommand.NotifyCanExecuteChanged();
    }
}
