using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ClipStudio.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace ClipStudio.UI.ViewModels.Settings;

/// <summary>
/// The About section of the Settings page: the application version, where it stands against the
/// latest release, and the two outward links, support and feedback.
/// </summary>
public sealed partial class AboutSectionViewModel : SettingsSectionViewModel
{
    private readonly IApplicationUpdateService _updates;

    /// <inheritdoc/>
    public override string Title => "About";

    /// <inheritdoc/>
    public override MaterialIconKind Icon => MaterialIconKind.InformationOutline;

    /// <summary>Gets the command that opens the Ko-fi support page in the default browser.</summary>
    public IRelayCommand OpenKofiCommand { get; }

    /// <summary>Gets the command that opens the GitHub Issues page to submit feedback.</summary>
    public IRelayCommand SendFeedbackCommand { get; }

    /// <summary>Gets the command that asks for an update check right now.</summary>
    /// <remarks>
    /// Works regardless of the automatic-check setting: turning the automatic checks off means
    /// "do not go looking on your own", not "refuse to look when asked".
    /// </remarks>
    public IAsyncRelayCommand CheckForUpdatesCommand { get; }

    /// <summary>
    /// Gets a human-readable application version string, such as <c>ClipStudio v1.1.4</c>.
    /// </summary>
    public string AppVersion => ApplicationVersion.Display;

    /// <summary>Gets or sets whether a check is running, so the button can disable itself.</summary>
    [ObservableProperty]
    private bool _isCheckingForUpdates;

    /// <summary>Gets or sets the one-line summary of where this build stands.</summary>
    [ObservableProperty]
    private string _updateStatusText = "";

    /// <summary>Gets or sets the icon shown beside <see cref="UpdateStatusText"/>.</summary>
    [ObservableProperty]
    private MaterialIconKind _updateStatusIcon = MaterialIconKind.InformationOutline;

    /// <summary>Initialises a new <see cref="AboutSectionViewModel"/>.</summary>
    /// <param name="host">The settings page hosting this section.</param>
    /// <param name="updates">The updater this section reports on and can ask to check.</param>
    public AboutSectionViewModel(ISettingsSectionHost host, IApplicationUpdateService updates)
        : base(host)
    {
        _updates = updates;

        OpenKofiCommand        = new RelayCommand(OpenKofi);
        SendFeedbackCommand    = new RelayCommand(SendFeedback);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);

        _updates.StatusChanged += (_, status) => Describe(status);
        Describe(_updates.Status);
    }

    /// <inheritdoc/>
    public override Task RefreshAsync()
    {
        Describe(_updates.Status);
        return Task.CompletedTask;
    }

    /// <summary>Asks the updater to check, then describes what it found.</summary>
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        UpdateStatusText     = "Checking for updates...";
        UpdateStatusIcon     = MaterialIconKind.CloudSyncOutline;

        try
        {
            Describe(await _updates.CheckAsync());
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>Turns a status into the line shown to the user.</summary>
    /// <param name="status">The status to describe.</param>
    private void Describe(UpdateStatus status)
    {
        if (!status.IsSupported)
        {
            // A publish folder or a debugger session. Saying "up to date" would be a claim this
            // build is in no position to make.
            UpdateStatusText = "This build does not update itself.";
            UpdateStatusIcon = MaterialIconKind.InformationOutline;
            return;
        }

        if (status.FailureReason is not null)
        {
            UpdateStatusText = "Could not check for updates. See the log for details.";
            UpdateStatusIcon = MaterialIconKind.AlertCircleOutline;
            return;
        }

        if (status.IsDownloading)
        {
            UpdateStatusText = $"Downloading {status.AvailableVersion}...";
            UpdateStatusIcon = MaterialIconKind.Download;
            return;
        }

        if (status.IsDownloaded)
        {
            UpdateStatusText = $"{status.AvailableVersion} is ready to install. See Attention required.";
            UpdateStatusIcon = MaterialIconKind.Download;
            return;
        }

        if (status.IsUpdateAvailable)
        {
            UpdateStatusText = $"{status.AvailableVersion} is available. See Attention required.";
            UpdateStatusIcon = MaterialIconKind.Download;
            return;
        }

        UpdateStatusText = "Up to date.";
        UpdateStatusIcon = MaterialIconKind.CheckCircleOutline;
    }

    /// <summary>Opens the Ko-fi support page in the default browser.</summary>
    private static void OpenKofi()
    {
        Process.Start(new ProcessStartInfo("https://ko-fi.com/phazertron") { UseShellExecute = true });
    }

    /// <summary>
    /// Opens GitHub Issues with a pre-filled feedback template and auto-detected environment info.
    /// </summary>
    private static void SendFeedback()
    {
        try
        {
            Process.Start(new ProcessStartInfo(GitHubIssueHelper.BuildFeedbackIssueUrl()) { UseShellExecute = true });
        }
        catch
        {
            // Opening a browser is best-effort.
        }
    }
}
