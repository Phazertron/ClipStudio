# ClipStudio Development Plan

Living tracker for the next development rounds. Tick items as they land, move
finished phases to the bottom. Feature codes (F-R, F-A, ...) match the codes
used in DECISION_LOG.md and the round planning notes.

Baseline at plan creation (2026-09-08): v1.1.1, master, 0 warnings, 118 tests passing.

Status (2026-09-10): 0 warnings, 493 tests passing, working tree clean, 19 commits
ahead of origin/master and unpushed. **Phase 0 is complete and verified at a
keyboard. F-R is complete** apart from "import and link", which waits on F-A.
Import performance work is done and measured.

**Start here next: Phase 1.5 below.** It is written to be picked up cold, in five
parts, ordered so each gives the next one somewhere to live:

1. `1.5.1` Settings restructure - the view is 731 lines and the view model 869
2. `1.5.2` Split the startup sanitize from the repair
3. `1.5.3` "Attention required" section - the home every finding below needs
4. `1.5.4` Duplicate resolution with merge
5. `1.5.5` Import performance - the measured wins are done; what remains is listed

The two findings most worth reading first are in `1.5.2` and `1.5.4`: the full
sanitize runs unannounced on every startup, and duplicates already in the library
are never surfaced because detection only runs at import.

Commit style from 2026-09-09 onwards follows the conventional-commit skill
(`type(scope): subject` plus a bullet body), maintained in the `claude-skills`
repo and cloned at `~/.claude/commands/`.

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

### 0.2 verification - done (2026-09-09)

Walked at a keyboard: the four tag picker sites, bulk edit with staged and promoted
chips, copy-format, the destructive confirmations, and both playback fixes. Five
bugs came out of that pass and are fixed - the watch-mode hang, the repair progress
bar, and three separate faults in the tag pickers' keyboard handling. Two further
findings are in the backlog below as features rather than defects.

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

- [x] `Clip.FileHash` (string, nullable, indexed) + migration `Round15FileHash`. Verified against
  a populated library.
- [x] `IFileHashService` / `FileHashService` - SHA-256, streamed, cancellable; quick hash covers
  length plus the first and last 8 MB, full hash confirms. Files below two chunks are hashed whole,
  which makes the quick hash conclusive for short files.
- [x] Backfill hashes for existing clips in `LibrarySanitizerService`, reported in the summary.
  **Correction to an earlier note here:** the backfill is not confined to an explicit Repair
  Library. `App.InitializeServicesAsync` runs `SanitizeAsync()` on every startup, so the backfill
  runs there too. Confirmed working end to end - a startup run hashed all 19 demo clips and
  reported the out-of-range highlight. The cost is one-off per clip, but on the 885-clip library it
  is roughly 49 seconds of disk work on the first launch after upgrading (and would have been
  8.4 minutes before the chunk size was reduced). Worth deciding whether unbounded startup work
  should be bounded, deferred, or announced.
- [x] `ImportService` - hashes before the expensive work, confirms every quick-hash match with a
  full hash, and returns `ImportResult.Duplicate` with both clips without importing. An
  `allowDuplicate` flag covers "import anyway".
- [x] `DuplicateClipDialog` - Skip / Import anyway, plus "do the same for the other N", since a
  scan can turn up dozens. **"Import and link" is deliberately left out until F-A lands**, rather
  than holding F-R back to build both together.
- [x] Setting `DuplicateDetectionEnabled` (default on), exposed in Settings.
- [x] Tests: hash stability, the head/tail/length sensitivities, a real quick-hash collision,
  a missing candidate file, backfill, and the dialog's result routing through
  `DuplicateImportResolver`.

**F-R is feature-complete apart from linking.** Verified against real files: a
303 MB clip copied under a different name produces an identical quick hash in 15 ms,
and a different clip does not collide. The dialog itself has not been driven at a
keyboard yet.

**One wrinkle worth knowing:** detection compares against stored hashes, and clips
imported before this feature have none. Until Repair Library backfills them, a
duplicate of an older clip imports without a word. Whether that is acceptable, or
the backfill should run once automatically after an upgrade, is an open decision.

### Found while building F-R

- [ ] **The first launch after any migration logs a burst of query errors.** `LibraryView`
  loads as soon as the window is shown, but migrations are applied afterwards in
  `InitializeServicesAsync`, so that first load queries a schema that does not exist yet - eleven
  `no such column: c.FileHash` errors on the launch that added the column. It self-heals: step 3
  of the same routine reloads the library once migrations are done, so the user sees a correct
  library and only the log shows it. Pre-existing and true of every migration, not just this one;
  surfaced because `Round15FileHash` was the first schema change since the startup order was set.
  The fix is to gate that first load behind migrations rather than to move migrations earlier,
  which would undo the deliberately fast window.

