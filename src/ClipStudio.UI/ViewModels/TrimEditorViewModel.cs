using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Enums;
using ClipStudio.UI.Parsing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Owns the trim-and-export form: the trim range, its timeline handles and timestamp boxes, the
/// destructive-trim guard, and queueing the export.
/// </summary>
/// <remarks>
/// The trim range is held as two <see cref="TimeSpan"/> values, with three ways in that must stay
/// in step: dragging a handle, marking at the play head, and typing a timestamp. Each updates the
/// range, refreshes the readout and re-raises the matching fraction so the handle follows.
/// </remarks>
public sealed partial class TrimEditorViewModel : ViewModelBase
{
    private const string InvalidTimeMessage = "Invalid time (use m:ss or h:mm:ss).";

    private readonly ITrimEditorHost _host;
    private readonly IExportService _exportService;
    private readonly ISettingsService _settings;

    private TimeSpan _trimStart = TimeSpan.Zero;
    private TimeSpan _trimEnd = TimeSpan.Zero;

    /// <summary>Gets or sets a value indicating whether the trim and export form is expanded.</summary>
    [ObservableProperty]
    private bool _isTrimming;

    /// <summary>Gets or sets the trim start readout, which the user may also type into.</summary>
    [ObservableProperty]
    private string _trimStartDisplay = "0:00";

    /// <summary>Gets or sets the trim end readout, which the user may also type into.</summary>
    [ObservableProperty]
    private string _trimEndDisplay = "0:00";

    /// <summary>Gets or sets the validation message for the start box, or null when it is valid.</summary>
    [ObservableProperty]
    private string? _trimStartError;

    /// <summary>Gets or sets the validation message for the end box, or null when it is valid.</summary>
    [ObservableProperty]
    private string? _trimEndError;

    /// <summary>Gets or sets where the trimmed file will be written.</summary>
    [ObservableProperty]
    private string _trimOutputPath = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the export re-encodes and replaces the original
    /// rather than writing a separate file.
    /// </summary>
    [ObservableProperty]
    private bool _isTrimDestructive;

    /// <summary>
    /// Gets or sets the confirmation prompt shown when a destructive trim would cut through
    /// existing highlights, or null when there is nothing to confirm.
    /// </summary>
    [ObservableProperty]
    private string? _trimDestructiveWarning;

    /// <summary>Gets where the trim start sits along the timeline, as a fraction from 0 to 1.</summary>
    public double TrimStartFraction =>
        _host.DurationSeconds > 0 ? _trimStart.TotalSeconds / _host.DurationSeconds : 0;

    /// <summary>Gets where the trim end sits along the timeline, as a fraction from 0 to 1.</summary>
    public double TrimEndFraction =>
        _host.DurationSeconds > 0 ? _trimEnd.TotalSeconds / _host.DurationSeconds : 0;

    /// <summary>Gets the command that opens the trim form.</summary>
    public IRelayCommand BeginTrimCommand { get; }

    /// <summary>Gets the command that closes the trim form without queueing anything.</summary>
    public IRelayCommand CancelTrimCommand { get; }

    /// <summary>Gets the command that marks the trim start at the current play head.</summary>
    public IRelayCommand MarkTrimStartCommand { get; }

    /// <summary>Gets the command that marks the trim end at the current play head.</summary>
    public IRelayCommand MarkTrimEndCommand { get; }

    /// <summary>Gets the command that applies the typed trim start.</summary>
    public IRelayCommand CommitTrimStartCommand { get; }

    /// <summary>Gets the command that applies the typed trim end.</summary>
    public IRelayCommand CommitTrimEndCommand { get; }

    /// <summary>Gets the command that queues the trim export.</summary>
    public IAsyncRelayCommand QueueTrimExportCommand { get; }

    /// <summary>Gets the command that queues a destructive trim the user has confirmed.</summary>
    public IAsyncRelayCommand ConfirmDestructiveTrimCommand { get; }

