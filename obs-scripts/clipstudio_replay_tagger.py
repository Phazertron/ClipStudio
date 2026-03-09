"""
ClipStudio OBS Replay Tagger
============================
An OBS Studio Python script that hooks into the Replay Buffer and renames each
saved replay file to embed the detected active game window title.

Produced filename format (matches ClipStudio's parser):
    Replay 2025-03-03 22-49-45 [Apex Legends].mp4

Installation
------------
1. Open OBS Studio.
2. Go to Tools > Scripts.
3. Click the "+" button and select this file.
4. Configure the settings that appear on the right panel.

Requirements
------------
- OBS Studio 29+ with Python 3.x scripting enabled.
- Windows, macOS, or Linux (game detection is best-effort on each platform).

Notes
-----
- The script is entirely optional; ClipStudio works without it.
- If the active window title cannot be determined, the file is left unmodified.
- The script only processes Replay Buffer saves, not regular recording stops.
"""

import obspython as obs  # type: ignore  (available only inside OBS)
import os
import re
import sys
import platform
import subprocess
import ctypes
import datetime

# ---------------------------------------------------------------------------
# Script metadata
# ---------------------------------------------------------------------------

SCRIPT_VERSION = "1.5.0"
SCRIPT_DESCRIPTION = (
    "<h3>ClipStudio Replay Tagger v{}</h3>"
    "<p>Renames each replay buffer save to embed the detected game name, "
    "producing a filename that ClipStudio can automatically parse.</p>"
    "<p>Example: <code>Replay 2025-03-03 22-49-45 [Apex Legends].mp4</code></p>"
    "<p><b>Detection order:</b> (1) current OBS scene name — name your scenes after "
    "the game you are recording for best results; (2) Game Capture or Window Capture source with "
    "<em>Capture Audio (BETA)</em> enabled — for Game Capture the source name is used as the game title "
    "(name your Game Capture sources after the game); (3) active foreground window title as final fallback.</p>"
    "<p>OBS built-in default names such as <em>Scene</em>, <em>Game Capture</em>, <em>Window Capture</em>, "
    "and <em>Game</em> are automatically ignored so the script always falls through to the next detection "
    "step rather than tagging clips with a meaningless label.</p>"
).format(SCRIPT_VERSION)

# ---------------------------------------------------------------------------
# Script settings (persisted by OBS between sessions)
# ---------------------------------------------------------------------------

# Whether the tagger is active.
_enabled = True

# A comma-separated list of window title substrings that should be treated as
# "unknown game" (e.g., "Desktop", "OBS Studio").  Matches are case-insensitive.
_ignored_titles = "obs studio,desktop,explorer,finder,dock,taskbar"

# Optional: strip this suffix from the detected window title.
# Some games append " - Steam" or similar.  Leave blank to keep the title as-is.
_strip_suffix = ""


def script_description():
    """Return the HTML description shown in OBS Scripts panel."""
    return SCRIPT_DESCRIPTION


def script_defaults(settings):
    """Set default values for all settings."""
    obs.obs_data_set_default_bool(settings, "enabled", True)
    obs.obs_data_set_default_string(
        settings, "ignored_titles",
        "obs studio,desktop,explorer,finder,dock,taskbar"
    )
    obs.obs_data_set_default_string(settings, "strip_suffix", "")


def script_properties():
    """Define the settings UI shown in OBS Scripts panel."""
    props = obs.obs_properties_create()

    obs.obs_properties_add_bool(props, "enabled", "Enable replay tagger")

    obs.obs_properties_add_text(
        props, "ignored_titles",
        "Ignored window titles (comma-separated, case-insensitive)",
        obs.OBS_TEXT_DEFAULT
    )

    obs.obs_properties_add_text(
        props, "strip_suffix",
        "Strip suffix from window title (e.g. \" - Steam\")",
        obs.OBS_TEXT_DEFAULT
    )

    return props


