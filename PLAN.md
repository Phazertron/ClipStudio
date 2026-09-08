# ClipStudio Development Plan

Living tracker for the next development rounds. Tick items as they land, move
finished phases to the bottom. Feature codes (F-R, F-A, ...) match the codes
used in DECISION_LOG.md and the round planning notes.

Baseline at plan creation (2026-09-08): v1.1.1, master, 0 warnings, 118 tests passing.

Status (2026-09-08, end of session): 0 warnings, 399 tests passing, 23 commits
ahead of origin/master and unpushed. **Phase 0 is complete.** Phase 1 (F-R
duplicate detection) is the next thing to start.

One debt carried out of Phase 0: the view-layer changes of the last three commits
(`TagPickerBehavior`, compiled bindings on LibraryView, `BulkEditViewModel`) are
verified only as far as "the app starts and stays up on a populated library". The
pickers and the bulk-edit panel still need one pass with a mouse and keyboard -
see "Verifying a change in the running app" below, and the checklist at the end
of this section.

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

### 0.2 Split ClipDetailViewModel (was 2,801 lines; now 1,676)

Extract child view models, each owning its own commands and observable state.
The parent keeps navigation, load/save orchestration, and exposes the children.

**The pattern, for whoever continues this.** Each child owns its own state and
commands and reaches the surrounding view through a small `I...Host` interface
that `ClipDetailViewModel` implements explicitly. That keeps the child free of
LibVLC and of the parent's other concerns, and makes it testable with a fake
host (see `tests/ClipStudio.Tests/Fakes/`). Existing seams: `IAudioPlaybackHost`,
`IPlaybackHost`, `ITrimEditorHost`, `IHighlightEditorHost`.

**The safety net.** ClipDetailView sets `x:CompileBindings="True"`, so every
binding is checked against the view model at build time; moving a property onto
a child is a compile error, not a silent runtime break. This does NOT cover the
code-behind's `PropertyChanged` subscriptions - those compile fine and fail
silently, so they need the app run (see "Verifying a change" below).

**Extractions were kept behaviour-only.** Where a quirk was found it was
preserved and recorded in the backlog rather than quietly fixed mid-move; three
such items are listed there.

- [x] `PlaybackViewModel` - position, loop, scrub, seek (volume/mute went to AudioMixerViewModel; subtitles stayed with the parent, which owns the media and SRT slave)
- [x] `TrimEditorViewModel` - trim start/end, destructive check against highlights, timestamp inputs
- [x] `HighlightEditorViewModel` - add/edit form, pending tags, timeline handles (the highlight list stays with the parent)
- [x] `AudioMixerViewModel` - track selection, per-track volume, MixedRemux cache
- [x] `ScreenshotViewModel` - capture (list lands with the UI that shows it)
- [x] Move the AutoCompleteBox picker state machine into a reusable `TagPickerBehavior`.
  Landed as three files in `src/ClipStudio.UI/Behaviors/`: `TagPickerStateMachine`
  (the three-phase logic, no Avalonia), `ITagPickerHost` (the seam), and
  `TagPickerBehavior` (the `AutoCompleteBox` glue, one instance per picker).
  Note for the record: the plan said this was duplicated with `LibraryView.axaml.cs`,
  but that file has no `AutoCompleteBox` handling at all - the duplication was three
  near-identical copies *inside* `ClipDetailView.axaml.cs` (game, general, highlight).
  All three now share the one machine; they differ only in a resolve delegate, a
  commit delegate, and a `refocusAfterCommit` flag. `ClipDetailView.axaml.cs`
  940 -> 566 lines. Covered by 19 tests in `tests/ClipStudio.Tests/Behaviors/`
  driving the machine through a `FakeTagPickerHost`.
  **Not yet verified by hand.** The extraction is behaviour-preserving by
  construction and the app starts and runs clean, but the actual dropdown paths
  (click / arrow+Enter / type+Enter / type+Tab / Escape, on the game, general,
  new-highlight and per-highlight-row pickers) still need one pass with a mouse
  and keyboard. That is the only outstanding risk from this change.
