using ClipStudio.Application.Models;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Provides access to persisted application settings and manages their lifecycle.
/// </summary>
public interface ISettingsService
{
    /// <summary>Gets the current application settings. Always returns a valid instance.</summary>
    AppSettings Current { get; }

    /// <summary>Persists the current settings to disk asynchronously.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
