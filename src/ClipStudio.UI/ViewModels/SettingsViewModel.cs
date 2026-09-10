using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Interfaces;
using ClipStudio.UI.ViewModels.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFMpegCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the Settings page.
/// </summary>
/// <remarks>
/// The page itself owns almost nothing: it is a navigable list of
/// <see cref="SettingsSectionViewModel"/> children plus the three things that are genuinely
/// page-wide - the single status line, the loading flag, and the Save that writes every section
/// back to the settings snapshot in one go. Everything else lives in a section, which reaches back
/// through <see cref="ISettingsSectionHost"/> and nowhere else.
/// Registered as a singleton so that an in-progress import scan is not interrupted when the user
/// navigates away from the Settings tab and returns.
/// </remarks>
public sealed partial class SettingsViewModel : ViewModelBase, ISettingsSectionHost, IAttentionActionHost
{
    private readonly ISettingsService _settings;

    /// <summary>
    /// Set to <see langword="true"/> while a library repair is running.
    /// Prevents <see cref="LoadAsync"/> from clearing <see cref="StatusMessage"/> or the progress
    /// bar mid-repair.
    /// </summary>
    private bool _isRepairing;

    // ---- Sections ----

    /// <summary>Gets the navigable sections of the settings page, in display order.</summary>
    public ObservableCollection<SettingsSectionViewModel> Sections { get; } = new();

    /// <summary>Gets or sets the section currently shown in the content area.</summary>
    [ObservableProperty]
    private SettingsSectionViewModel? _selectedSection;

    /// <summary>Gets the Source Folders section.</summary>
    public SourceFoldersSectionViewModel SourceFoldersSection { get; }

    /// <summary>Gets the Preferences section.</summary>
    public PreferencesSectionViewModel PreferencesSection { get; }

    /// <summary>Gets the Transcription section.</summary>
    public TranscriptionSectionViewModel TranscriptionSection { get; }

    /// <summary>Gets the OBS Integration section.</summary>
    public ObsIntegrationSectionViewModel ObsIntegrationSection { get; }

    /// <summary>Gets the Attention Required section.</summary>
    public AttentionSectionViewModel AttentionSection { get; }

    /// <summary>Gets the Library Maintenance section.</summary>
    public MaintenanceSectionViewModel MaintenanceSection { get; }

    /// <summary>Gets the About section.</summary>
    public AboutSectionViewModel AboutSection { get; }

    // ---- State ----

    /// <summary>Gets or sets a value indicating whether a background operation is running.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Gets or sets a status message shown after saving preferences.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    // ---- Commands ----

    /// <summary>Gets the command that loads every section from persistence.</summary>
    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>Gets the command that saves every section's fields to disk.</summary>
    public IAsyncRelayCommand SavePreferencesCommand { get; }

    // ---- Callbacks ----

    /// <summary>
    /// Optional callback invoked after a source folder archive or wipe so that the main window
    /// can refresh the unreviewed clip count badge.
    /// </summary>
    public Action? UnreviewedCountRefreshRequested { get; set; }

    /// <summary>
    /// Initialises a new <see cref="SettingsViewModel"/>.
    /// </summary>
    /// <param name="settings">The application settings service.</param>
    /// <param name="folders">The source folder repository.</param>
    /// <param name="watcher">The library watcher service.</param>
    /// <param name="importService">The import service used to scan folders for existing clips.</param>
    /// <param name="scopeFactory">The service scope factory used to resolve scoped services such as <see cref="ILibrarySanitizerService"/>.</param>
    /// <param name="soundService">The service that plays UI sound cues.</param>
    /// <param name="health">The library health check whose last report feeds the attention list.</param>
    /// <param name="duplicates">The duplicate finder whose last run feeds the attention list.</param>
    public SettingsViewModel(
        ISettingsService settings,
        ISourceFolderRepository folders,
        ILibraryWatcherService watcher,
        IImportService importService,
        IServiceScopeFactory scopeFactory,
        ClipStudio.UI.Services.ISoundService soundService,
        ILibraryHealthCheckService health,
        IDuplicateClipFinder duplicates)
    {
        _settings = settings;

        SourceFoldersSection  = new SourceFoldersSectionViewModel(this, folders, watcher, importService, scopeFactory, soundService);
        PreferencesSection    = new PreferencesSectionViewModel(this, scopeFactory);
        TranscriptionSection  = new TranscriptionSectionViewModel(this, settings);
        ObsIntegrationSection = new ObsIntegrationSectionViewModel(this);
        MaintenanceSection    = new MaintenanceSectionViewModel(this, scopeFactory);
        AttentionSection      = new AttentionSectionViewModel(this, scopeFactory, health, duplicates, this);
        AboutSection          = new AboutSectionViewModel(this);

        Sections.Add(SourceFoldersSection);
        Sections.Add(PreferencesSection);
        Sections.Add(TranscriptionSection);
        Sections.Add(ObsIntegrationSection);
        Sections.Add(AttentionSection);
        Sections.Add(MaintenanceSection);
        Sections.Add(AboutSection);

        SelectedSection = SourceFoldersSection;

        LoadCommand            = new AsyncRelayCommand(LoadAsync);
        SavePreferencesCommand = new AsyncRelayCommand(SavePreferencesAsync);
    }

