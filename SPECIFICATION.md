# ClipStudio - Software Specification

**Version:** 0.2
**Date:** 2026-02-26
**Status:** Draft - Under Review

---

## 1. Vision and Overview

ClipStudio is a cross-platform desktop application for managing, tagging, and curating gameplay video clips recorded via tools such as OBS Studio. It solves the problem of large, unorganized libraries of replay files by giving users a structured tagging system, an inline video player with highlight markers, and flexible search and filtering — so any memorable moment can be found in seconds.

The application is non-invasive: it never modifies source files unless explicitly instructed by the user, and maintains its own metadata database alongside the user's existing clip library.

---

## 2. Target Platforms

- Windows 10/11
- macOS 12+
- Linux (major distributions)

**Framework:** .NET 8+ with Avalonia UI (cross-platform native desktop UI).

---

## 3. Technology Stack

| Layer | Technology | Reason |
|---|---|---|
| UI Framework | Avalonia UI | WPF-like model, true cross-platform, native rendering |
| Language | C# (.NET 8+) | Team and project standard |
| Database | SQLite via Entity Framework Core | Embedded, cross-platform, no server required |
| Video Playback | LibVLCSharp | Cross-platform, wraps VLC, mature and reliable |
| Video Processing | FFMpegCore (.NET wrapper for FFmpeg) | Trimming, thumbnail generation, preview strips |
| File Watching | .NET FileSystemWatcher | Detect new clips arriving in watched folders |
| Game Database | IGDB API (Twitch) | Comprehensive game metadata, cover art, titles |
| OBS Integration | OBS Python Script (distributed separately) | Lives inside OBS, no extra process required |

---

## 4. Core Features

### 4.1 Library Management

- User defines one or more **source folders** where OBS (or other tools) save clips.
- The application **watches these folders** for new files and imports them automatically.
- On import, the app:
  - Generates a **thumbnail** (single frame, configurable time offset).
  - Generates a **preview strip** (series of frames across the clip duration, used for hover scrubbing).
  - Extracts clip metadata: duration, resolution, codec, file size, creation date.
  - Attempts to **parse the game title** from the filename (see Section 6).
  - Sets the clip status to **Unreviewed**.
- Source files are **never moved or modified** by the import process.
- Thumbnails and preview data are stored in the app's own managed data directory.

### 4.2 Tag System

Tags are the central organizing concept of ClipStudio. They are user-defined and apply to both whole clips and to specific time ranges within a clip (Highlights).

#### 4.2.1 Tag Definition

- Tags have: **Name**, **Color**, **Icon** (optional), **Description** (optional).
- Tags belong to a **Type**:
  - `General` - free-form user tags (e.g., "Funny", "Clutch").
  - `Game` - represents a game title, optionally linked to an IGDB entry.

#### 4.2.2 Tag Hierarchy

- A tag may have one **parent tag**, forming a tree structure.
- Example: `Kills` is the parent of `Kill-Streak`, `Double-Kill`, `Headshot`, `Longshot`.
- When searching by a parent tag, results include clips tagged with any descendant tag (configurable).

#### 4.2.3 Tag Relations (Soft Links)

- Tags may be **related** to other tags via a many-to-many soft link (non-hierarchical association).
- Example: `Gameplay` and `PubPush` are related but neither is parent of the other.
- Relations appear as suggestions when tagging and help surface connected content in search.

#### 4.2.4 Tag Application

- Tags can be applied at two levels:
  1. **Clip level** - the tag applies to the clip as a whole.
  2. **Highlight level** - the tag applies to a specific time range within the clip.
- A clip **inherits** all tags from its highlights. A clip tagged `Funny` via a highlight will appear in searches for `Funny`.

#### 4.2.5 Unreviewed State

- Every newly imported clip enters **Unreviewed** status.
- The app provides a dedicated **Unreviewed queue** view (similar to an inbox) to prompt the user to process new clips.
- A clip leaves Unreviewed status when the user explicitly marks it as reviewed, or applies at least one tag.

