namespace ClipStudio.Core.Enums;

/// <summary>
/// The game launchers whose installed titles ClipStudio can read.
/// </summary>
public enum GameLauncher
{
    /// <summary>Valve's Steam client. Available on Windows, macOS and Linux.</summary>
    Steam = 0,

    /// <summary>The Epic Games Launcher. Windows only.</summary>
    Epic = 1,

    /// <summary>GOG Galaxy. Windows only.</summary>
    Gog = 2,
}