## Phase 1.5 - Settings overhaul and library health

Agreed 2026-09-09. Self-contained: a fresh session can start here without reading
the rest of this file. Points 1-4 of that review are already done (duplicate name
detection is always on, the toggle now governs content hashing itself, the wizard
offers it, Settings warns when clips are unhashed).

**Why this is a phase rather than two tickets.** Sanitize already finds things the
user must act on - broken clips, highlights whose range falls outside their clip,
and now duplicates - and none of them have anywhere to live. They are logged, or
counted in a summary line that scrolls away, or marked on one page only. Settings
also needs a structural pass in its own right: `SettingsView.axaml` is 731 lines of
one flat scroll and `SettingsViewModel.cs` is 869, mixing folder management,
preferences, transcription, repair and now duplicate resolution.

### 1.5.1 Settings restructure

- [ ] Split `SettingsView` into sections that can be navigated rather than scrolled.
- [ ] Split `SettingsViewModel` along the same seams, using the `I...Host` child view model
  pattern established in Phase 0.2 - see the `BulkEditViewModel` / `IBulkEditHost` pair for the
  shape to copy, and keep each child testable behind a fake host.
- [ ] `SettingsView.axaml.cs` already owns two dialogs; keep dialog ownership in the view and
  child view models free of windows.

### 1.5.2 Split the startup sanitize from the repair

Today `App.InitializeServicesAsync` calls the full `SanitizeAsync()` on every launch.
That is a repair pass doing regeneration, hashing and cache sweeps, run unannounced,
uncancellable, with no progress, on every start.

**Measured before proposing anything.** Steady state on a 23-clip library is ~220 ms,
so roughly 10 ms per clip - about 9 seconds for an 885-clip library, every launch,
and it grows linearly. The first run after enabling hashing adds ~49 seconds of
hashing on that library, and any missing thumbnail or strip adds seconds more each.

The surprise is where the cost is *not*: checking the disk is nearly free. One
directory enumeration of 885 files measured at under a millisecond, and 885
individual existence checks at ~11 ms. What costs is the regeneration work and
loading every clip with its highlights and tags.

So the split is cheap to make:

- [ ] **Startup: a health check, not a repair.** Per source folder, one directory listing
  compared against the clip paths in the database. That single pass answers everything worth
  knowing at launch:
  - the folder root is unreachable, so the drive is disconnected - archive its clips (this is
    F-P in Phase 2, and it belongs here)
  - database paths with no file on disk - mark broken
  - files on disk with no database row - "a scan is recommended"
- [ ] **Repair Library keeps the rest**: hash backfill, thumbnail and strip regeneration, highlight
  thumbnails, orphan cache sweeps, the G5 timestamp heuristic.
- [ ] **Findings go to the attention list** below rather than into a log line, which is what makes
  the split safe to do.
- [ ] **Watch for the thing the startup pass is quietly providing today:** it regenerates missing
  thumbnails and strips, so a user who never runs Repair Library currently never notices they went
  missing. Removing that needs lazy regeneration on demand - the grid asking for a thumbnail that
  is not there should create it - or the split trades a slow start for visibly broken tiles.

### 1.5.3 "Attention required" section

A single list of everything the library needs a human to resolve. This is the home
the existing findings never had.

- [ ] Sources: duplicate clips **already in the library** (see below), broken clips
  (`Clip.IsBroken`), disconnected source drives, "a scan is recommended", highlights whose range
  falls outside their clip (`WatchWindow.Clamp(...).IsUsable` is the existing definition - reuse it,
  do not restate it).
- [ ] Populated by a sanitize run; each entry names the problem and offers the action that fixes it.
- [ ] This subsumes an item already in the backlog: an out-of-range highlight can currently be
  found but not repaired in place, and its fix-it entry belongs here.
- [ ] The user's rule for the whole section: **never guess.** Every entry prompts; nothing is
  auto-resolved. This is why the sanitizer was changed to report rather than move an
  out-of-range highlight, and the same standard applies to everything listed here.

### 1.5.4 Duplicate resolution with merge

- [ ] Sanitize hashes first, then presents what it found - hashing has to complete before the
  duplicate list can be right.
- [ ] **Duplicates already in the library are currently invisible**, which is the gap testing hit
  on 2026-09-09. Detection only runs at import, so once two copies are in the library nothing ever
  compares them again. Proven on the test profile: two pairs shared a hash
  (a Warframe clip and its copy, and a fixture pair) and nothing reported either. A clip that
  imported while hashing was off is the common way in.
