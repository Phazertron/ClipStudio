using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace ClipStudio.UI.ViewModels.Settings;

/// <summary>
/// The About section of the Settings page: the application version and the two outward links,
/// support and feedback.
/// </summary>
public sealed class AboutSectionViewModel : SettingsSectionViewModel
{
    /// <inheritdoc/>
    public override string Title => "About";

    /// <inheritdoc/>
    public override MaterialIconKind Icon => MaterialIconKind.InformationOutline;

    /// <summary>Gets the command that opens the Ko-fi support page in the default browser.</summary>
    public IRelayCommand OpenKofiCommand { get; }

    /// <summary>Gets the command that opens the GitHub Issues page to submit feedback.</summary>
    public IRelayCommand SendFeedbackCommand { get; }

    /// <summary>
    /// Gets a human-readable application version string, such as <c>ClipStudio v1.1.4</c>.
    /// </summary>
    public string AppVersion => ApplicationVersion.Display;

    /// <summary>Initialises a new <see cref="AboutSectionViewModel"/>.</summary>
    /// <param name="host">The settings page hosting this section.</param>
    public AboutSectionViewModel(ISettingsSectionHost host)
        : base(host)
    {
        OpenKofiCommand     = new RelayCommand(OpenKofi);
        SendFeedbackCommand = new RelayCommand(SendFeedback);
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
