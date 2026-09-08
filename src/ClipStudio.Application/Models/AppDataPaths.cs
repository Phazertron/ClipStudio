using System;
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
    /// <param name="isProfileScoped">
    /// <see langword="true"/> when the app was launched with <c>--profile</c>. A profile is meant
    /// to be disposable, so anything it writes must stay inside <paramref name="root"/> rather
    /// than landing in the user's real folders.
    /// </param>
    public AppDataPaths(string root, bool isProfileScoped = false)
    {
        Root            = root;
        IsProfileScoped = isProfileScoped;
        AudioCachePath  = Path.Combine(root, "audio_cache");
        MediaCachePath  = Path.Combine(root, "media-cache");

        // Screenshots are the one artefact meant for the user rather than the app, so by default
        // they go to Pictures. Under a profile that would escape the sandbox, so they stay inside.
        DefaultScreenshotFolder = isProfileScoped
            ? Path.Combine(root, "screenshots")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "ClipStudio");
    }

    /// <summary>Gets the root application data directory.</summary>
    public string Root { get; }

    /// <summary>Gets the directory used to cache mixed-audio preview files.</summary>
    public string AudioCachePath { get; }

    /// <summary>Gets the directory used to cache miscellaneous media artefacts.</summary>
    public string MediaCachePath { get; }

    /// <summary>Gets a value indicating whether a <c>--profile</c> launch argument is active.</summary>
    public bool IsProfileScoped { get; }

    /// <summary>
    /// Gets the folder screenshots are written to when the user has not configured one.
    /// </summary>
    public string DefaultScreenshotFolder { get; }
}
