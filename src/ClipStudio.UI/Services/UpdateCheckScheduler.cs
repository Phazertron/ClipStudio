using System;
using System.Threading;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using Serilog;

namespace ClipStudio.UI.Services;

/// <summary>
/// Looks for new releases on a timer for as long as the application is running.
/// </summary>
/// <remarks>
/// A single check at startup suits an application that is opened and closed. ClipStudio is not
/// one: a library session can run for days, and over that time the startup check is the only one
/// that would ever happen, so a release published mid-session would never be noticed.
/// <para>
/// Checking is cheap - one small HTTPS request that downloads nothing - so the interval is set by
/// how soon it is reasonable to hear about a release rather than by cost.
/// </para>
/// </remarks>
public sealed class UpdateCheckScheduler : IAsyncDisposable
{
    /// <summary>How long to wait between checks.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IApplicationUpdateService _updates;
    private readonly ISettingsService _settings;
    private readonly CancellationTokenSource _stopping = new();

    private Task? _loop;

    /// <summary>Initialises a new <see cref="UpdateCheckScheduler"/>.</summary>
    /// <param name="updates">The updater to drive.</param>
    /// <param name="settings">The settings consulted before each automatic check.</param>
    public UpdateCheckScheduler(IApplicationUpdateService updates, ISettingsService settings)
    {
        _updates  = updates;
        _settings = settings;
    }

    /// <summary>
    /// Starts checking: once now, then every <see cref="Interval"/> until disposed.
    /// </summary>
    /// <remarks>
    /// Calling this more than once does nothing after the first time, so a second call cannot
    /// leave two loops running against one updater.
    /// </remarks>
    public void Start() => _loop ??= Task.Run(RunAsync);

    /// <summary>Runs the check loop until cancellation.</summary>
    private async Task RunAsync()
    {
        var token = _stopping.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                // Read the setting on every pass rather than once at startup, so turning
                // automatic checks off takes effect without a restart.
                if (_settings.Current.AutomaticUpdateChecksEnabled)
                    await _updates.CheckAsync(token).ConfigureAwait(false);
                else
                    Log.Debug("Automatic update check skipped: turned off in settings.");

                await Task.Delay(Interval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // CheckAsync does not throw, so reaching here means the loop itself broke. Log it
            // rather than let it escape onto an unobserved task.
            Log.Warning(ex, "The update check loop stopped unexpectedly.");
        }
    }

    /// <summary>Stops checking and waits for the loop to unwind.</summary>
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);

        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }

        _stopping.Dispose();
    }
}