#### 4.2.6 Game Title Tags

- Game title is a special tag type, not a free-form text field.
- When assigning a game title, the user can:
  - Search the **IGDB game database** and select an official entry (imports cover art and metadata).
  - Define a **custom game title** manually.
- Game title tags participate in the hierarchy and relation system like any other tag.

### 4.3 Highlight Editor

A Highlight is a named, tagged time range within a clip.

- The user opens a clip in the built-in player.
- The timeline displays any existing highlights as colored bands.
- The user sets an **in-point** and an **out-point** to define a highlight range.
- Each highlight has:
  - **Label** (optional short name).
  - **Tags** (one or more, from the tag system).
  - **Notes** (optional free text).
- Highlights can be trimmed and exported independently (see Section 4.4).
- Multiple highlights per clip are supported.
- **Overlapping highlights are explicitly allowed.** Partial overlap (shared time range) and complete containment (one highlight fully inside another) are both treated as independent highlights. They do not merge or affect each other.
- A single highlight accepts multiple tags simultaneously.

### 4.4 Trimming System

Trimming operates on a clip or on a specific highlight range.

#### 4.4.1 Trim Modes

| Mode | Behavior |
|---|---|
| Non-Destructive (default) | Stores in/out points in the database. Original file is untouched. User exports a new file on demand. |
| Destructive | FFmpeg re-encodes or stream-copies to produce a new file. The original is optionally deleted. |

#### 4.4.2 Trim Mode Configuration

- The user sets their **preferred trim mode** in Settings.
- When destructive trim is selected or invoked, the app displays a **two-step warning dialog** clearly stating that the original file will be permanently altered or deleted.
- A separate setting controls whether destructive trim keeps or deletes the original source after export.

#### 4.4.3 Export

- Any highlight or full clip can be exported to a user-specified location as a standalone video file.
- Export uses FFmpeg stream-copy where possible for speed; re-encodes only when format conversion is required.

### 4.5 Built-in Video Player

- Full playback controls: play, pause, seek, volume, speed (0.25x to 4x).
- Timeline scrubbing with highlight bands overlaid.
- Hover over timeline to preview frame (using pre-generated preview strip).
- In/out point markers for active trim or highlight creation.
- Frame-step controls for precise editing.
- Keyboard shortcuts for all player controls.

### 4.6 Search and Filtering

The library view supports composable filters:

| Filter Type | Details |
|---|---|
| Tags | Match clips by tag (any, all, or none). Respects hierarchy (include descendants option). |
| Game Title | Filter by specific game tag. |
| Date Range | Clip creation or import date. |
| Rating | Star rating filter (1-5 stars). |
| Favorites | Boolean toggle. |
| Status | Unreviewed, Reviewed, Archived. |
| Duration | Min/max clip length. |
| Has Highlights | Filter clips that have at least one defined highlight. |

- Filters are combinable (AND logic by default, OR configurable per filter group).
- Saved **filter presets** allow the user to name and recall common search configurations.
- Full text search across clip names, highlight labels, and notes.

### 4.7 Library Views

- **Grid view** - thumbnails with clip title and key metadata badges.
- **List view** - tabular with sortable columns.
- **Unreviewed queue** - filtered view of all Unreviewed clips, designed for efficient triage.
- Hover over a thumbnail to scrub the preview strip.

### 4.8 Clip Detail / Player View

- Dedicated full-screen or split-pane view for a single clip.
- Left: player with timeline and highlight bands.
- Right: metadata panel (tags, game, date, rating, notes, highlights list).
- Inline editing of all metadata without leaving the view.

### 4.9 Screenshot Capture

- While viewing a clip in the player, the user can capture the current frame as a still image.
- Captured screenshots are saved as PNG files to a user-configured output folder.
- Each screenshot is linked to its source clip and playback timestamp in the database.
- Screenshots are accessible from the clip detail view (thumbnail strip of captures).
- Screenshots can be tagged independently using the same tag system.