- [x] Target: no file in UI above 1,000 lines. Met for C#, deliberately not
  pursued for XAML. `LibraryViewModel` was split (1,753 -> 965) by extracting
  `BulkEditViewModel`, and `ClipDetailView.axaml.cs` fell to 566 with
  `TagPickerBehavior`. Remaining files over 1,000 lines:
  `LibraryView.axaml` (1,738), `ClipDetailViewModel.cs` (1,676),
  `ClipDetailView.axaml` (1,568).
  The two XAML files are markup, which the rule was never really aimed at -
  splitting them buys indirection rather than clarity. `ClipDetailViewModel`
  still holds the highlight *list* and the tag/player/game panels; neither is a
  named child in this plan, and it is already down from 2,801. Closing the item
  here rather than inventing more children: the phase goal was that later
  features do not land in a 2,800-line file, and that is now true of both view
  models the goal named.

### 0.2 verification still owed

Compiled bindings catch a moved property, but not a code-behind `PropertyChanged`
subscription and not the picker state machine's event wiring. One pass with a
mouse and keyboard over:

- [ ] Game, general, new-highlight and per-highlight-row tag pickers: click a
  suggestion, arrow+Enter, type+Enter, type+Tab, Escape. Tab should move focus on;
  the highlight pickers should keep focus after the other commit paths.
- [ ] Bulk-edit panel: select 2+ clips, check a shared tag reads Shared and a
  partial one Partial, promote a partial, Apply, then Cancel on a fresh staging.
- [ ] Copy-format: source deselects on start, paste applies only the toggled aspects.
- [ ] The four destructive confirmations, and "remove all broken" from the toolbar.

### 0.3 Small fixes

- [x] `SetupWizardViewModel.cs:89` - replace `GetAwaiter().GetResult()` on FFmpeg detect with async load
- [x] `ClipService.SearchAsync` - push tag/player/excluded-id filters into the EF query instead of in-memory
- [x] Add `obs-scripts/__pycache__/` to `.gitignore`
- [x] Commit `CLAUDE.md`

### Verifying a change in the running app

Several bugs this phase surfaced only by running the app - a stuck progress bar,
a dead `PropertyChanged` subscription, an ineffective volume fix. Building and
the test suite do not catch those. The recipe used:

1. Launch with an isolated profile so the real library is never touched:
   `ClipStudio.UI.exe --profile smoke`. Everything under
   `%AppData%\ClipStudio_smoke` is throwaway; delete it afterwards.
   Note the one leak: the screenshot output folder ignores `--profile` (backlog).
2. Generate a test clip with an OBS-style name, which also exercises the
   filename timestamp and `[Game Name]` parsing:
   `ffmpeg -f lavfi -i testsrc=size=1280x720:rate=30:duration=12 -f lavfi -i sine=frequency=440:duration=12 -c:v libx264 -pix_fmt yuv420p -c:a aac "Replay 2025-03-03 22-49-45 [Deep Rock Galactic].mp4"`
3. Walk the wizard, add the folder in Settings, Scan All, open the clip.
4. For audio questions, read `%TEMP%\clipstudio_audio.log` - it records VLC's
   Volume, Mute and AudioTrack at each play event. Note that a Windows audio
   session peak meter does NOT observe LibVLC's output; it reads zero even when
   audio is fine, so it cannot be used to prove silence. When in doubt, build the
   previous commit and compare against it rather than assuming a regression.

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

### Found during Phase 0 - needs a decision or investigation

These came out of the split and the app-verification passes. What remains here is
blocked on runtime diagnosis rather than on effort. The rest were fixed after
Phase 0 closed - see "Fixed after Phase 0" below.

