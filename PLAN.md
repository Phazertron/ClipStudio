# ClipStudio Development Plan

Living tracker for the next development rounds. Tick items as they land, move
finished phases to the bottom. Feature codes (F-R, F-A, ...) match the codes
used in DECISION_LOG.md and the round planning notes.

Baseline at plan creation (2026-09-08): v1.1.1, master, 0 warnings, 118 tests passing.

---

## Phase 0 - Hardening before new features

Goal: put a safety net under the import pipeline and shrink the two view models
that every later feature would otherwise grow. Phases 1 and 2 both touch import
and the clip detail view, so this comes first.

### 0.1 Tests for the file-system layer

- [x] `ImportServiceTests` - metadata capture, bracket game-name parsing, GameTagAlias auto-apply, Unreviewed status, duplicate path handling
- [x] `LibrarySanitizerServiceTests` - missing thumbnail/strip regeneration, IsBroken cleared on rediscovery, orphan cache deletion, trash subfolder ghost cleanup
- [x] `HighlightServiceTests` - overlap allowed, thumbnail regenerated on time-range edit
- [x] `ExportServiceTests` - job queue ordering, failure marks job and continues
- [x] Introduce `IFileSystem` abstraction (or use System.IO.Abstractions) so the above run without touching disk
- [x] Target: tests above 150

### 0.2 Split ClipDetailViewModel (2,800 lines, 60 commands)

Extract child view models, each owning its own commands and observable state.
The parent keeps navigation, load/save orchestration, and exposes the children.

Groundwork done: ClipDetailView now sets `x:CompileBindings="True"`, so every
binding is checked against the view model at build time. Moving a property onto
a child view model is now a compile error rather than a silent runtime break.

Order by blast radius (XAML + code-behind references): audio 14/0,
highlights 42/53, trim 49/40. Audio is the smallest but touches the fragile
LibVLC ordering rules in DECISION_LOG (SetAudioTrack on initial load, explicit
MediaPlayer.Volume) - verify by running the app, not just by building.

- [x] `PlaybackViewModel` - position, loop, scrub, seek (volume/mute went to AudioMixerViewModel; subtitles stayed with the parent, which owns the media and SRT slave)
- [x] `TrimEditorViewModel` - trim start/end, destructive check against highlights, timestamp inputs
- [ ] `HighlightEditorViewModel` - add/edit form, pending tags, timeline handles
- [x] `AudioMixerViewModel` - track selection, per-track volume, MixedRemux cache
- [x] `ScreenshotViewModel` - capture (list lands with the UI that shows it)
- [ ] Move the AutoCompleteBox picker state machine into a reusable `TagPickerBehavior`
- [ ] Target: no file in UI above 1,000 lines

### 0.3 Small fixes

- [x] `SetupWizardViewModel.cs:89` - replace `GetAwaiter().GetResult()` on FFmpeg detect with async load
- [x] `ClipService.SearchAsync` - push tag/player/excluded-id filters into the EF query instead of in-memory
- [x] Add `obs-scripts/__pycache__/` to `.gitignore`
- [x] Commit `CLAUDE.md`

---

## Phase 1 - Round 15 remaining features

Order chosen so that each feature builds on the previous one: duplicates need a
hash, links need duplicates as their first use case, the Games page import is
independent, and grouped search is UI-only and can slot in anywhere.

### F-R - Duplicate clip detection

- [ ] `Clip.FileHash` (string, nullable, indexed) + migration `Round15FileHash`
- [ ] `IFileHashService` / `FileHashService` - SHA-256, streamed, cancellable; hash only first + last N MB plus file size for speed, full hash on collision
- [ ] Backfill hashes for existing clips in `LibrarySanitizerService` (opt-in progress step)
- [ ] `ImportService` - after metadata, look up hash; if match found raise `DuplicateDetected` with both clips
- [ ] `DuplicateClipDialog` - Skip / Import anyway / Import and link (link requires F-A)
- [ ] Setting `DuplicateDetectionEnabled` (default on)
- [ ] Tests: hash stability, collision path, dialog result routing

### F-A - Link clips

- [ ] `ClipLink` entity (SourceClipId, TargetClipId, LinkType, Note, CreatedAt) + `ClipLinkType` enum (SameMoment, Sequel, Reaction, Variant)
- [ ] Migration `Round15ClipLinks`, unique index on (Source, Target, Type)
- [ ] `IClipLinkRepository` / `ClipLinkRepository`
- [ ] `IClipLinkService` / `ClipLinkService` - create, remove, list for clip (both directions), type inverse handling (Sequel <-> Prequel display)
- [ ] `RelatedClipsPanelView` in clip detail - card list, link type badge, click to navigate
- [ ] Link picker: search by name from detail view, pre-filled when coming from F-R dialog
- [ ] Trash: deleting a clip removes its links; restore does not recreate them (log decision)
- [ ] Tests: bidirectional listing, cascade on delete

### F-F - Import game titles from installed libraries