### 4.10 Supported Formats

ClipStudio targets common gaming and screen-capture formats. Support is provided via LibVLC (playback) and FFmpeg (processing).

**Explicitly supported and tested:**

| Format | Extension | Notes |
|---|---|---|
| MPEG-4 | .mp4 | Most common OBS output (H.264/H.265) |
| Matroska | .mkv | Common OBS lossless/high-quality output |
| QuickTime | .mov | Common on macOS |

**Accepted but not explicitly tested in v1:**

WebM (.webm), AVI (.avi), FLV (.flv), Transport Stream (.ts).

Additional formats may work by virtue of LibVLC and FFmpeg's broad codec support but are not guaranteed. This list will be expanded with explicit testing in future versions.

---

## 5. OBS Integration

### 5.1 Approach: OBS Python Script

To avoid requiring the user to run two separate applications simultaneously, OBS integration is implemented as a **Python script uploaded by the user into OBS Studio's Scripts panel**.

The script:
1. Hooks into OBS's **replay buffer saved** event.
2. At the moment of save, reads the currently active foreground window title to infer the game being played.
3. Renames or re-patterns the saved replay file to embed the detected game name in the filename.
4. The script is distributed as part of ClipStudio (e.g., bundled in an `obs-scripts/` folder) for the user to install once.

### 5.2 Filename Convention

The script appends the detected game name to the OBS filename, producing a pattern such as:

```
Replay 2025-03-03 22-49-45 [Apex Legends].mp4
```

ClipStudio's importer parses this bracket-delimited token and uses it as a **suggested game title tag** during import.

### 5.3 Game Detection

- **Primary method:** Read the active window title at save time. Most fullscreen Steam games use the game name as their window title.
- **Fallback:** If window title matching fails or is ambiguous, the field is left empty.
- Detection is explicitly "best-effort." ClipStudio always prompts the user to **confirm or correct the suggested game title** in the Unreviewed queue before it is committed as a tag.

### 5.4 User Workflow

1. User installs the OBS script once via `Tools > Scripts > +`.
2. OBS records replays with game name embedded in the filename.
3. ClipStudio detects the new file, parses the suggested game name, generates thumbnails.
4. Clip appears in the Unreviewed queue with a suggested game title tag pending confirmation.
5. User confirms or corrects the tag, adds additional tags/highlights, marks as reviewed.

### 5.5 OBS Script: Optional Dependency

The OBS script is entirely optional. ClipStudio functions fully without it. Users who do not use OBS, or who prefer not to install the script, simply tag game titles manually in the Unreviewed queue.

---

## 6. Game Database Integration (IGDB)

