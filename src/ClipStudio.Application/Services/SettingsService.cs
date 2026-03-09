using System.Text.Json;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Loads and persists application settings from a JSON file in the user's application data folder.
/// Settings are loaded once on construction and written back on each call to <see cref="SaveAsync"/>.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsPath;
    private readonly ILogger<SettingsService> _logger;

    /// <inheritdoc/>
    public AppSettings Current { get; private set; }

    /// <summary>Initializes a new instance of <see cref="SettingsService"/> and loads settings from disk.</summary>
    /// <param name="settingsPath">The absolute path to the JSON settings file.</param>
    /// <param name="logger">Logger instance.</param>
    public SettingsService(string settingsPath, ILogger<SettingsService> logger)
    {
        _settingsPath = settingsPath;
        _logger = logger;
        Current = Load();
    }

    /// <inheritdoc/>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json, cancellationToken);
        _logger.LogDebug("Settings saved to: {Path}", _settingsPath);
    }

    private AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            _logger.LogInformation("No settings file found; using defaults.");
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings; using defaults.");
            return new AppSettings();
        }
    }
}
