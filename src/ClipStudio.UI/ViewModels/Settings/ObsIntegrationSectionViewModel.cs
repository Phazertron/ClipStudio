using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace ClipStudio.UI.ViewModels.Settings;

/// <summary>
/// The OBS Integration section of the Settings page: where the bundled replay-tagger script lives
/// and whether OBS can currently see it.
/// </summary>
public sealed class ObsIntegrationSectionViewModel : SettingsSectionViewModel
{
    /// <inheritdoc/>
    public override string Title => "OBS Integration";

    /// <inheritdoc/>
    public override MaterialIconKind Icon => MaterialIconKind.Video;

    /// <summary>Gets the command that opens the OBS scripts folder in the system file explorer.</summary>
    public IRelayCommand OpenObsScriptsFolderCommand { get; }

    /// <summary>
    /// Gets the status of the bundled OBS Python script.
    /// Returns a message indicating whether the script file exists next to the application binary.
    /// </summary>
    public string ObsScriptStatus
        => File.Exists(ObsScriptPath)
            ? $"Script found: {ObsScriptPath}"
            : $"Script not found at expected location: {ObsScriptPath}";

    /// <summary>
    /// Gets the absolute path to the bundled OBS Python script,
    /// located in <c>obs-scripts/</c> next to the application executable.
    /// </summary>
    public string ObsScriptPath =>
        Path.Combine(AppContext.BaseDirectory, "obs-scripts", "clipstudio_replay_tagger.py");

    /// <summary>Initialises a new <see cref="ObsIntegrationSectionViewModel"/>.</summary>
    /// <param name="host">The settings page hosting this section.</param>
    public ObsIntegrationSectionViewModel(ISettingsSectionHost host)
        : base(host)
    {
        OpenObsScriptsFolderCommand = new RelayCommand(OpenObsScriptsFolder);
    }

    /// <summary>
    /// Opens the OBS scripts folder in the system file explorer, creating it if it does not exist.
    /// </summary>
    private static void OpenObsScriptsFolder()
    {
        var obsScripts = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "obs-studio", "scripts");

        Directory.CreateDirectory(obsScripts);

        Process.Start(new ProcessStartInfo
        {
            FileName        = obsScripts,
            UseShellExecute = true,
        });
    }
}