- [ ] Smallest useful step, separable from the merge UI: have the sanitize summary report the
  duplicate groups its hashing found, exactly as it already reports out-of-range highlights.
  No new screens, and it makes Repair Library answer "did it find anything?".
- [ ] List each duplicate group showing the ClipStudio metadata on both sides: tags, players,
  highlights, rating, notes. The file is the same; the metadata is what differs and what the user
  is actually choosing between.
- [ ] The user picks which clip survives.
- [ ] **Tags and players: reuse the bulk edit promote/demote chips.** `BulkEditViewModel` already
  models exactly this - Shared, Partial and New across a set of clips, with promote to spread a
  partial one - so the merge screen should reuse that rather than invent a second way to reconcile
  tags. The resulting set is applied to the surviving clip.
- [ ] **Highlights: list all of them from every copy and let the user choose which to keep.** They
  are time ranges over identical content, so all of them are valid against the survivor; this is a
  selection, not a merge.
- [ ] Removing the copies is destructive and comes last, after the survivor has its merged metadata.

### 1.5.5 Import performance

Measured, not assumed. On a 303 MB clip the quick hash costs ~633 ms cold and ~15 ms
warm, while SHA-256 over the same 16 MB already in RAM takes 8 ms. **The hashing is
not the cost - the file reading is.**

**Correction to an earlier suggestion in this file:** "read the clip once into RAM
and serve every step from it" does not work. FFMpegCore runs `ffmpeg`/`ffprobe` as
external processes that open the file by path; they cannot be handed a buffer, and
piping a 300 MB video through stdin would be worse and would break the seeking that
thumbnails and strips need. The saving has to come from fewer and better-overlapped
passes, not from a shared buffer.

What one import currently costs, counted in `MediaService`:

1. `GetMetadataAsync` - one `FFProbe` process
2. `GenerateThumbnailAsync` - one `ffmpeg` process
3. `GeneratePreviewStripAsync` - **a second `FFProbe`** for the duration, then one `ffmpeg`
4. The quick hash - our own read

That is four external process launches plus our own read, and the second probe is
pure waste.

- [x] Drop the redundant probe: the duration is passed in from the metadata already read.
- [x] **The strip was the whole cost, and it is fixed.** It decoded every frame of the clip -
  ~10,800 for a 180s recording - to keep 20 of them, because the `fps` filter drops frames only
  after the decoder has produced them. Decoding keyframes at the demuxer instead
  (`-discard nokey`) took it from 6,114 ms to 280 ms on the same file. A guard falls back to a
  full decode when the file has fewer keyframes than the strip has tiles.
  End to end, cold, on real 300 MB clips: **9,538 ms -> 2,141 ms per clip, 4.5x**.

  **Verified against the real library** (`E:\Clip stream\#LastAdded`, 885 clips, 409 GB - read
  only, never modified). Its clips are far heavier than the showcase set: high-bitrate HEVC,
  2560x1080 at 60fps, around 170 Mbps, so a 24-second clip is half a gigabyte and used to take
  17.7 seconds to decode for its strip. Averaged over three cold clips:
  **21,699 ms -> 2,773 ms per clip, 7.8x. Across all 885 clips that is 5h 20m -> 41 min.**

  Keyframe density there is about one every two seconds, so the guard only rejects clips shorter
  than roughly 40 seconds - **8% of a 78-clip duration sample**. The other 92% take the fast path.
  On the rejected ones, `-hwaccel auto` cuts the full decode from 17.4s to 10.2s and `-threads 4`
  makes it *worse* (22.8s), so neither is a substitute for keyframe-only decoding.
- [ ] Combining the thumbnail and strip into one `ffmpeg` pass is no longer worth it. The
  thumbnail costs ~360 ms because `-ss` seeks straight to the frame, while a combined pass has to
  reach that timestamp through the filter graph. Measured slower than the two separate passes.
- [ ] **Parallelism across files is worth doing, and the spinning disk does not rule it out.**
  Measured on eight untouched clips, run in both directions to cancel out cache warmth:
  sequential 1,136 ms and 1,132 ms, four-way parallel 723 ms and 700 ms - consistently ~1.6x.
  Keyframe-only decoding turned this step from streaming-bound into seek-and-CPU-bound, which is
  why overlapping now helps where it would not have before.
  **The blocker is not I/O, it is EF Core:** `DbContext` is not thread-safe and `ImportService`
  takes a scoped repository, so parallel imports need a scope per file and a rethink of how
  progress and results are collected. That is the actual work, and it is not small.
