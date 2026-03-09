namespace ClipStudio.UI.ViewModels.WizardSteps;

/// <summary>
/// Indicates the result of FFmpeg binary auto-detection performed during the setup wizard.
/// </summary>
public enum FfmpegDetectionStatus
{
    /// <summary>Detection is in progress and the result is not yet known.</summary>
    Checking,

    /// <summary>FFmpeg was found in the bundled <c>ffmpeg/</c> subfolder next to the application executable.</summary>
    FoundBundled,

    /// <summary>FFmpeg was found in a directory listed on the system PATH environment variable.</summary>
    FoundOnPath,

    /// <summary>FFmpeg was not found automatically; manual configuration is required.</summary>
    NotFound,
}