    // ---- Load ----

    /// <summary>
    /// Reloads every section: the settings-backed fields from the current snapshot, and whatever
    /// each section reads from elsewhere.
    /// </summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        if (!_isRepairing) StatusMessage = null;

        try
        {
            var snapshot = _settings.Current;

            foreach (var section in Sections)
            {
                section.LoadFrom(snapshot);
                await section.RefreshAsync();
            }
        }
        finally
        {
            // A repair owns IsLoading for its whole run, and it drives the progress bar. Navigating
            // away and back re-enters LoadAsync, so clearing the flag unconditionally here hid the
            // bar while the repair was still going and left the user with no sign of progress.
            // StatusMessage is already guarded the same way at the top of this method.
            if (!_isRepairing) IsLoading = false;
        }
    }

    // ---- Save ----

    private async Task SavePreferencesAsync()
    {
        StatusMessage = null;

        var snapshot = _settings.Current;
        foreach (var section in Sections)
            section.ApplyTo(snapshot);

        await _settings.SaveAsync();

        // Re-apply FFmpeg binary path immediately so scans after saving use the new value.
        ApplyFfmpegFolder(snapshot.FfmpegBinaryFolder);

        StatusMessage = "Settings saved.";
    }

    /// <summary>
    /// Applies the FFmpeg binary folder to FFMpegCore's global options, auto-detecting the
    /// bundled <c>ffmpeg/</c> folder when no explicit path is configured.
    /// </summary>
    /// <param name="configuredFolder">The folder configured in preferences, possibly empty.</param>
    private static void ApplyFfmpegFolder(string configuredFolder)
    {
        var folder = configuredFolder;

        if (string.IsNullOrWhiteSpace(folder))
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
            if (Directory.Exists(candidate))
                folder = candidate;
        }

        if (!string.IsNullOrWhiteSpace(folder))
            GlobalFFOptions.Configure(options => options.BinaryFolder = folder);
    }

    // ---- ISettingsSectionHost ----

    /// <inheritdoc/>
    string? ISettingsSectionHost.StatusMessage
    {
        get => StatusMessage;
        set => StatusMessage = value;
    }

    /// <inheritdoc/>
    Task ISettingsSectionHost.ReloadAsync() => LoadAsync();

    /// <inheritdoc/>
    void ISettingsSectionHost.SetRepairing(bool repairing)
    {
        _isRepairing = repairing;
        IsLoading    = repairing;
    }

    /// <inheritdoc/>
    void ISettingsSectionHost.RequestUnreviewedCountRefresh() => UnreviewedCountRefreshRequested?.Invoke();

    /// <inheritdoc/>
    async Task ISettingsSectionHost.NotifyLibraryRepairedAsync()
    {
        await PreferencesSection.RefreshUnhashedClipCountAsync();

        // A repair is the only thing that hashes, relocates or clears findings, so the attention
        // list is stale the moment one finishes.
        await AttentionSection.RebuildAsync();
    }

    // ---- IAttentionActionHost ----

    /// <summary>
    /// Optional callback invoked when an attention entry asks for a clip to be opened. Set by the
    /// main window, which owns navigation; left null in tests, where the request is a no-op.
    /// </summary>
    public Func<int, bool>? ClipOpenRequested { get; set; }

    /// <inheritdoc/>
    Task IAttentionActionHost.ScanSourceFolderAsync(int sourceFolderId)
        => SourceFoldersSection.ScanFolderAsync(sourceFolderId);

    /// <inheritdoc/>
    void IAttentionActionHost.ShowSourceFolders() => SelectedSection = SourceFoldersSection;

    /// <inheritdoc/>
    bool IAttentionActionHost.OpenClip(int clipId) => ClipOpenRequested?.Invoke(clipId) ?? false;
}
