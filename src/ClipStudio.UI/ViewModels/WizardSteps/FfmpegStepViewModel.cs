using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels.WizardSteps;

/// <summary>
/// View model for the FFmpeg configuration wizard step.
/// Automatically detects FFmpeg in the bundled folder or the system PATH and falls back
/// to a manual path entry when neither location yields a result.
/// </summary>
public sealed partial class FfmpegStepViewModel : WizardStepViewModel
{
    /// <inheritdoc/>
    public override string Title => "FFmpeg Setup";

    /// <inheritdoc/>
    public override int StepNumber => 3;

    /// <summary>Gets or sets the current FFmpeg detection status.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(NeedsManualPath))]
    [NotifyPropertyChangedFor(nameof(IsChecking))]
    [NotifyPropertyChangedFor(nameof(IsBundled))]
    [NotifyPropertyChangedFor(nameof(IsOnPath))]
    private FfmpegDetectionStatus _detectionStatus = FfmpegDetectionStatus.Checking;

    /// <summary>Gets or sets the folder path of the FFmpeg installation found automatically.</summary>
    [ObservableProperty]
    private string _detectedFolder = string.Empty;

    /// <summary>Gets or sets the folder path entered manually by the user.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResolvedFolder))]
    private string _manualFolder = string.Empty;

    /// <summary>Gets a value indicating whether FFmpeg detection is currently running.</summary>
    public bool IsChecking => DetectionStatus == FfmpegDetectionStatus.Checking;

    /// <summary>Gets a value indicating whether FFmpeg was found automatically (bundled or PATH).</summary>
    public bool IsReady =>
        DetectionStatus == FfmpegDetectionStatus.FoundBundled ||
        DetectionStatus == FfmpegDetectionStatus.FoundOnPath;

    /// <summary>Gets a value indicating whether FFmpeg was found in the bundled folder.</summary>
    public bool IsBundled => DetectionStatus == FfmpegDetectionStatus.FoundBundled;

    /// <summary>Gets a value indicating whether FFmpeg was found on the system PATH.</summary>
    public bool IsOnPath => DetectionStatus == FfmpegDetectionStatus.FoundOnPath;

    /// <summary>Gets a value indicating whether the manual path entry panel should be visible.</summary>
    public bool NeedsManualPath => DetectionStatus == FfmpegDetectionStatus.NotFound;

    /// <summary>
    /// Gets the effective FFmpeg folder to apply to settings:
    /// the auto-detected folder when detection succeeded, or the manually entered path otherwise.
    /// </summary>
    public string ResolvedFolder =>
        IsReady ? DetectedFolder : ManualFolder.Trim();

    /// <summary>Gets the command that opens a folder picker for locating FFmpeg binaries (handled in code-behind).</summary>
    public IRelayCommand BrowseCommand { get; }

    /// <summary>Raised when the user clicks the Browse button so the code-behind can open a folder picker.</summary>
    public event Action? BrowseRequested;

    /// <summary>Initialises a new <see cref="FfmpegStepViewModel"/>.</summary>
    public FfmpegStepViewModel()
    {
        BrowseCommand = new RelayCommand(() => BrowseRequested?.Invoke());
    }

    /// <summary>
    /// Scans for FFmpeg binaries in the bundled folder then the system PATH.
    /// Updates <see cref="DetectionStatus"/> and <see cref="DetectedFolder"/> accordingly.
    /// </summary>
    public Task DetectAsync()
    {
        DetectionStatus = FfmpegDetectionStatus.Checking;

        var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        // 1. Bundled alongside the executable
        var bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        if (File.Exists(Path.Combine(bundled, exeName)))
        {
            DetectedFolder  = bundled;
            DetectionStatus = FfmpegDetectionStatus.FoundBundled;
            return Task.CompletedTask;
        }

        // 2. System PATH
        var pathFolder = FindOnPath(exeName);
        if (pathFolder is not null)
        {
            DetectedFolder  = pathFolder;
            DetectionStatus = FfmpegDetectionStatus.FoundOnPath;
            return Task.CompletedTask;
        }

        // 3. Well-known out-of-PATH locations (macOS Homebrew on Apple Silicon and Intel)
        if (OperatingSystem.IsMacOS())
        {
            foreach (var candidate in new[] { "/opt/homebrew/bin", "/usr/local/bin" })
            {
                if (File.Exists(Path.Combine(candidate, exeName)))
                {
                    DetectedFolder  = candidate;
                    DetectionStatus = FfmpegDetectionStatus.FoundOnPath;
                    return Task.CompletedTask;
                }
            }
        }

        // 4. Not found — ask the user
        DetectionStatus = FfmpegDetectionStatus.NotFound;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Searches each directory listed in the PATH environment variable for the specified executable.
    /// Returns the first directory that contains it, or <see langword="null"/> if not found.
    /// </summary>
    /// <param name="exe">Executable filename to search for (e.g. <c>ffmpeg.exe</c>).</param>
    private static string? FindOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            var trimmed = dir.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            try
            {
                var full = Path.Combine(trimmed, exe);
                if (File.Exists(full))
                    return trimmed;
            }
            catch (ArgumentException)
            {
                // Skip malformed PATH entries
            }
        }

        return null;
    }
}