    /// <summary>Initialises a new <see cref="TrimEditorViewModel"/>.</summary>
    /// <param name="host">The surrounding clip detail context.</param>
    /// <param name="exportService">The export job queue.</param>
    /// <param name="settings">Application settings, read for the default trim mode.</param>
    public TrimEditorViewModel(
        ITrimEditorHost host,
        IExportService exportService,
        ISettingsService settings)
    {
        _host          = host;
        _exportService = exportService;
        _settings      = settings;

        BeginTrimCommand              = new RelayCommand(BeginTrim);
        CancelTrimCommand             = new RelayCommand(() => IsTrimming = false);
        MarkTrimStartCommand          = new RelayCommand(MarkTrimStart);
        MarkTrimEndCommand            = new RelayCommand(MarkTrimEnd);
        CommitTrimStartCommand        = new RelayCommand(CommitTrimStart);
        CommitTrimEndCommand          = new RelayCommand(CommitTrimEnd);
        QueueTrimExportCommand        = new AsyncRelayCommand(QueueTrimExportAsync);
        ConfirmDestructiveTrimCommand = new AsyncRelayCommand(ConfirmDestructiveTrimAsync);
    }

    /// <summary>Resets the form for a newly opened clip.</summary>
    public void Reset()
    {
        IsTrimming             = false;
        TrimOutputPath         = string.Empty;
        TrimDestructiveWarning = null;
        TrimStartError         = null;
        TrimEndError           = null;
        _trimStart             = TimeSpan.Zero;
        _trimEnd               = TimeSpan.Zero;
    }

    // ---- Opening the form ----

    /// <summary>
    /// Opens the trim form, seeding the range to the whole clip and the destination to a
    /// "_trimmed" file beside the original.
    /// </summary>
    private void BeginTrim()
    {
        if (!_host.CanBeginTrim) return;

        // Never show the trim handles and the highlight handles at the same time.
        _host.PrepareForTrim();

        var clip = _host.CurrentClip;
        if (clip is not null && string.IsNullOrEmpty(TrimOutputPath))
        {
            var directory = Path.GetDirectoryName(clip.FilePath) ?? string.Empty;
            var baseName  = Path.GetFileNameWithoutExtension(clip.FileName);
            TrimOutputPath = Path.Combine(directory, $"{baseName}_trimmed.mp4");
        }

        IsTrimDestructive = _settings.Current.DefaultTrimMode == TrimMode.Destructive;
        _trimStart        = TimeSpan.Zero;
        _trimEnd          = clip?.Duration ?? TimeSpan.Zero;

        TrimStartDisplay = PlaybackViewModel.FormatPlain(_trimStart);
        TrimEndDisplay   = PlaybackViewModel.FormatPlain(_trimEnd);
        NotifyStartMoved();
        NotifyEndMoved();

        IsTrimming = true;
    }

    /// <summary>Clears the destructive-trim prompt whenever the form closes, and repoints precision.</summary>
    /// <param name="value">Whether the form is now open.</param>
    partial void OnIsTrimmingChanged(bool value)
    {
        if (!value) TrimDestructiveWarning = null;
        _host.OnTrimmingChanged();
    }

    // ---- Moving the trim points ----

    /// <summary>Marks the trim start at the current play head.</summary>
    private void MarkTrimStart()
    {
        _trimStart       = TimeSpan.FromSeconds(_host.CurrentPositionSeconds);
        TrimStartDisplay = PlaybackViewModel.FormatPrecise(_trimStart);
        TrimStartError   = null;
        NotifyStartMoved();
    }

    /// <summary>Marks the trim end at the current play head.</summary>
    private void MarkTrimEnd()
    {
        _trimEnd       = TimeSpan.FromSeconds(_host.CurrentPositionSeconds);
        TrimEndDisplay = PlaybackViewModel.FormatPrecise(_trimEnd);
        TrimEndError   = null;
        NotifyEndMoved();
    }

    /// <summary>
    /// Moves the trim start to a fraction along the timeline, used while dragging its handle.
    /// </summary>
    /// <param name="fraction">The position along the timeline, from 0 to 1.</param>
    public void SetTrimStartFromFraction(double fraction)
    {
        _trimStart       = TimeSpan.FromSeconds(
            Math.Clamp(fraction * _host.DurationSeconds, 0, _host.DurationSeconds));
        TrimStartDisplay = PlaybackViewModel.FormatPrecise(_trimStart);
        TrimStartError   = null;
        NotifyStartMoved();
    }

