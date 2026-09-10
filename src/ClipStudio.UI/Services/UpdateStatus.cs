namespace ClipStudio.UI.Services;

/// <summary>
/// A snapshot of what the updater knows: which version is running, whether a newer one exists,
/// and whether it has finished downloading.
/// </summary>
/// <remarks>
/// Immutable so it can be handed to the UI thread without a lock. The updater replaces the whole
/// snapshot rather than mutating fields, so a reader never sees a half-updated state.
/// </remarks>
public sealed class UpdateStatus
{
    /// <summary>
    /// Gets whether this build can update itself at all.
    /// </summary>
    /// <remarks>
    /// False when the application is running from a plain publish folder or the debugger rather
    /// than a Velopack installation. Nothing is wrong in that case, so nothing should be reported.
    /// </remarks>
    public bool IsSupported { get; init; }

    /// <summary>Gets the version currently running, or null when it cannot be determined.</summary>
    public string? CurrentVersion { get; init; }

    /// <summary>Gets the newer version that is available, or null when none is.</summary>
    public string? AvailableVersion { get; init; }

    /// <summary>Gets whether a newer version has been found.</summary>
    public bool IsUpdateAvailable => AvailableVersion is not null;

    /// <summary>
    /// Gets whether the available version has finished downloading and can be applied immediately.
    /// </summary>
    public bool IsDownloaded { get; init; }

    /// <summary>Gets the reason the last check failed, or null when it did not fail.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Gets a status describing a build that cannot update itself.</summary>
    public static UpdateStatus Unsupported { get; } = new() { IsSupported = false };
}
