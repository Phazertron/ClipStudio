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
- [ ] `LibrarySanitizerServiceTests` - missing thumbnail/strip regeneration, IsBroken cleared on rediscovery, orphan cache deletion, trash subfolder ghost cleanup
- [ ] `HighlightServiceTests` - overlap allowed, thumbnail regenerated on time-range edit
- [ ] `ExportServiceTests` - job queue ordering, failure marks job and continues
- [x] Introduce `IFileSystem` abstraction (or use System.IO.Abstractions) so the above run without touching disk
- [x] Target: tests above 150

### 0.2 Split ClipDetailViewModel (2,800 lines, 60 commands)

Extract child view models, each owning its own commands and observable state.
The parent keeps navigation, load/save orchestration, and exposes the children.

- [ ] `PlaybackViewModel` - LibVLC player, position, volume, mute, loop, subtitles toggle
- [ ] `TrimEditorViewModel` - trim start/end, destructive check against highlights, timestamp inputs
- [ ] `HighlightEditorViewModel` - add/edit form, pending tags, timeline handles
- [ ] `AudioMixerViewModel` - track selection, per-track volume, MixedRemux cache
- [ ] `ScreenshotViewModel` - capture and list
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