- ClipStudio integrates with the **IGDB API** (https://api.igdb.com) for game title lookup.
- When the user creates or confirms a Game tag, a search against IGDB is performed.
- Results show: game title, release year, cover art thumbnail, platform list.
- The user selects an IGDB match or dismisses and enters a custom title.
- Matched Game tags store the IGDB game ID and cover art URL for display purposes.
- IGDB requires a Twitch Developer account for API credentials (free). Users may provide their own credentials in Settings. The app may also ship with a shared key for basic use — both options are supported simultaneously, with user-provided credentials taking priority.

---

## 7. Data Model (Conceptual)

```
SourceFolder
  - Id, Path, IsActive, LastScannedAt

Clip
  - Id, SourceFolderId, FilePath, FileName
  - Duration, Resolution, FileSize, CreatedAt, ImportedAt
  - ThumbnailPath, PreviewStripPath
  - Status (Unreviewed | Reviewed | Archived)
  - Rating (0-5), IsFavorite
  - Notes
  - SuggestedGameName (raw string parsed from filename, pre-confirmation)

Screenshot
  - Id, ClipId, FilePath, CapturedAt
  - PlaybackTimestamp (position in clip when captured)
  - Notes

Tag
  - Id, Name, Color, Icon, Description
  - Type (General | Game)
  - ParentTagId (nullable, self-referencing)
  - IgdbId (nullable), IgdbCoverUrl (nullable)

TagRelation  [many-to-many: Tag <-> Tag]
  - TagId, RelatedTagId

ClipTag  [many-to-many: Clip <-> Tag]
  - ClipId, TagId

Highlight
  - Id, ClipId, Label, Notes
  - StartTime, EndTime
  - CreatedAt

HighlightTag  [many-to-many: Highlight <-> Tag]
  - HighlightId, TagId

ExportJob
  - Id, ClipId, HighlightId (nullable)
  - TrimMode (NonDestructive | Destructive)
  - OutputPath, Status, CreatedAt, CompletedAt

Settings
  - DefaultTrimMode
  - DeleteOriginalOnDestructiveTrim
  - IgdbClientId, IgdbClientSecret
  - PreviewStripFrameCount
  - ThumbnailOffsetSeconds
```

---

## 8. Settings

| Setting | Default | Description |
|---|---|---|
| Default trim mode | Non-Destructive | Applied to new export jobs |
| Delete original after destructive trim | Off | Requires explicit enable + double warning |
| Thumbnail offset | 5 seconds | Frame used for clip thumbnail |
| Preview strip frame count | 20 | Frames in hover-scrub strip |
| IGDB credentials | Empty | User-provided Twitch Dev credentials |
| Watched folders | None | One or more source folders |
| Theme | Dark | Light / Dark / System |
| Screenshot output folder | ~/Pictures/ClipStudio | Destination for frame captures |

---

## 9. Installer and Auto-Update

ClipStudio targets non-technical end users. A polished first-run experience and seamless updates are required.

- **Tool:** Velopack (cross-platform .NET installer + auto-update framework).
- **Installers produced per platform:**
  - Windows: `.exe` setup wizard (NSIS-based via Velopack).
  - macOS: `.dmg` with `.app` bundle.
  - Linux: `AppImage` (single portable executable).
- **First-run setup wizard** guides the user through:
  1. Choosing one or more source (clip) folders.
  2. Optionally installing the OBS script.
  3. Optionally entering IGDB credentials.
  4. Selecting preferred theme.
- **Auto-update:** The app checks for updates on startup (configurable). When an update is available the user is notified and can install it with one click. Update is applied on next launch. No admin rights required on Windows (user-space install).
- Implementation of the installer and update system is deferred to the final stage of v1 development, after core features are stable.

---

## 10. Future Features (Post-v1 Backlog)

These are explicitly out of scope for v1 but are acknowledged as desirable future additions:

- **Social export integrations:** Direct upload to Twitch Clips, YouTube, and Steam game clips from within ClipStudio.
- **Cloud sync / remote library:** Syncing metadata and tags across devices.
- **AI clip detection:** Automatic highlight suggestion based on audio/video signals (e.g., kill sounds, score events).
- **Mobile companion app:** Browse and review library from a mobile device.

---

## 10. Non-Goals (v1 Scope Exclusions)

- Cloud sync or remote storage.
- Multiplayer / social / sharing features.
- Automatic video editing or AI clip detection.
- Support for non-video file types.
- Mobile platform support.
- Deep OBS remote control beyond filename embedding.

---

## 11. Open Items

| # | Topic | Status |
|---|---|---|
| 1 | IGDB credential model - shared key vs. user-provided | Resolved (DEC-012): both supported, user key takes priority |
| 2 | Preview strip generation: on import vs. on-demand | Deferred - revisit after performance benchmarking |
| 3 | Highlight overlap policy | Resolved (DEC-013): overlapping highlights are allowed and independent |
| 4 | OBS script cross-platform game detection method (macOS/Linux) | To design |
| 5 | Archive policy: what does "Archived" status mean for file retention | To decide |
| 6 | Export filename template (user-configurable pattern) | To design |
| 7 | Screenshot tagging - same tag system as clips, or simpler labels only | To decide |
