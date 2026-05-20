using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipStudio.UI.Views;

/// <summary>
/// View model for <see cref="TranscriptionSetupDialog"/>.
/// Manages the list of available Whisper models, download progress, backend selection,
/// and the final model path selection that is saved to <see cref="ISettingsService"/>.
/// </summary>
public sealed partial class TranscriptionSetupDialogViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly HttpClient _http;
    private CancellationTokenSource? _downloadCts;

    // ---- Backend ----

    /// <summary>Gets the list of backend options shown in the ComboBox.</summary>
    public IReadOnlyList<string> BackendOptions { get; } = new[] { "Auto (recommended)", "CPU only", "Vulkan (GPU)" };

    /// <summary>Gets or sets the selected backend display string.</summary>
    [ObservableProperty]
    private string _selectedBackend = "Auto (recommended)";

    // ---- Language ----

    /// <summary>Gets the list of languages available in the language picker.</summary>
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } = BuildLanguageOptionsList();

    /// <summary>Gets or sets the selected language option.</summary>
    [ObservableProperty] private LanguageOption _selectedLanguage = BuildLanguageOptionsList()[0];

    // ---- Models ----

    /// <summary>Gets the list of model rows available for download or selection.</summary>
    public ObservableCollection<TranscriptionModelRowViewModel> Models { get; } = new();

    /// <summary>Gets or sets whether any model is currently being downloaded.</summary>
    [ObservableProperty] private bool _isDownloading;

    /// <summary>Gets or sets the download progress (0 – 1).</summary>
    [ObservableProperty] private float _downloadProgress;

    /// <summary>Gets or sets a status message shown during or after download.</summary>
    [ObservableProperty] private string _downloadStatus = string.Empty;

    // ---- Browse ----

    /// <summary>Gets or sets the path chosen via the Browse file picker.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(SelectedModelDisplay))]
    private string _browsedModelPath = string.Empty;

    // ---- Result ----

    /// <summary>Gets the final model path (either downloaded or browsed) selected by the user.</summary>
    public string ResolvedModelPath { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether the user may click OK.</summary>
    public bool CanConfirm => !string.IsNullOrWhiteSpace(ResolvedModelPath);

    /// <summary>Gets a short display label for the selected model.</summary>
    public string SelectedModelDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ResolvedModelPath))
                return $"Selected: {Path.GetFileName(ResolvedModelPath)}";
            return "No model selected";
        }
    }

    /// <summary>Raised when the user confirms the dialog (OK button).</summary>
    public event Action? Confirmed;

    /// <summary>Raised when the user cancels or closes without confirming.</summary>
    public event Action? Cancelled;

    /// <summary>
    /// Initialises a new <see cref="TranscriptionSetupDialogViewModel"/>.
    /// </summary>
    public TranscriptionSetupDialogViewModel(ISettingsService settings)
    {
        _settings = settings;
        _http     = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        // Populate model table with known GGML Whisper model variants.
        var modelsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipStudio", "models");

        foreach (var def in KnownModels())
        {
            var localPath = Path.Combine(modelsFolder, def.FileName);
            var row = new TranscriptionModelRowViewModel(def.Name, def.SizeDisplay, def.Url, localPath);
            row.Selected          += OnModelSelected;
            row.DownloadRequested += OnDownloadRequested;
            row.UninstallRequested += OnUninstallRequested;
            Models.Add(row);
        }

        // Pre-select the model already configured in settings.
        var configured = settings.Current.TranscriptionModelPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            SetResolved(configured);

        // Pre-populate language from settings.
        var savedCode = settings.Current.TranscriptionLanguage;
        SelectedLanguage = LanguageOptions.FirstOrDefault(o => o.Code == savedCode)
                           ?? LanguageOptions[0];

        SelectedBackend = settings.Current.TranscriptionBackend switch
        {
            TranscriptionBackend.Cpu    => "CPU only",
            TranscriptionBackend.Vulkan => "Vulkan (GPU)",
            _                           => "Auto (recommended)"
        };
    }

    /// <summary>
    /// Applies the selected settings to <see cref="ISettingsService"/> and saves.
    /// Called by the dialog code-behind when the user clicks OK.
    /// </summary>
    public async Task ConfirmAsync()
    {
        var s = _settings.Current;
        s.TranscriptionModelPath = ResolvedModelPath;
        s.TranscriptionLanguage  = SelectedLanguage.Code;
        s.TranscriptionBackend   = SelectedBackend switch
        {
            "CPU only"      => TranscriptionBackend.Cpu,
            "Vulkan (GPU)"  => TranscriptionBackend.Vulkan,
            _               => TranscriptionBackend.Auto
        };
        s.TranscriptionEnabled   = true;
        await _settings.SaveAsync();
        Confirmed?.Invoke();
    }

    /// <summary>Sets the model path after the view code-behind resolves it from the file picker.</summary>
    public void SetBrowsedPath(string path)
    {
        BrowsedModelPath = path;
        SetResolved(path);
    }

    /// <summary>Cancels any ongoing download and raises <see cref="Cancelled"/>.</summary>
    public void Cancel()
    {
        _downloadCts?.Cancel();
        Cancelled?.Invoke();
    }

    // ---- Internals ----

    private void OnModelSelected(TranscriptionModelRowViewModel row)
    {
        foreach (var m in Models)
            m.IsSelected = m == row;
        SetResolved(row.LocalPath);
    }

    private void OnUninstallRequested(TranscriptionModelRowViewModel row)
    {
        try
        {
            if (File.Exists(row.LocalPath))
                File.Delete(row.LocalPath);

            row.IsDownloaded = false;
            row.StatusText   = "Not downloaded";

            // If this was the selected model, clear the selection.
            if (row.IsSelected)
            {
                row.IsSelected    = false;
                ResolvedModelPath = string.Empty;
                OnPropertyChanged(nameof(CanConfirm));
                OnPropertyChanged(nameof(SelectedModelDisplay));
            }

            DownloadStatus = $"{row.Name} uninstalled.";
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Uninstall failed: {ex.Message}";
        }
    }

    private async void OnDownloadRequested(TranscriptionModelRowViewModel row)
    {
        if (IsDownloading) return;

        _downloadCts   = new CancellationTokenSource();
        IsDownloading  = true;
        DownloadStatus = $"Downloading {row.Name}...";
        DownloadProgress = 0f;

        try
        {
            var dir = Path.GetDirectoryName(row.LocalPath)!;
            Directory.CreateDirectory(dir);

            using var response = await _http.GetAsync(row.Url, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token);
            response.EnsureSuccessStatusCode();

            var total  = response.Content.Headers.ContentLength ?? 0L;
            var buffer = new byte[81920];
            var written = 0L;

            await using var src  = await response.Content.ReadAsStreamAsync(_downloadCts.Token);
            await using var dest = File.Create(row.LocalPath);

            int read;
            while ((read = await src.ReadAsync(buffer, _downloadCts.Token)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read), _downloadCts.Token);
                written += read;
                if (total > 0)
                    DownloadProgress = (float)written / total;
            }

            row.IsDownloaded = true;
            row.StatusText   = "Downloaded";
            DownloadStatus   = $"{row.Name} downloaded.";
            OnModelSelected(row);
        }
        catch (OperationCanceledException)
        {
            DownloadStatus = "Download cancelled.";
            if (File.Exists(row.LocalPath)) File.Delete(row.LocalPath);
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Download failed: {ex.Message}";
            if (File.Exists(row.LocalPath)) File.Delete(row.LocalPath);
        }
        finally
        {
            IsDownloading    = false;
            DownloadProgress = 0f;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    private void SetResolved(string path)
    {
        ResolvedModelPath = path;
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(SelectedModelDisplay));
    }

    private static IEnumerable<(string Name, string FileName, string SizeDisplay, string Url)> KnownModels()
    {
        const string baseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";
        yield return ("tiny",     "ggml-tiny.bin",     "75 MB",   baseUrl + "ggml-tiny.bin");
        yield return ("base",     "ggml-base.bin",     "142 MB",  baseUrl + "ggml-base.bin");
        yield return ("small",    "ggml-small.bin",    "466 MB",  baseUrl + "ggml-small.bin");
        yield return ("medium",   "ggml-medium.bin",   "1.5 GB",  baseUrl + "ggml-medium.bin");
        yield return ("large-v3", "ggml-large-v3.bin", "3.1 GB",  baseUrl + "ggml-large-v3.bin");
    }

    /// <summary>
    /// Builds the full list of language options shared between the setup dialog and the Settings page.
    /// </summary>
    public static IReadOnlyList<LanguageOption> BuildLanguageOptionsList()
    {
        return new[]
        {
            new LanguageOption("auto",  "Auto-detect"),
            new LanguageOption("en",    "English"),
            new LanguageOption("zh",    "Chinese"),
            new LanguageOption("de",    "German"),
            new LanguageOption("es",    "Spanish"),
            new LanguageOption("ru",    "Russian"),
            new LanguageOption("ko",    "Korean"),
            new LanguageOption("fr",    "French"),
            new LanguageOption("ja",    "Japanese"),
            new LanguageOption("pt",    "Portuguese"),
            new LanguageOption("tr",    "Turkish"),
            new LanguageOption("pl",    "Polish"),
            new LanguageOption("ca",    "Catalan"),
            new LanguageOption("nl",    "Dutch"),
            new LanguageOption("ar",    "Arabic"),
            new LanguageOption("sv",    "Swedish"),
            new LanguageOption("it",    "Italian"),
            new LanguageOption("id",    "Indonesian"),
            new LanguageOption("hi",    "Hindi"),
            new LanguageOption("fi",    "Finnish"),
            new LanguageOption("vi",    "Vietnamese"),
            new LanguageOption("he",    "Hebrew"),
            new LanguageOption("uk",    "Ukrainian"),
            new LanguageOption("el",    "Greek"),
            new LanguageOption("ms",    "Malay"),
            new LanguageOption("cs",    "Czech"),
            new LanguageOption("ro",    "Romanian"),
            new LanguageOption("da",    "Danish"),
            new LanguageOption("hu",    "Hungarian"),
            new LanguageOption("ta",    "Tamil"),
            new LanguageOption("no",    "Norwegian"),
            new LanguageOption("th",    "Thai"),
            new LanguageOption("ur",    "Urdu"),
            new LanguageOption("hr",    "Croatian"),
            new LanguageOption("bg",    "Bulgarian"),
            new LanguageOption("lt",    "Lithuanian"),
            new LanguageOption("lv",    "Latvian"),
            new LanguageOption("sl",    "Slovenian"),
            new LanguageOption("sk",    "Slovak"),
        };
    }
}