- [ ] `IInstalledGameScanner` abstraction with one implementation per launcher
- [ ] `SteamLibraryScanner` - parse `libraryfolders.vdf` + `appmanifest_*.acf`, gives AppId + name (also fills GameStoreAppId + cover URL for free)
- [ ] `EpicLibraryScanner` - parse `ProgramData/Epic/EpicGamesLauncher/Data/Manifests/*.item`
- [ ] `GogLibraryScanner` - registry `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games` on Windows; skip elsewhere
- [ ] Platform guards: Steam paths for Windows/Linux/macOS; Epic and GOG Windows only
- [ ] `GamesPage` - "Import from launchers" button, preview list with checkboxes, merge-by-name into existing Game tags
- [ ] Tests: VDF and ACF parser with fixture files

### F-I - Grouped search results flyout

- [ ] `ISearchService` / `SearchService` - single query returns `SearchResultGroup[]` (Clips, Highlights, Tags, Players, Notes)
- [ ] Debounced flyout under the Library search bar; keyboard navigation; Enter on a clip opens editor, Enter on a tag applies it as filter
- [ ] Reuse `SearchCaptions` flag to add a Captions group (also closes the missing filter-panel toggle)
- [ ] Tests: group population, empty query returns nothing

---

## Phase 2 - Round 16

### F-S - Tag and game import-export

- [ ] `TagExportDocument` JSON schema (version field, tags with parent-by-name, relations, game aliases, optional players)
- [ ] `ITagExportService` - export all / export selection; import with merge-by-name and conflict report
- [ ] Settings page: Export / Import buttons with summary dialog
- [ ] Tests: round trip, merge with existing names, unknown schema version rejected

### F-P - Removable drive auto-archive

- [ ] `SourceFolder.IsRemovableDrive` + `VolumeSerialNumber` + migration
- [ ] `IDriveMonitor` abstraction; Windows implementation via WMI `Win32_VolumeChangeEvent`; Linux/macOS polling fallback
- [ ] On drive removal: mark clips of that folder as Archived (not Broken); on reinsert: restore previous status
- [ ] Settings toggle per source folder

---

## Bug and polish backlog

Not tied to a phase. Pull from here when a session has spare time or when a
phase touches the same area.

- [ ] Trash: sanitizer should detect clips moved to the system trash outside the app and drop their DB record (only on explicit sanitize run, never proactively)
- [ ] Source watcher: new file in a source folder does not badge the Settings nav item; add auto-import setting
- [ ] Player suggestions missing from some tag/player autocompletes
- [ ] Tag suggestions not available in bulk edit mode
- [ ] Transcription: model uninstall UI
- [ ] Transcription: language picker human-readable
- [ ] Transcription: drop base/small models from the picker (assessed as not useful)
- [ ] Transcription: translation runs after recognition, should be a separate opt-in step
- [ ] Installer: remove the "hide" option on the Velopack setup window
- [ ] Master volume is lost after a clip restarts. Every end-of-clip path in
  `OnPlayerEndReached` does `MediaPlayer.Stop()` then `Play()`, which tears down VLC's audio
  output; the rebuilt output starts at LibVLC's own default instead of the user's master level,
  and `MediaPlayer.Volume` reads -1 afterwards. Investigated 2026-09-08: re-applying the level
  from `AudioMixerViewModel.ReapplyVolume()` inside the `Playing` callback does NOT work - the
  Volume setter is a no-op while no audio output exists, and the log still reads -1. The fix has
  to apply the level once the output actually exists (first `TimeChanged` after a restart, or a
  short retry), so it needs its own runtime verification pass. Not yet confirmed whether audio is
  genuinely at the wrong level or only the query misreports.
- [ ] Clicking the position slider track in watch mode seeks to the wrong place. The play-head
  setter treats its value as an absolute media position, but in watch mode the slider is relative
  to the highlight start, so a track-click lands `WatchStart` seconds early. Dragging is fine -
  a drag is settled by `EndScrub`, which does convert. Carried over unchanged when the transport
  was extracted, and marked in `PlaybackViewModel.OnPositionSecondsChanged`.
- [ ] The play-head readout keeps the old precision until the next player update, so entering a
  trim or highlight edit while paused leaves it at `m:ss` while the duration already reads
  `m:ss.f`. Pre-existing; `PlaybackViewModel.RefreshDurationDisplay` deliberately preserves it.
- [ ] `LogAudioDiagnostics` is debug scaffolding that still ships: it appends a snapshot to
  `%TEMP%\clipstudio_audio.log` on every play event. Its own comment says to remove it once the
  audio issues are resolved - do that together with the master-volume item above, since that is
  what it is currently being used to diagnose.
- [ ] `AudioMixerViewModel._mixedPreviewPath` is assigned in three places and never read. Dead
  since before the extraction, moved across verbatim to keep that change behaviour-only. Drop it,
  or start using it (the natural use is skipping a regeneration when the requested mix already
  matches the loaded preview).
- [ ] Screenshot output folder is not profile-scoped: with `--profile` set, captures still land in `Pictures\ClipStudio` (found while smoke-testing the wizard)

---

## Done

### Round 15 - completed
- [x] F-O Relocate lost clips + source folder migration
- [x] Broken clip detection (IsBroken, red tint, batch remove)
- [x] Highlight thumbnail regenerated on time-range edit
- [x] Trash race condition, SQLite WAL, null TrashPath restore fallback
- [x] Sound effect volume reduced by 6 dB
- [x] Unified highlight add/edit form, sub-second precision display

### v1.1.1 - cross-platform porting
- [x] Windows, Linux, macOS builds; CI on push; release on tag via Velopack