def script_update(settings):
    """Called by OBS when settings change; update module-level variables."""
    global _enabled, _ignored_titles, _strip_suffix
    _enabled        = obs.obs_data_get_bool(settings, "enabled")
    _ignored_titles = obs.obs_data_get_string(settings, "ignored_titles")
    _strip_suffix   = obs.obs_data_get_string(settings, "strip_suffix")


def script_load(settings):
    """Called once when the script is loaded; register the event callback."""
    obs.obs_frontend_add_event_callback(_on_obs_event)
    _log("ClipStudio Replay Tagger loaded (v{}).".format(SCRIPT_VERSION))


def script_unload():
    """Called when the script is unloaded or OBS closes."""
    obs.obs_frontend_remove_event_callback(_on_obs_event)
    _log("ClipStudio Replay Tagger unloaded.")


# ---------------------------------------------------------------------------
# OBS event handler
# ---------------------------------------------------------------------------

def _on_obs_event(event):
    """Handle OBS frontend events and process replay buffer saves."""
    if event != obs.OBS_FRONTEND_EVENT_REPLAY_BUFFER_SAVED:
        return

    if not _enabled:
        _log("Tagger disabled — skipping rename.")
        return

    replay_path = obs.obs_frontend_get_last_replay()
    if not replay_path:
        _log("Could not retrieve last replay path — skipping rename.")
        return

    _log("Replay saved: {}".format(replay_path))

    game_name = _detect_active_game()
    if not game_name:
        game_name = "Just Chatting"
        _log("No game name detected — using default: '{}'.".format(game_name))
    else:
        _log("Detected game: '{}'.".format(game_name))
        
    _rename_replay(replay_path, game_name)


# ---------------------------------------------------------------------------
# Game (window title) detection
# ---------------------------------------------------------------------------

def _detect_active_game() -> str | None:
    """
    Attempt to determine the active game name.

    Detection order:
    1. Current OBS scene name — most reliable when scenes are named after the game.
    2. Window Capture source with "Capture Audio (BETA)" enabled — the last such
       source in the current scene is used. On Windows the source's window property
       has the format "Title:ClassName:Exe"; the title segment is extracted.
    3. Active foreground window title — platform-specific fallback.

    Returns None if no method yields a usable name.
    """
    # ---- 1. Try OBS scene name ----
    scene_name = _get_current_scene_name()
    if scene_name:
        result = _sanitise_candidate(scene_name)
        if result:
            _log("Game detected from OBS scene name: '{}'.".format(result))
            return result
        _log("Scene name '{}' is on the ignore list — trying capture-audio source.".format(scene_name))

    # ---- 2. Window Capture source with capture_audio=true ----
    capture_title = _get_capture_audio_window_title()
    if capture_title:
        result = _sanitise_candidate(capture_title)
        if result:
            _log("Game detected from Window Capture (capture_audio): '{}'.".format(result))
            return result
        _log("Capture-audio window '{}' is on the ignore list — falling back to active window.".format(capture_title))

    # ---- 3. Fallback: active window title ----
    raw_title = _get_active_window_title()
    if not raw_title:
        return None
    result = _sanitise_candidate(raw_title)
    if result:
        _log("Game detected from window title: '{}'.".format(result))
    return result


