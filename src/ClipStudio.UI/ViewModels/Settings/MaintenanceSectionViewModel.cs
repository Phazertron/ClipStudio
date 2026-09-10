using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ClipStudio.UI.ViewModels.Settings;

/// <summary>
/// The Library Maintenance section of the Settings page: the on-demand repair pass and the
/// logging controls used to diagnose it.
/// </summary>
public sealed partial class MaintenanceSectionViewModel : SettingsSectionViewModel
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundTaskService _tasks;

    /// <inheritdoc/>
    public override string Title => "Maintenance";

    /// <inheritdoc/>
    public override MaterialIconKind Icon => MaterialIconKind.Wrench;

    /// <summary>
    /// Gets or sets the minimum log level for the rolling log file.
    /// Accepted values: "Verbose", "Debug", "Information", "Warning", "Error", "Fatal".
    /// </summary>
    [ObservableProperty]
    private string _minimumLogLevel = "Error";

    /// <summary>
    /// Gets the list of available log levels for the minimum log level ComboBox.
    /// Exposed as a static list so the AXAML ComboBox can bind to it as ItemsSource,
    /// allowing <see cref="MinimumLogLevel"/> (a plain string) to match correctly.
    /// </summary>
    public static IReadOnlyList<string> LogLevels { get; } =
        new[] { "Verbose", "Debug", "Information", "Warning", "Error", "Fatal" };

    /// <summary>
    /// Gets or sets whether a repair pass is currently running, which disables the button that
    /// starts one.
    /// </summary>
    [ObservableProperty]
    private bool _isRepairRunning;

    /// <summary>
    /// Gets the command that runs an on-demand library sanitize pass to regenerate missing
    /// thumbnails and preview strips and remove orphaned cache files.
    /// </summary>
    public IAsyncRelayCommand RepairLibraryCommand { get; }

    /// <summary>Gets the command that opens the logs folder in the system file explorer.</summary>
    public IRelayCommand OpenLogsFolderCommand { get; }

    /// <summary>Initialises a new <see cref="MaintenanceSectionViewModel"/>.</summary>
    /// <param name="host">The settings page hosting this section.</param>
    /// <param name="scopeFactory">The scope factory used to resolve the scoped sanitizer service.</param>
    /// <param name="tasks">The registry the repair reports itself into.</param>
    public MaintenanceSectionViewModel(
        ISettingsSectionHost host,
        IServiceScopeFactory scopeFactory,
        IBackgroundTaskService tasks)
        : base(host)
    {
        _scopeFactory = scopeFactory;
        _tasks        = tasks;

        RepairLibraryCommand  = new AsyncRelayCommand(RepairLibraryAsync);
        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);
    }

    /// <inheritdoc/>
    public override void LoadFrom(AppSettings settings)
    {
        MinimumLogLevel = settings.MinimumLogLevel;
    }

    /// <inheritdoc/>
    public override void ApplyTo(AppSettings settings)
    {
        settings.MinimumLogLevel = MinimumLogLevel;
    }

    /// <summary>
    /// Runs an on-demand library sanitize pass that regenerates any missing thumbnail or
    /// preview-strip files and removes orphaned files from the media cache.
    /// Progress messages are surfaced on the page's status line.
    /// </summary>
    private async Task RepairLibraryAsync()
    {
        IsRepairRunning = true;
        Host.SetRepairing(true);
        Host.StatusMessage = "Repairing library...";

        // Cancellable now. The pass already threaded a token all the way down; nothing was ever
        // passing one, which is what made the old startup run impossible to stop.
        using var cancellation = new CancellationTokenSource();
        var task = _tasks.Start(
            "Repairing library",
            MaterialIconKind.Wrench,
            ownerNavLabel: "Settings",
            cancellation: cancellation);

        // The sanitizer reports what it is doing rather than how much is left, so progress only
        // drives the running detail. The summary comes back from the call itself: Progress<T> posts
        // asynchronously, so scraping the last reported line here read whichever message happened
        // to have landed, not the summary.
        var progress = new Progress<string>(msg =>
            Dispatcher.UIThread.Post(() =>
            {
                Host.StatusMessage = msg;
                task.Report(msg);
            }));

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sanitizer   = scope.ServiceProvider.GetRequiredService<ILibrarySanitizerService>();
            var summary = await sanitizer.SanitizeAsync(progress, cancellation.Token);

            task.Finish(BackgroundTaskState.Completed, summary);
        }
        catch (OperationCanceledException)
        {
            // Every repair the pass had already made is a completed write of its own, so stopping
            // leaves the library consistent - just less repaired than it would have been.
            task.Finish(BackgroundTaskState.Cancelled, "Repair stopped. Everything already repaired was kept.");
            Host.StatusMessage = "Repair cancelled.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Library repair failed.");
            task.Finish(BackgroundTaskState.Failed, ex.Message);
            Host.StatusMessage = "Repair failed - see the log for details.";
        }
        finally
        {
            Host.SetRepairing(false);
            IsRepairRunning = false;
        }

        // A repair is the thing that clears the unhashed-clip warning, so the count has to be
        // re-read here. Without it the warning stayed up, naming a number that was no longer true,
        // until the page happened to be reloaded.
        await Host.NotifyLibraryRepairedAsync();
    }

    /// <summary>
    /// Opens the ClipStudio logs folder in the system file explorer.
    /// Creates the folder first if it does not yet exist.
    /// </summary>
    private static void OpenLogsFolder()
    {
        Directory.CreateDirectory(App.LogsFolder);
        Process.Start(new ProcessStartInfo
        {
            FileName        = App.LogsFolder,
            UseShellExecute = true,
        });
    }
}