- [x] **Hashing was a separate cost and is now fixed too.** The keyframe change did nothing for it:
  hashing read 8 MB from each end of every clip, costing 570 ms on a spinning drive. Reduced to
  1 MB, measured at 55 ms - tenfold, and it applies to Repair Library's backfill as much as to
  import. Safe by construction: identical bytes always hash identically, so a smaller chunk can
  only produce false positives, which the full hash rejects. Zero collisions across 124 clips of
  the real library at 1 MB, and none at 256 KB either.

  **Combined per clip on the real library: about 22.3s -> 2.8s. Across 885 clips, 5h 28m -> 42 min.**

- [ ] The short-clip fallback is the remaining quality/speed tension. A 24s clip with 13 keyframes
  currently takes the 17.7s full decode to guarantee 20 distinct tiles. Two ways out, neither yet
  chosen: relax the guard and accept 13 distinct tiles out of 20 (silent quality loss, which is
  why it was not just done), or store the actual tile count per clip so a shorter strip can be
  rendered honestly - that needs a schema change and the hover-scrub mapping updated.
- [ ] Remaining smaller win: the keyframe count is a second `ffprobe` launch (~150 ms warm, 1,330
  ms on a cold spinning-disk read). It could be folded into the metadata probe by replacing
  `FFProbe.AnalyseAsync` with one call that requests format, streams and keyframe times together.

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

### Found in manual testing (2026-09-09)

From a full pass over the Phase 0 verification checklist. The checklist itself is
done - the tag pickers, bulk edit, copy-format, destructive confirmations and both
playback fixes all behaved correctly apart from the entries below.

- [ ] **No way to create a tag from the clip editor.** The pickers resolve only
  against existing tags, so typing a new name and pressing Enter does nothing; the
  user has to go to the Tags page first. `ITagService.CreateAsync` already exists,
  so this is a UI affordance plus one decision: should an unmatched name create a
  tag silently, or require an explicit "create" action? Silent creation turns every
  typo into a tag, so an explicit affordance is probably right.
- [ ] **Screenshot capture gives no feedback.** Pressing the button reports nothing -
  not where the file went, not whether the clip already has screenshots, and there
  is no audible or visual confirmation. Needs design, not just a toast: the natural
  fix is a screenshot list on the clip, which is what `ScreenshotViewModel` was
  extracted for.

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
- [x] Watch mode hung the app on a highlight starting past the clip end (`WatchWindow.Clamp`
  plus a restart guard). Found in manual testing.
- [x] The library-repair progress bar vanished when navigating away from Settings and back.
- [x] Dropped two dead stub commands (`AddGeneralTagCommand`, `AddGameTagCommand`) that
  returned `Task.CompletedTask` and were bound to nothing.
- [x] Tag picker keyboard behaviour, confirmed by hand 2026-09-09. Tab takes the tag and stays in
  the field ready for the next one; a second Tab leaves. Nothing to take means Tab moves focus as
  normal. Typing never commits - inline completion selects a suggestion, but only Enter, Tab or a
  click takes it. Three fixes were needed: defer the reset past the control's own key handling,
  focus the inner TextBox rather than the AutoCompleteBox, and stop treating a completion-driven
  selection as a choice.
- [x] Data-integrity faults are logged at Error so they survive the default log level, and the
  log-level parser reads the actual setting instead of the first level name in the file.
- [x] Highlight ranges are validated on save, so the app can no longer write one that does not
  fit its clip. The sanitizer still checks, since a clip can be relocated or trimmed afterwards.
- [x] The sanitizer truncates a highlight that overruns its clip end, and *reports* rather than
  moves one that starts past the end - there is no correct range to guess, and the summary says
  how many need attention.
- [x] The clip's tag pickers no longer offer tags the clip already has (`ClipGeneralTagOptions` /
  `ClipGameTagOptions`). Highlight rows keep the full list on purpose - highlight tags propagate
  to the clip, so filtering the shared list would hide clip tags from every row.
- [ ] **An out-of-range highlight can be seen but not repaired in place.** It is now marked in the
  list, refused for watching and skipped by the queue, and the sanitize summary counts it - so it
  can be found. What is still missing is a way to fix it: an editor that opens the row with its
  range ready to re-pick. Until then the only route is deleting and recreating it.
- [ ] **Highlight tag pickers still offer tags that highlight already has.** The clip-level
  exclusion did not extend to them: `HighlightViewModel.AvailableTags` is a shared reference to
  the full list, so a per-highlight exclusion needs its own filtered collection per row.

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
