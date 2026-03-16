using ClipStudio.UI.ViewModels;

namespace ClipStudio.UI.Services;

/// <summary>
/// Plays short UI sound effects in response to user-facing events.
/// Respects the <c>SoundEffectsEnabled</c> application setting; calls are
/// silently ignored when the setting is disabled.
/// </summary>
public interface ISoundService
{
    /// <summary>
    /// Plays the specified sound effect asynchronously.
    /// Does nothing when sound effects are disabled in settings.
    /// </summary>
    /// <param name="effect">The sound effect to play.</param>
    void Play(SoundEffect effect);
}