    /// <summary>
    /// Moves the trim end to a fraction along the timeline, used while dragging its handle.
    /// </summary>
    /// <param name="fraction">The position along the timeline, from 0 to 1.</param>
    public void SetTrimEndFromFraction(double fraction)
    {
        _trimEnd       = TimeSpan.FromSeconds(
            Math.Clamp(fraction * _host.DurationSeconds, 0, _host.DurationSeconds));
        TrimEndDisplay = PlaybackViewModel.FormatPrecise(_trimEnd);
        TrimEndError   = null;
        NotifyEndMoved();
    }

    /// <summary>Applies the trim start the user typed, restoring the last good value if it will not parse.</summary>
    private void CommitTrimStart()
    {
        if (TimestampInput.TryParse(TrimStartDisplay, out var parsed))
        {
            _trimStart     = parsed;
            TrimStartError = null;
            NotifyStartMoved();
        }
        else
        {
            TrimStartError = InvalidTimeMessage;
        }

        TrimStartDisplay = PlaybackViewModel.FormatPlain(_trimStart);
    }

    /// <summary>Applies the trim end the user typed, restoring the last good value if it will not parse.</summary>
    private void CommitTrimEnd()
    {
        if (TimestampInput.TryParse(TrimEndDisplay, out var parsed))
        {
            _trimEnd     = parsed;
            TrimEndError = null;
            NotifyEndMoved();
        }
        else
        {
            TrimEndError = InvalidTimeMessage;
        }

        TrimEndDisplay = PlaybackViewModel.FormatPlain(_trimEnd);
    }

    private void NotifyStartMoved() => OnPropertyChanged(nameof(TrimStartFraction));

    private void NotifyEndMoved() => OnPropertyChanged(nameof(TrimEndFraction));

    // ---- Queueing the export ----

    /// <summary>
    /// Queues the trim, first warning when a destructive trim would cut through highlights that
    /// fall outside the range.
    /// </summary>
    /// <returns>A task that completes once the job is queued or the warning has been raised.</returns>
    private async Task QueueTrimExportAsync()
    {
        if (_host.CurrentClip is null || _trimStart >= _trimEnd || string.IsNullOrWhiteSpace(TrimOutputPath))
            return;

        if (IsTrimDestructive)
        {
            var clipped = _host.Highlights
                .Where(h => h.StartTime < _trimStart || h.EndTime > _trimEnd)
                .ToList();

            if (clipped.Count > 0)
            {
                TrimDestructiveWarning = BuildDestructiveWarning(clipped.Count,
                    string.Join(", ", clipped.Take(3).Select(h => $"\"{h.Label}\"")),
                    clipped.Count > 3 ? clipped.Count - 3 : 0);
                return;
            }
        }

        await ExecuteQueueTrimAsync();
    }

    /// <summary>
    /// Builds the prompt shown when a destructive trim would clip existing highlights.
    /// </summary>
    /// <param name="count">How many highlights fall outside the range.</param>
    /// <param name="names">The quoted labels of the first few of them.</param>
    /// <param name="overflow">How many more there are beyond those named.</param>
    /// <returns>The prompt to show.</returns>
    private static string BuildDestructiveWarning(int count, string names, int overflow)
    {
        var plural = count == 1 ? string.Empty : "s";
        var verb   = count == 1 ? "s" : string.Empty;
        if (overflow > 0) names += $" and {overflow} more";

        return $"{count} highlight{plural} fall{verb} outside the trim range and will be clipped: " +
               $"{names}. Queue anyway?";
    }

    /// <summary>Queues the trim after the user has acknowledged the destructive-trim prompt.</summary>
    /// <returns>A task that completes once the job is queued.</returns>
    private async Task ConfirmDestructiveTrimAsync()
    {
        TrimDestructiveWarning = null;
        await ExecuteQueueTrimAsync();
    }

    /// <summary>Queues the trim unconditionally and starts draining the export queue.</summary>
    /// <returns>A task that completes once the job is queued.</returns>
    private async Task ExecuteQueueTrimAsync()
    {
        var clip = _host.CurrentClip;
        if (clip is null) return;

        var trimMode = IsTrimDestructive ? TrimMode.Destructive : TrimMode.NonDestructive;

        try
        {
            await _exportService.QueueAsync(
                clip.Id,
                null,
                TrimOutputPath,
                trimMode,
                IsTrimDestructive,
                _trimStart,
                _trimEnd);
        }
        catch (Exception ex)
        {
            _host.ReportExportFailure($"Export failed: {ex.Message}");
            return;
        }

        IsTrimming = false;
        _host.RunExportQueue();
    }
}
