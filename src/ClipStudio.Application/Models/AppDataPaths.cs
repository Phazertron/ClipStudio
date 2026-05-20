using System.IO;

namespace ClipStudio.Application.Models;

/// <summary>
/// Provides resolved paths to the root application data directory and its well-known
/// sub-directories. Registered as a singleton so services can consume it without
/// re-computing or hard-coding the folder name.
/// </summary>
public sealed record AppDataPaths
{
    /// <summary>Initialises a new instance with the given root directory path.</summary>
    /// <param name="root">
    /// Absolute path to the application data root (e.g. <c>%AppData%\ClipStudio</c>
    /// or <c>%AppData%\ClipStudio_demo</c> when a <c>--profile</c> argument is active).
    /// </param>
    public AppDataPaths(string root)
    {
        Root           = root;
        AudioCachePath = Path.Combine(root, "audio_cache");
        MediaCachePath = Path.Combine(root, "media-cache");
    }

    /// <summary>Gets the root application data directory.</summary>
    public string Root { get; }

    /// <summary>Gets the directory used to cache mixed-audio preview files.</summary>
    public string AudioCachePath { get; }

    /// <summary>Gets the directory used to cache miscellaneous media artefacts.</summary>
    public string MediaCachePath { get; }
}
