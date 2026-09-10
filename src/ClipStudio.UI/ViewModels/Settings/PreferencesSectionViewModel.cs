using System;
using System.IO;
using System.Threading.Tasks;
using ClipStudio.Application.Models;
using ClipStudio.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ClipStudio.UI.ViewModels.Settings;

/// <summary>
/// The Preferences section of the Settings page: the everyday behaviour toggles, the thumbnail
/// and screenshot paths, content hashing, the audio preview cache and the trash policy.
/// </summary>
public sealed partial class PreferencesSectionViewModel : SettingsSectionViewModel
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <inheritdoc/>
    public override string Title => "Preferences";

    /// <inheritdoc/>
    public override MaterialIconKind Icon => MaterialIconKind.TuneVariant;

    /// <summary>Gets or sets the offset in seconds from clip start used to capture the thumbnail frame.</summary>
    [ObservableProperty]
    private int _thumbnailOffsetSeconds;

    /// <summary>Gets or sets the absolute path where frame screenshots are saved.</summary>
    [ObservableProperty]
    private string _screenshotOutputFolder = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path to the folder containing the FFmpeg binaries.
    /// Leave blank to use the system PATH or the auto-detected bundled <c>ffmpeg/</c> folder.
    /// </summary>
    [ObservableProperty]
    private string _ffmpegBinaryFolder = string.Empty;

    /// <summary>
    /// Gets or sets whether clips are automatically transitioned to Reviewed when the user
    /// adds their first tag to them.
    /// </summary>
    [ObservableProperty]
    private bool _autoMarkReviewedOnTagAdd = true;

    /// <summary>
    /// Gets or sets whether a clip starts playing automatically when it is opened in the detail view.
    /// </summary>
    [ObservableProperty]
    private bool _autoPlayOnOpen = true;

    /// <summary>
    /// Gets or sets whether all active source folders are scanned for new clips on each application start.
    /// </summary>
    [ObservableProperty]
    private bool _autoScanAtStartup;

    /// <summary>
    /// Gets or sets whether clip files are hashed on import and during Repair Library.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUnhashedClipNote))]
    private bool _contentHashingEnabled;

    /// <summary>Gets or sets how many clips in the library carry no content hash.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUnhashedClipNote))]
    [NotifyPropertyChangedFor(nameof(UnhashedClipNote))]
    private int _unhashedClipCount;

    /// <summary>
    /// Gets whether to warn that part of the library takes no part in duplicate detection.
    /// </summary>
    /// <remarks>
    /// Only worth saying while hashing is on: with it off, nothing is being detected anyway, so the
    /// note would be noise rather than a warning.
    /// </remarks>
    public bool ShowUnhashedClipNote => ContentHashingEnabled && UnhashedClipCount > 0;

    /// <summary>Gets the warning text naming how many clips are not yet hashed.</summary>
    public string UnhashedClipNote => UnhashedClipCount == 1
        ? "1 clip has not been hashed yet and will not be matched against new imports. Run Repair Library to include it."
        : $"{UnhashedClipCount} clips have not been hashed yet and will not be matched against new imports. Run Repair Library to include them.";

    /// <summary>
    /// Gets or sets whether mixed audio preview files are cached on disk between sessions.
    /// </summary>
    [ObservableProperty]
    private bool _cacheAudioPreviews = true;

    /// <summary>
    /// Gets or sets whether clips removed from the Trash (by auto-expiry or Empty Trash) are
    /// sent to the OS Recycle Bin. When false, files are permanently deleted immediately.
    /// </summary>
    [ObservableProperty]
    private bool _trashExpiredSendToRecycleBin = true;

    /// <summary>
    /// Gets or sets whether players marked as "Me" are automatically tagged on every newly imported clip.
    /// </summary>
    [ObservableProperty]
    private bool _autoApplyMePlayerOnImport = true;

    /// <summary>
    /// Gets or sets whether game cover art and player icon images are shown in the library
    /// details view and highlights list. When disabled, plain text is shown instead.
    /// </summary>
    [ObservableProperty]
    private bool _showImagesInLists = true;

    /// <summary>
    /// Gets or sets whether UI sound effects are played.
    /// </summary>
    [ObservableProperty]
    private bool _soundEffectsEnabled = true;

    /// <summary>Gets the command that clears all cached audio mix preview files.</summary>
    public IRelayCommand ClearAudioCacheCommand { get; }

    /// <summary>Initialises a new <see cref="PreferencesSectionViewModel"/>.</summary>
    /// <param name="host">The settings page hosting this section.</param>
    /// <param name="scopeFactory">The scope factory used to resolve the scoped clip repository.</param>
    public PreferencesSectionViewModel(ISettingsSectionHost host, IServiceScopeFactory scopeFactory)
        : base(host)
    {
        _scopeFactory = scopeFactory;

        ClearAudioCacheCommand = new RelayCommand(ClearAudioCache);
    }

    /// <inheritdoc/>
    public override void LoadFrom(AppSettings settings)
    {
        ThumbnailOffsetSeconds       = settings.ThumbnailOffsetSeconds;
        ScreenshotOutputFolder       = settings.ScreenshotOutputFolder ?? string.Empty;
        FfmpegBinaryFolder           = settings.FfmpegBinaryFolder ?? string.Empty;
        AutoMarkReviewedOnTagAdd     = settings.AutoMarkReviewedOnTagAdd;
        AutoPlayOnOpen               = settings.AutoPlayOnOpen;
        AutoScanAtStartup            = settings.AutoScanAtStartup;
        ContentHashingEnabled        = settings.ContentHashingEnabled;
        CacheAudioPreviews           = settings.CacheAudioPreviews;
        TrashExpiredSendToRecycleBin = settings.TrashExpiredSendToRecycleBin;
        AutoApplyMePlayerOnImport    = settings.AutoApplyMePlayerOnImport;
        ShowImagesInLists            = settings.ShowImagesInLists;
        SoundEffectsEnabled          = settings.SoundEffectsEnabled;
    }

    /// <inheritdoc/>
    public override void ApplyTo(AppSettings settings)
    {
        settings.ThumbnailOffsetSeconds       = ThumbnailOffsetSeconds;
        settings.ScreenshotOutputFolder       = ScreenshotOutputFolder.Trim();
        settings.FfmpegBinaryFolder           = FfmpegBinaryFolder.Trim();
        settings.AutoMarkReviewedOnTagAdd     = AutoMarkReviewedOnTagAdd;
        settings.AutoPlayOnOpen               = AutoPlayOnOpen;
        settings.AutoScanAtStartup            = AutoScanAtStartup;
        settings.ContentHashingEnabled        = ContentHashingEnabled;
        settings.CacheAudioPreviews           = CacheAudioPreviews;
        settings.TrashExpiredSendToRecycleBin = TrashExpiredSendToRecycleBin;
        settings.AutoApplyMePlayerOnImport    = AutoApplyMePlayerOnImport;
        settings.ShowImagesInLists            = ShowImagesInLists;
        settings.SoundEffectsEnabled          = SoundEffectsEnabled;
    }

    /// <inheritdoc/>
    public override Task RefreshAsync() => RefreshUnhashedClipCountAsync();

    /// <summary>
    /// Recounts the clips with no content hash, which drives the warning next to the hashing toggle.
    /// </summary>
    /// <remarks>
    /// Read through a scope because the repository is scoped and this view model outlives one.
    /// A failure here is not worth surfacing - the count is advisory, so it falls back to zero and
    /// simply hides the note.
    /// </remarks>
    public async Task RefreshUnhashedClipCountAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var clips = scope.ServiceProvider.GetRequiredService<IClipRepository>();
            UnhashedClipCount = await clips.CountWithoutFileHashAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not count unhashed clips.");
            UnhashedClipCount = 0;
        }
    }

    /// <summary>
    /// Deletes all cached audio mix preview files from the application's audio cache folder.
    /// </summary>
    private void ClearAudioCache()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipStudio", "audio_cache");

        if (!Directory.Exists(folder))
        {
            Host.StatusMessage = "Audio cache folder not found — nothing to clear.";
            return;
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*.mkv"))
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch
            {
                // Non-fatal: the file may still be held open by VLC.
            }
        }

        Host.StatusMessage = deleted == 0
            ? "Audio cache was already empty."
            : $"Cleared {deleted} audio cache file{(deleted == 1 ? string.Empty : "s")}.";
    }
}
