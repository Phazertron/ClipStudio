using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ClipStudio.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model projection for a single <see cref="SourceFolder"/> row in the settings page.
/// Exposes the folder path, its active state, and remove / toggle / scan commands that
/// delegate back to the parent <see cref="SettingsViewModel"/> via constructor callbacks.
/// </summary>
public sealed partial class SourceFolderRowViewModel : ViewModelBase
{
    /// <summary>Gets the unique identifier of the underlying source folder.</summary>
    public int FolderId { get; }

    /// <summary>Gets the absolute path of the watched folder.</summary>
    public string Path { get; }

    /// <summary>Gets or sets the last-scan timestamp formatted for display.</summary>
    [ObservableProperty]
    private string _lastScannedDisplay;

    /// <summary>Gets or sets whether this folder is currently being actively watched.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowOpacity))]
    private bool _isActive;

    /// <summary>
    /// Gets the display opacity for this row.
    /// Active folders are shown at full opacity; inactive (archived) folders are dimmed to signal they are disabled.
    /// </summary>
    public double RowOpacity => IsActive ? 1.0 : 0.45;

    /// <summary>Gets or sets a value indicating whether a scan is running for this folder.</summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>
    /// Gets or sets the scan completion percentage (0–100) while <see cref="IsScanning"/> is true.
    /// Drives the determinate progress bar shown during an active scan.
    /// </summary>
    [ObservableProperty]
    private double _scanProgressPercent;

    /// <summary>
    /// Gets or sets a human-readable status line while a scan is in progress,
    /// e.g. "File 3 / 10 — my_clip.mp4  |  2 imported, 0 failed".
    /// Set to <see langword="null"/> when no scan is running.
    /// </summary>
    [ObservableProperty]
    private string? _scanStatusText;

    /// <summary>Gets or sets a summary line shown after the last scan (e.g. "3 imported, 1 skipped").</summary>
    [ObservableProperty]
    private string? _lastScanSummary;

    /// <summary>Gets the collection of per-file error messages produced by the last scan.</summary>
    public ObservableCollection<string> ScanErrorMessages { get; } = new();

    /// <summary>Gets the command that removes this folder from the watched list.</summary>
    public IAsyncRelayCommand RemoveCommand { get; }

    /// <summary>Gets the command that toggles the active state of this folder.</summary>
    public IAsyncRelayCommand ToggleActiveCommand { get; }

    /// <summary>Gets the command that runs a full scan of this folder to import new clips.</summary>
    public IAsyncRelayCommand ScanCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="SourceFolderRowViewModel"/> from an entity and parent callbacks.
    /// </summary>
    /// <param name="folder">The source folder entity to project.</param>
    /// <param name="onRemove">Callback invoked when the user removes this folder.</param>
    /// <param name="onToggleActive">Callback invoked when the user toggles the active state.</param>
    /// <param name="onScan">Callback invoked when the user requests a manual scan of this folder.</param>
    public SourceFolderRowViewModel(
        SourceFolder folder,
        Func<SourceFolderRowViewModel, Task> onRemove,
        Func<SourceFolderRowViewModel, Task> onToggleActive,
        Func<SourceFolderRowViewModel, Task> onScan)
    {
        FolderId           = folder.Id;
        Path               = folder.Path;
        _isActive          = folder.IsActive;
        _lastScannedDisplay = folder.LastScannedAt.HasValue
            ? folder.LastScannedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "Never";

        RemoveCommand       = new AsyncRelayCommand(() => onRemove(this));
        ToggleActiveCommand = new AsyncRelayCommand(() => onToggleActive(this));
        ScanCommand         = new AsyncRelayCommand(() => onScan(this));
    }
}
