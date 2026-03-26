using System.Collections.Generic;
using ClipStudio.Core.Enums;

namespace ClipStudio.Application.Models;

/// <summary>
/// Represents the persisted user preferences for the ClipStudio application.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Gets or sets the default trim mode applied when exporting clips or highlights.</summary>
    public TrimMode DefaultTrimMode { get; set; } = TrimMode.NonDestructive;

    /// <summary>
    /// Gets or sets whether the original source file is deleted after a successful destructive export.
    /// Only relevant when <see cref="DefaultTrimMode"/> is <see cref="TrimMode.Destructive"/>.
    /// </summary>
    public bool DeleteOriginalAfterDestructiveTrim { get; set; } = false;

    /// <summary>Gets or sets the offset in seconds from the start of the clip used to capture the thumbnail frame.</summary>
    public int ThumbnailOffsetSeconds { get; set; } = 5;

    /// <summary>Gets or sets the number of frames included in the hover-scrub preview strip per clip.</summary>
    public int PreviewStripFrameCount { get; set; } = 20;

    /// <summary>Gets or sets the UI theme preference. Accepted values: "Dark", "Light", "System".</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Gets or sets the absolute path to the folder where frame screenshots are saved.</summary>
    public string ScreenshotOutputFolder { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the application has not yet completed the first-run setup wizard.</summary>
    public bool IsFirstRun { get; set; } = true;

    /// <summary>
    /// Gets or sets the absolute path to the folder containing the FFmpeg binaries (ffmpeg.exe, ffprobe.exe).
    /// Leave empty to rely on the system PATH, or to let the app auto-detect a <c>ffmpeg/</c> subfolder
    /// next to the application executable.
    /// </summary>
    public string FfmpegBinaryFolder { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether a clip is automatically transitioned to the Reviewed state when the user
    /// applies their first tag to it. Defaults to <c>true</c>.
    /// </summary>
    public bool AutoMarkReviewedOnTagAdd { get; set; } = true;

    /// <summary>
    /// Gets or sets whether a clip starts playing automatically when it is opened in the detail view.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool AutoPlayOnOpen { get; set; } = true;

    /// <summary>
    /// Gets or sets whether all active source folders are scanned for new clips automatically
    /// each time the application starts. Defaults to <c>false</c>.
    /// </summary>
    public bool AutoScanAtStartup { get; set; } = false;

    /// <summary>
    /// Gets or sets whether mixed audio previews are cached on disk so repeated playback of the
    /// same track selection does not require re-running FFmpeg. Defaults to <c>true</c>.
    /// </summary>
    public bool CacheAudioPreviews { get; set; } = true;

    /// <summary>
    /// Gets or sets whether clips removed from the Trash (by auto-expiry after 30 days or via
    /// the Empty Trash action) are sent to the operating-system Recycle Bin rather than being
    /// permanently deleted. Defaults to <c>true</c>.
    /// </summary>
    public bool TrashExpiredSendToRecycleBin { get; set; } = true;

    /// <summary>
    /// Gets or sets whether players marked as "Me" are automatically tagged on every newly imported clip.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool AutoApplyMePlayerOnImport { get; set; } = true;

    /// <summary>
    /// Gets or sets the persisted pixel widths for the resizable columns in the library detail view.
    /// Keyed by column name (e.g. "Game", "Tags", "Players", "Date", "Duration", "Rating").
    /// A <c>null</c> value means default widths are used.
    /// </summary>
    public Dictionary<string, double>? LibraryColumnWidths { get; set; }

    /// <summary>
    /// Gets or sets the persisted pixel widths for the resizable columns in the highlights list.
    /// Keyed by column name (e.g. "Tags", "Game", "Players", "Start", "End", "Duration", "Created").
    /// A <c>null</c> value means default widths are used.
    /// </summary>
    public Dictionary<string, double>? HighlightsColumnWidths { get; set; }

    /// <summary>
    /// Gets or sets whether game cover art and player icon images are shown in the library
    /// details view and highlights list. When <c>false</c>, plain text is shown instead.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool ShowImagesInLists { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum log level written to the rolling log file.
    /// Accepted values: "Verbose", "Debug", "Information", "Warning", "Error", "Fatal".
    /// Defaults to "Error" so the log stays quiet during normal use.
    /// </summary>
    public string MinimumLogLevel { get; set; } = "Error";

    /// <summary>
    /// Gets or sets whether UI sound effects (e.g. notification chimes) are played.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool SoundEffectsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the row height in pixels for the library details view.
    /// Defaults to 52 pixels to comfortably show the thumbnail and metadata.
    /// </summary>
    public int LibraryDetailsRowHeight { get; set; } = 52;

    /// <summary>
    /// Gets or sets whether local voice transcription is enabled.
    /// When <c>false</c>, the transcription panel and controls are hidden throughout the UI.
    /// Defaults to <c>false</c> because a model must be downloaded before the feature can function.
    /// </summary>
    public bool TranscriptionEnabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the absolute path to the GGML Whisper model file (.bin) used for transcription.
    /// Leave empty if no model has been configured; the setup dialog will prompt the user.
    /// </summary>
    public string TranscriptionModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the hardware inference backend used by the Whisper engine.
    /// Defaults to <see cref="TranscriptionBackend.Auto"/> which selects Vulkan when available
    /// and falls back to CPU.
    /// </summary>
    public TranscriptionBackend TranscriptionBackend { get; set; } = TranscriptionBackend.Auto;

    /// <summary>
    /// Gets or sets the BCP-47 language code passed to Whisper (e.g. "en", "fr"), or "auto"
    /// for automatic language detection. Defaults to "auto".
    /// </summary>
    public string TranscriptionLanguage { get; set; } = "auto";

    /// <summary>
    /// Gets or sets the folder where generated SRT subtitle files are saved.
    /// When empty, the SRT file is written to the same directory as the source clip.
    /// </summary>
    public string TranscriptionSrtFolder { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the experimental speaker diarization pass is attempted during
    /// transcription. This is a no-op in the current release; the flag is reserved for a
    /// future implementation that requires additional tooling.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool TranscriptionEnableDiarization { get; set; } = false;
}
