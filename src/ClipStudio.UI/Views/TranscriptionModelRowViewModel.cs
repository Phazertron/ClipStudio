using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.Views;

/// <summary>
/// View model for a single row in the Whisper model table of <see cref="TranscriptionSetupDialog"/>.
/// Tracks download state and exposes commands for selecting and downloading the model.
/// </summary>
public sealed partial class TranscriptionModelRowViewModel : ObservableObject
{
    /// <summary>Gets the display name of the model (e.g. "tiny", "base").</summary>
    public string Name { get; }

    /// <summary>Gets the human-readable file size (e.g. "75 MB").</summary>
    public string SizeDisplay { get; }

    /// <summary>Gets the download URL for the GGML model file.</summary>
    public string Url { get; }

    /// <summary>Gets the absolute local path where the model file is stored after download.</summary>
    public string LocalPath { get; }

    /// <summary>Gets or sets whether the model file exists on disk.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isDownloaded;

    /// <summary>Gets or sets whether this row is the currently selected model.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>Gets or sets whether a download is in progress for this model.</summary>
    [ObservableProperty] private bool _isDownloading;

    /// <summary>Gets or sets the status text shown in the Status column.</summary>
    [ObservableProperty] private string _statusText;

    /// <summary>Gets the command that marks this model as the active selection.</summary>
    public IRelayCommand SelectCommand { get; }

    /// <summary>Gets the command that initiates a download for this model.</summary>
    public IRelayCommand DownloadCommand { get; }

    /// <summary>Gets the command that deletes the local model file.</summary>
    public IRelayCommand UninstallCommand { get; }

    /// <summary>Raised when the user clicks Select on this row.</summary>
    public event Action<TranscriptionModelRowViewModel>? Selected;

    /// <summary>Raised when the user clicks Download on this row.</summary>
    public event Action<TranscriptionModelRowViewModel>? DownloadRequested;

    /// <summary>Raised when the user clicks Uninstall on this row.</summary>
    public event Action<TranscriptionModelRowViewModel>? UninstallRequested;

    /// <summary>
    /// Initialises a new <see cref="TranscriptionModelRowViewModel"/>.
    /// </summary>
    public TranscriptionModelRowViewModel(string name, string sizeDisplay, string url, string localPath)
    {
        Name        = name;
        SizeDisplay = sizeDisplay;
        Url         = url;
        LocalPath   = localPath;

        // Commands must be created before IsDownloaded is set, because the source-generated
        // OnIsDownloadedChanged partial method calls NotifyCanExecuteChanged on all three.
        SelectCommand    = new RelayCommand(() => Selected?.Invoke(this),           () => IsDownloaded);
        DownloadCommand  = new RelayCommand(() => DownloadRequested?.Invoke(this),  () => !IsDownloading && !IsDownloaded);
        UninstallCommand = new RelayCommand(() => UninstallRequested?.Invoke(this), () => IsDownloaded && !IsDownloading);

        IsDownloaded = File.Exists(localPath);
        StatusText   = IsDownloaded ? "Downloaded" : "Not downloaded";
    }

    /// <summary>Refreshes command enabled state when <see cref="IsDownloaded"/> changes.</summary>
    partial void OnIsDownloadedChanged(bool value)
    {
        SelectCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        UninstallCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Refreshes command enabled state when <see cref="IsDownloading"/> changes.</summary>
    partial void OnIsDownloadingChanged(bool value)
    {
        DownloadCommand.NotifyCanExecuteChanged();
        UninstallCommand.NotifyCanExecuteChanged();
    }
}