def _get_capture_audio_window_title() -> str | None:
    """
    Enumerate all sources in the current OBS scene and return the game name of
    the last capture source that has 'Capture Audio (BETA)' enabled.

    Supported source types:
    - "game_capture":   The source *name* set by the user is used as the game title
                        (users typically name game-capture sources after the game).
                        The window property format "[exe.exe]: Window Title" is also
                        parsed as a fallback to extract the window title after ": ".
    - "window_capture": The window property format "Title:ClassName:Exe" on Windows;
                        the first colon-delimited segment is the window title.

    Returns None if no matching source is found or on any OBS API error.
    """
    _SUPPORTED_SOURCE_IDS = ("game_capture", "window_capture")

    try:
        scene_source = obs.obs_frontend_get_current_scene()
        if scene_source is None:
            return None

        scene = obs.obs_scene_from_source(scene_source)
        if scene is None:
            obs.obs_source_release(scene_source)
            return None

        items = obs.obs_scene_enum_items(scene)
        found_title = None

        if items:
            for item in items:
                source = obs.obs_sceneitem_get_source(item)
                if source is None:
                    continue

                source_id = obs.obs_source_get_id(source)
                if source_id not in _SUPPORTED_SOURCE_IDS:
                    continue

                settings = obs.obs_source_get_settings(source)
                if settings is None:
                    continue

                capture_audio = obs.obs_data_get_bool(settings, "capture_audio")
                if capture_audio:
                    title_part = None

                    # Strategy: Use the Source Name first if it's not a generic default
                    source_name = obs.obs_source_get_name(source) or ""
                    
                    if source_id == "game_capture":
                        # If the source name is descriptive, use it.
                        if source_name.strip():
                            title_part = source_name.strip()
                        
                        # Fallback to internal window string if source name failed
                        if not title_part or title_part.lower() == "game capture":
                            window_str = obs.obs_data_get_string(settings, "window") or ""
                            sep = "]: "
                            if sep in window_str:
                                title_part = window_str.split(sep, 1)[1].strip()

                    elif source_id == "window_capture":
                        window_str = obs.obs_data_get_string(settings, "window") or ""
                        title_part = window_str.split(":")[0].strip() if ":" in window_str else window_str.strip()

                    # CRITICAL FIX: Verify the name isn't [game] or generic BEFORE accepting it
                    if title_part:
                        sanitised = _sanitise_candidate(title_part)
                        if sanitised:
                            found_title = sanitised
                            obs.obs_data_release(settings)
                            break # Stop at the first VALID match found

                obs.obs_data_release(settings)

            obs.sceneitem_list_release(items)
        obs.obs_source_release(scene_source)
        return found_title

    except Exception as exc:
        _log("Capture source (capture_audio) detection failed: {}".format(exc))
        return None

def _get_current_scene_name() -> str | None:
    """Return the name of the currently active OBS scene, or None on failure."""
    try:
        scene = obs.obs_frontend_get_current_scene()
        if scene is None:
            return None
        name = obs.obs_source_get_name(scene)
        obs.obs_source_release(scene)
        return name.strip() if name else None
    except Exception as exc:
        _log("OBS scene name lookup failed: {}".format(exc))
        return None


# OBS built-in default source / scene names that are not meaningful as game titles.
# Each pattern matches the bare name ("game capture") OR the name with a trailing
# space + number ("game capture 2"), case-insensitively.
_OBS_DEFAULT_NAME_PATTERNS = tuple(
    re.compile(r"^" + re.escape(name) + r"(\s+\d+)?$", re.IGNORECASE)
    for name in (
        "scene",
        "game",
        "game capture",
        "window capture",
        "display capture",
        "browser",
        "audio input capture",
        "audio output capture",
    )
)


def _sanitise_candidate(raw: str) -> str | None:
    """
    Strip optional suffix, check against the OBS default-name list and the
    user-configurable ignore list, and sanitise a candidate game-name string.
    Returns the cleaned name, or None if the candidate should be rejected.

    Two-stage rejection:
    1. OBS built-in default names (exact / "Name N" pattern, case-insensitive).
       These are never useful game titles regardless of user configuration.
    2. User-configured ``_ignored_titles`` list — substring match, case-insensitive.
    """
    title = raw.strip()

    # Strip optional suffix (e.g. " - Steam")
    if _strip_suffix and title.lower().endswith(_strip_suffix.lower()):
        title = title[: -len(_strip_suffix)].strip()

    # Stage 1: reject OBS built-in default names.
    if any(pat.match(title) for pat in _OBS_DEFAULT_NAME_PATTERNS):
        return None

    # Stage 2: reject user-configured substrings.
    ignored = [t.strip().lower() for t in _ignored_titles.split(",") if t.strip()]
    if any(ig in title.lower() for ig in ignored):
        return None

    sanitised = _sanitise_game_name(title)
    return sanitised if sanitised else None