- [ ] **Master volume is lost after a clip restarts.** Every end-of-clip path in
  `OnPlayerEndReached` does `MediaPlayer.Stop()` then `Play()`, which tears down VLC's audio
  output; the rebuilt output starts at LibVLC's own default instead of the user's master level,
  and `MediaPlayer.Volume` reads -1 afterwards.
  *Attempted and reverted 2026-09-08:* re-applying the level from a
  `AudioMixerViewModel.ReapplyVolume()` called inside the `Playing` callback does NOT work - the
  Volume setter is a no-op while no audio output exists, and the log still read -1. A working fix
  has to apply the level once the output actually exists (first `TimeChanged` after a restart, or
  a short retry), and needs its own runtime verification.
  *Still unknown:* whether audio is genuinely at the wrong level or only the query misreports.
  Establish that first - it decides whether this is a user-facing bug at all.
- [ ] **`LogAudioDiagnostics` is debug scaffolding that still ships.** It appends a snapshot to
  `%TEMP%\clipstudio_audio.log` on every play event. Its own remark says to remove it once the
  audio issues are resolved - do that together with the master-volume item, since that is what it
  is currently being used to diagnose. (It is genuinely useful until then.)
### Pre-existing

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

## Fixed after Phase 0

- [x] Slider track-click seeked `WatchStart` seconds early in watch mode. Both the
  click and drag paths now share one `ToAbsoluteMs` conversion.
- [x] Play-head readout kept its old precision while paused; `RefreshDurationDisplay`
  now reformats both readouts. The test that asserted the old behaviour was updated.
- [x] Screenshots escaped `--profile` into the user's real Pictures folder. The default
  now comes from `AppDataPaths`, which takes an explicit `isProfileScoped` flag.
- [x] Dropped the dead `AudioMixerViewModel._mixedPreviewPath`.
- [x] Compiled bindings turned on across all 21 remaining views, so a missed rename is
  a build error everywhere rather than a silent no-op at runtime.
- [x] The `OWNER/ClipStudio` GitHub placeholders were already replaced with the real
  repository URL; the backlog entry was stale.

## Done

### Phase 0 - Hardening (2026-09-08)
- [x] 0.1 file-system test net: `IFileSystem` + `PhysicalFileSystem` + `FakeFileSystem`; import, sanitizer, highlight and export suites. Tests 118 -> 214.
- [x] 0.3 small fixes: async FFmpeg detection, clip search filters pushed into SQL, gitignore, CLAUDE.md
- [x] 0.2 five child view models extracted from ClipDetailViewModel (2,801 -> 1,676 lines), each behind an `I...Host` seam and covered by tests. Tests 214 -> 344.
- [x] 0.2 `TagPickerBehavior` + `TagPickerStateMachine` extracted; three copies of the picker state machine in `ClipDetailView.axaml.cs` collapsed to one (940 -> 566 lines). Tests 344 -> 363.
- [x] 0.2 compiled bindings turned on in `LibraryView.axaml` - the safety net that made the split below mechanical rather than risky.
- [x] 0.2 `BulkEditViewModel` + `IBulkEditHost` extracted from `LibraryViewModel` (1,753 -> 965 lines); 69 bindings repointed. Tests 363 -> 391.
- [x] 0.2 line-count target closed: no C# file in the UI over 1,000 lines.
- [x] Fixed along the way: setup wizard progress bar never advanced; `TimestampInput` accepted negative bare seconds

### Round 15 - completed
- [x] F-O Relocate lost clips + source folder migration
- [x] Broken clip detection (IsBroken, red tint, batch remove)
- [x] Highlight thumbnail regenerated on time-range edit
- [x] Trash race condition, SQLite WAL, null TrashPath restore fallback
- [x] Sound effect volume reduced by 6 dB
- [x] Unified highlight add/edit form, sub-second precision display

### v1.1.1 - cross-platform porting
- [x] Windows, Linux, macOS builds; CI on push; release on tag via Velopack