def _get_active_window_title() -> str | None:
    """
    Platform-specific active window title retrieval.
    Returns the raw window title string, or None on failure.
    """
    system = platform.system()

    if system == "Windows":
        return _get_active_window_title_windows()
    elif system == "Darwin":
        return _get_active_window_title_macos()
    elif system == "Linux":
        return _get_active_window_title_linux()
    else:
        _log("Unsupported platform '{}' — cannot detect game.".format(system))
        return None


def _get_active_window_title_windows() -> str | None:
    """Read the foreground window title using Win32 API via ctypes."""
    try:
        user32 = ctypes.windll.user32  # type: ignore
        hwnd   = user32.GetForegroundWindow()
        if not hwnd:
            return None

        length = user32.GetWindowTextLengthW(hwnd)
        if length == 0:
            return None

        buf = ctypes.create_unicode_buffer(length + 1)
        user32.GetWindowTextW(hwnd, buf, length + 1)
        return buf.value or None
    except Exception as exc:
        _log("Windows title detection failed: {}".format(exc))
        return None


def _get_active_window_title_macos() -> str | None:
    """Read the frontmost application name via osascript."""
    try:
        script = (
            'tell application "System Events" '
            'to get name of first process whose frontmost is true'
        )
        result = subprocess.run(
            ["osascript", "-e", script],
            capture_output=True, text=True, timeout=3
        )
        return result.stdout.strip() or None
    except Exception as exc:
        _log("macOS title detection failed: {}".format(exc))
        return None


def _get_active_window_title_linux() -> str | None:
    """Read the active window name using xdotool (if available)."""
    try:
        result = subprocess.run(
            ["xdotool", "getactivewindow", "getwindowname"],
            capture_output=True, text=True, timeout=3
        )
        return result.stdout.strip() or None
    except FileNotFoundError:
        _log("xdotool not found — install it for Linux game detection.")
        return None
    except Exception as exc:
        _log("Linux title detection failed: {}".format(exc))
        return None


# ---------------------------------------------------------------------------
# Filename manipulation
# ---------------------------------------------------------------------------

_INVALID_CHARS = re.compile(r'[\\/:*?"<>|\[\]]')


def _sanitise_game_name(name: str) -> str:
    """Remove characters that are illegal in filenames or conflict with the bracket syntax."""
    return _INVALID_CHARS.sub("", name).strip()


def _rename_replay(original_path: str, game_name: str) -> None:
    """
    Rename the replay file to embed game_name in square brackets immediately
    before the file extension.

    Before: Replay 2025-03-03 22-49-45.mp4
    After:  Replay 2025-03-03 22-49-45 [Apex Legends].mp4

    If the target filename already contains a bracket token (e.g. the script
    ran twice), it is replaced rather than appended.
    """
    directory   = os.path.dirname(original_path)
    base        = os.path.basename(original_path)
    stem, ext   = os.path.splitext(base)

    # Remove any existing bracket token
    stem_clean = re.sub(r"\s*\[[^\[\]]*\]", "", stem).rstrip()

    new_stem     = "{} [{}]".format(stem_clean, game_name)
    new_filename = new_stem + ext
    new_path     = os.path.join(directory, new_filename)

    if new_path == original_path:
        _log("File already correctly named — no rename required.")
        return

    if os.path.exists(new_path):
        _log("Target file '{}' already exists — skipping rename.".format(new_filename))
        return

    try:
        os.rename(original_path, new_path)
        _log("Renamed: '{}' -> '{}'".format(base, new_filename))
    except OSError as exc:
        _log("Rename failed: {}".format(exc))


# ---------------------------------------------------------------------------
# Logging helper
# ---------------------------------------------------------------------------

def _log(message: str) -> None:
    """Write a timestamped message to the OBS script log."""
    timestamp = datetime.datetime.now().strftime("%H:%M:%S")
    print("[ClipStudio][{}] {}".format(timestamp, message))
