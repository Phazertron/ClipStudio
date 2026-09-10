# ClipStudio Development Plan

Living tracker for the next development rounds. Tick items as they land, move
finished phases to the bottom. Feature codes (F-R, F-A, ...) match the codes
used in DECISION_LOG.md and the round planning notes.

Baseline at plan creation (2026-09-08): v1.1.1, master, 0 warnings, 118 tests passing.

Status (2026-09-10): 0 warnings, 662 tests passing, working tree clean, pushed to
origin/master. **Phase 0, Phase 1 (F-R and F-A), Phase 1.5 and Phase 1.6 are all
complete** and verified in the running app under `--profile smoke`.

**Start here next.** What remains in Phase 1 is two independent features, plus
three things that need a decision or a measurement rather than effort:

1. `F-F` Import game titles from installed Steam / Epic / GOG libraries
2. `F-I` Grouped search results flyout - UI only, slots in anywhere
3. `1.5.5` The short-clip fallback quality/speed tension - needs the user's call
4. `1.5.5` Parallel imports - the measurement that justified it predates the
   keyframe change and has to be redone before the work is worth starting
5. `1.6.2` Shortcut remapping, and a graphical keyboard map now the set is big
   enough to be worth drawing

The bug and polish backlog below also has a transcription cluster worth one pass,
and the master-volume defect, which is blocked on a diagnosis rather than effort.

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
5. **The app can be driven without a keyboard, which is how Phase 1.5 was verified.**
   Avalonia exposes UI Automation, so PowerShell can navigate and click:
   `Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes`, find the window by
   process id, then find `ControlType.ListItem` for nav items (`SelectionItemPattern`
   to select) and `ControlType.Button` (`InvokePattern` to click). A screenshot of the
   window's `BoundingRectangle` via `Graphics.CopyFromScreen` shows the result.
   **One gotcha that cost time:** a nav item's first Text descendant is the badge
   count, not the label, so match against *every* Text descendant of the item rather
   than `FindFirst`. Buttons whose content is a StackPanel have an empty `Name` for the
   same reason - match their inner Text too.
   Remember to stop the process before rebuilding, or the copy step fails on locked DLLs.

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
  **Settled in 1.5.2 (2026-09-10).** This note used to record that the backfill was not confined
  to an explicit Repair Library, because `App.InitializeServicesAsync` ran `SanitizeAsync()` on
  every startup - roughly 49 seconds of disk work on the 885-clip library on the first launch
  after upgrading, unannounced. That is no longer true: startup runs the cheap health check and
  the backfill belongs to Repair Library alone.
- [x] `ImportService` - hashes before the expensive work, confirms every quick-hash match with a
  full hash, and returns `ImportResult.Duplicate` with both clips without importing. An
  `allowDuplicate` flag covers "import anyway".
- [x] `DuplicateClipDialog` - Skip / Import anyway / Import and link, plus "do the same for the
  other N", since a scan can turn up dozens. "Import and link" was deliberately left out until F-A
  landed rather than holding F-R back to build both together; it landed 2026-09-10.
- [x] Setting `DuplicateDetectionEnabled` (default on), exposed in Settings.
- [x] Tests: hash stability, the head/tail/length sensitivities, a real quick-hash collision,
  a missing candidate file, backfill, and the dialog's result routing through
  `DuplicateImportResolver`.

**F-R is complete as of 2026-09-10.** Verified against real files: a 303 MB clip copied under a
different name produces an identical quick hash in 15 ms, and a different clip does not collide.
The last missing piece, "import and link", landed with F-A - the dialog now offers Skip, Import
anyway and Import and link.

**The wrinkle this left, and how 1.5.4 closed it:** detection compares against stored
hashes, and clips imported before this feature have none, so a duplicate of an older
clip imported without a word. It still does until Repair Library backfills them - but
the library is no longer blind to the result. `IDuplicateClipFinder` compares the
library against itself after the backfill, and what it finds is listed under Attention
required with a merge screen behind it.

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

## Phase 1.5 - Settings overhaul and library health - DONE (2026-09-10)

Agreed 2026-09-09, delivered 2026-09-10. Everything below landed and was verified
in the running app under `--profile smoke`. Tests 493 -> 579, still 0 warnings.

**Why this was a phase rather than two tickets.** Sanitize already found things
the user had to act on - broken clips, highlights whose range falls outside their
clip, and duplicates - and none of them had anywhere to live. They were logged, or
counted in a summary line that scrolled away, or marked on one page only. Settings
also needed a structural pass in its own right. Both are now done, and the second
is what gave the first somewhere to go.

### 1.5.1 Settings restructure - done

- [x] `SettingsView` split into navigable sections. The 731-line flat scroll is now
  a 98-line shell: a section list on the left, the selected section, and one Save bar.
- [x] `SettingsViewModel` split along the same seams: 869 lines down to a shell over
  seven `SettingsSectionViewModel` children - Source Folders, Preferences,
  Transcription, OBS Integration, Attention Required, Maintenance, About. Each reaches
  the page only through `ISettingsSectionHost`, following the
  `BulkEditViewModel` / `IBulkEditHost` pattern from Phase 0.2, and each is tested
  against `FakeSettingsSectionHost`.
- [x] Dialog ownership stayed in the views: the folder pickers, removal dialog,
  duplicate prompt and model manager each moved into the code-behind of the section
  they belong to, leaving `SettingsView.axaml.cs` with only the initial load.

**The shape, for whoever adds a section.** A section maps onto `AppSettings` through
`LoadFrom`/`ApplyTo` and reads anything else through `RefreshAsync`; both are no-ops
on the base class, so a section implements only the half it has. Views resolve through
`ViewLocator` via the `ViewModels.Settings` / `Views.Settings` namespace pair, so a new
section needs no registration - just the matching names.

One thing worth knowing: the repair pass used to disable its own button through the
page-wide `IsLoading`, which a section can no longer see. It now owns
`IsRepairRunning` instead.

### 1.5.2 Split the startup sanitize from the repair - done

`App.InitializeServicesAsync` called the full `SanitizeAsync()` on every launch - a
repair pass doing regeneration, hashing and cache sweeps, unannounced, uncancellable,
with no progress. Roughly 9 seconds on an 885-clip library, growing linearly, plus
about 49 seconds of hashing on the first run after enabling it.

- [x] **Startup is a health check now.** `ILibraryHealthCheckService`: one directory
  listing per source folder compared against a projection of the clip rows.
  **Measured at 16-20 ms on the 23-clip smoke profile**; a listing of the real
  library's 885 files is 0.7 ms cold.
- [x] **An unreachable source folder leaves its clips completely alone** rather than
  marking every one of them broken, so a library on an unplugged drive comes back
  intact. It is reported instead. This is the part of F-P that belonged here; the rest
  of F-P (the removable-drive schema and the drive monitor) is still Phase 2.
- [x] **Findings are typed** (`LibraryHealthFinding`) and the last report is kept on
  the service, so the attention list renders them instead of a log line.
- [x] **Repair Library keeps the rest** - hash backfill, thumbnail and strip
  regeneration, highlight thumbnails, orphan sweeps, the G5 timestamp heuristic.
- [x] **The trap was cleared.** The startup pass was quietly regenerating missing
  thumbnails and strips, so removing it would have traded a slow start for visibly
  broken tiles. `IMediaAssetProvider` now makes a missing thumbnail, strip or highlight
  thumbnail the first time something asks for one, persists it, and dedupes concurrent
  requests so twenty cards of one clip start one FFmpeg run. Verified by deleting a
  cached thumbnail and watching the grid put it back.

Also here: `IClipRepository.GetFileSnapshotsAsync` reads six columns instead of pulling
every clip's tags, players and highlights, which was the other half of what made the old
pass slow. `SetBrokenAsync` flips the flag without loading the row.

### 1.5.3 "Attention required" section - done

One list of everything the library needs a human to resolve. It lives as a Settings
section, and its count badges the Settings nav item so a library that needs something
says so from the first frame rather than on a page nobody is looking at.

- [x] Sources: unreachable source folders and un-imported files from the startup health
  report; broken clips and out-of-range highlights read straight from the library;
  duplicate clips from the last duplicate scan.
- [x] Out-of-range reuses `WatchWindow.Clamp(...).IsUsable` rather than restating it, so
  the list and watch mode cannot drift apart - and it is the drift that locks the player up.
- [x] Each entry names the problem and offers the action that fixes it.
- [x] **Never guess, and this is where it bites.** No entry resolves anything: an
  unreachable folder opens Source Folders rather than choosing between reconnect,
  archive and remove. A test asserts the section never writes to the library at all.
- [x] The backlog item about an out-of-range highlight being findable but not repairable
  is half-closed: the entry now takes you to the clip. A range picker that opens on the
  offending row is still missing - see the backlog.

Two details worth keeping. The report's missing-file findings are deliberately unused:
the clip's broken flag is the same fact and is always current, so a clip relocated since
the check is not still listed. And past eight entries of a kind they collapse into one
counted row, so a library that lost a whole folder does not bury everything else.

### 1.5.4 Duplicate resolution with merge - done

**The gap this closed.** Detection only ran at import, so once two copies were both in
the library nothing ever compared them again. Proven on the smoke profile: two pairs
shared a hash - a Warframe clip and its copy, and a fixture pair - and nothing reported
either.

- [x] `IDuplicateClipFinder` applies the two hashes the import path already uses to the
  library instead of to an incoming file. The stored quick hash screens; every candidate
  group is confirmed by hashing its members in full, so a quick-hash collision between
  different recordings is rejected rather than reported.
- [x] **Repair Library runs it last, after the hash backfill.** Searching first would miss
  exactly the clips that were never hashed, which is the usual way a duplicate gets in
  unnoticed.
- [x] The sanitize summary names what it found, so a repair answers "did it find anything?"
  with no new screens. That was the smallest useful step, and it shipped separately from
  the merge UI.
- [x] The merge screen shows every copy side by side with its path, import time, tags,
  players, highlights and rating, and the user picks the survivor.
- [x] **Tags and players reuse the bulk-edit chips** rather than a second way of
  reconciling them: shared purple, partial red, promoted yellow, and only promoted chips
  are written. Because that model drops an unpromoted chip, the screen counts what is at
  stake and offers "Keep them all" - explicit, but one click from the common intent.
- [x] **Highlights are a selection, not a merge.** Every range is valid against the
  survivor, so they start ticked. Identical range-and-label pairs across copies collapse
  into one row, so the merge cannot create a duplicate range on the survivor.
- [x] Removal is destructive and comes last - metadata is written first, so a failed
  removal still leaves the survivor complete. Trash, not delete, and the confirmation
  says it is restorable for 30 days.

**One decision made while building this.** The finder's groups live in memory, so after a
restart nothing is listed until something looks again. Making the user run a full repair
for that would be a poor trade, so the attention section has its own "Look for duplicates"
button. It is deliberately separate from "Re-check": the re-check is directory listings,
this reads every candidate file end to end.

### 1.5.5 Import performance

Measured, not assumed, throughout. **Correction to an earlier suggestion in this file:**
"read the clip once into RAM and serve every step from it" does not work. FFMpegCore runs
`ffmpeg`/`ffprobe` as external processes that open the file by path; they cannot be handed
a buffer, and piping a 300 MB video through stdin would be worse and would break the
seeking that thumbnails and strips need.

- [x] Drop the redundant probe: the duration is passed in from the metadata already read.
- [x] **The strip was the whole cost, and it is fixed.** It decoded every frame of the clip -
  ~10,800 for a 180s recording - to keep 20 of them, because the `fps` filter drops frames
  only after the decoder has produced them. Decoding keyframes at the demuxer instead
  (`-discard nokey`) took it from 6,114 ms to 280 ms on the same file. A guard falls back to
  a full decode when the file has fewer keyframes than the strip has tiles.
  End to end, cold, on real 300 MB clips: **9,538 ms -> 2,141 ms per clip, 4.5x**.
  On the real library (`E:\Clip stream\#LastAdded`, 885 clips, 409 GB - read only, never
  modified): **21,699 ms -> 2,773 ms per clip, 7.8x**. Its clips are 170 Mbps HEVC
  2560x1080 at 60fps, so a 24-second clip is half a gigabyte.
  Keyframe density there is about one every two seconds, so the guard only rejects clips
  under roughly 40 seconds - **8% of a 78-clip duration sample**. On the rejected ones,
  `-hwaccel auto` cuts the full decode from 17.4s to 10.2s and `-threads 4` makes it
  *worse* (22.8s), so neither is a substitute for keyframe-only decoding.
- [x] **Hashing was a separate cost and is fixed too.** The keyframe change did nothing for
  it: hashing read 8 MB from each end of every clip, costing 570 ms on a spinning drive.
  Reduced to 1 MB, measured at 55 ms - tenfold, and it applies to Repair Library's backfill
  as much as to import. Safe by construction: identical bytes always hash identically, so a
  smaller chunk can only produce false positives, which the full hash rejects. Zero
  collisions across 124 clips of the real library at 1 MB, and none at 256 KB either.
- [x] **The keyframe guard asks the right question now (2026-09-10).** It needs one bit -
  are there at least as many keyframes as tiles - but was counting every keyframe in the
  file to get it. `HasAtLeastKeyframesAsync` stops reading the moment it has seen enough.
  Measured cold against cold on the real library, two disjoint interleaved sets so the size
  distribution matches: **2,194 ms -> 847 ms per clip, 2.6x, about 1.35 s saved on every
  import.** The saving grows with keyframe density - on a 186 MB clip with 375 keyframes,
  8,030 ms -> 537 ms, 15x. Clips too sparse to take the fast path read the whole index
  either way and still fall back correctly.
- [x] Combining the thumbnail and strip into one `ffmpeg` pass is **not** worth it. The
  thumbnail costs ~360 ms because `-ss` seeks straight to the frame, while a combined pass
  has to reach that timestamp through the filter graph. Measured slower than the two
  separate passes. Closing this rather than leaving it open: it was tried and rejected.

**What is left, and why neither is just effort:**

- [ ] **The short-clip fallback is a quality/speed tension that needs a decision, not code.**
  A 24s clip with 13 keyframes takes the full decode to guarantee 20 distinct tiles. Two ways
  out, neither chosen: relax the guard and accept 13 distinct tiles out of 20 (a silent
  quality loss, which is why it was not just done), or store the actual tile count per clip so
  a shorter strip can be rendered honestly - that needs a schema change and the hover-scrub
  mapping updated. **This is the user's call.**
- [ ] **Parallel imports need re-measuring before the work starts.** Four-way parallel strips
  measured ~1.6x faster than sequential (1,136 ms vs 723 ms, reproducible with roles swapped),
  but that was taken when the keyframe probe still cost about 2.2 s per clip. Roughly 1.35 s
  of that is now gone, so the shape of the remaining per-clip time has changed and the 1.6x
  no longer describes what would be gained. Re-measure first.
  **The blocker was never I/O, it is EF Core:** `DbContext` is not thread-safe and
  `ImportService` takes a scoped repository, so parallel imports need a scope per file and a
  rethink of how progress and results are collected. There is also a correctness hazard worth
  naming before anyone starts: `ImportService` creates tags and players as a side effect of
  import (GameTagAlias, auto-applied "Me" player), so two files carrying the same new game
  name could race and create it twice. That needs solving, not just parallelising.

## Phase 1.6 - Background activity and keyboard shortcuts - DONE (2026-09-10)

Agreed with the user after Phase 1.5, from two requests: make running work visible
with the settings gear spinning while something is going, and consider a keybind
map. Tests 597 -> 615, 0 warnings.

### 1.6.1 Background activity

Long-running work reported into whichever page started it, so nothing running was
visible from anywhere else, and a finished summary was a single string on a
one-line bar that ran off the edge of the window - which is the screenshot that
started this.

- [x] `IBackgroundTaskService`, a singleton registry every long-running operation
  announces itself into: folder scans, library repair, duplicate scan,
  transcription and the export run.
- [x] An activity indicator docked at the bottom of the sidebar, reachable from
  every page. It names what is running and opens a panel of running plus recently
  finished work, where a completion message is shown in full and wrapped.
- [x] The owning page's navigation icon spins while its work runs.
- [x] Repair, duplicate scan and folder scans are cancellable.
- [x] Export keeps its own page. Export jobs are user-created and persisted, unlike
  a scan or a repair; the registry only makes the run visible from elsewhere.

**The spinner machinery already existed** on every navigation item and was only
wired to Library during watcher imports, so most of that request was wiring.
`HasBackgroundTask` is deliberately separate from `IsScanning`: the watcher sets
the latter for a moment when a file lands, and the two would clear each other.

**Cancellation was almost free.** Repair, the duplicate scan and folder scans all
threaded a `CancellationToken` already - nothing was ever passing one, which is
exactly what made the old startup pass impossible to stop. Two rules came out of
wiring it: a cancelled task says what it *kept* rather than reading as a failure,
and a cancelled duplicate scan reports no groups at all, because a partial list
reads as "these are the duplicates" when it is not.

**Two truncation bugs, one of them mine:**

- The settings status line sat in a horizontal `StackPanel`, which measures its
  children with infinite width, so `TextWrapping` on it did nothing. Introduced in
  1.5.1. A `Grid` with a `*` column fixes it.
- The repair's completion message was scraped from `IProgress`, which posts
  asynchronously, so it kept whichever line had landed rather than the summary - in
  practice "Confirming 2 possible duplicates...". `SanitizeAsync` returns its
  summary now. **Worth remembering: never scrape a result from a progress sink.**

### 1.6.2 Keyboard shortcuts

The request was a graphical keybind map. The honest answer was that the map was not
the useful part yet: there were five shortcuts, hardcoded in a `switch` in
`ClipDetailView.axaml.cs`, and **no list of them anywhere** - so a map would have
been a second copy that drifted the first time one was added. Agreed with the user
to build the registry and expand the shortcut set first.

- [x] `KeyboardShortcuts` is the one source the handler and the help list both read.
  Each entry carries a stable id separate from its keys.
- [x] The handler is a registry lookup rather than a switch over keys. The focus
  guards that stop a shortcut stealing a keystroke from a text box are unchanged.
- [x] 5 shortcuts became 19: Escape closes the clip, Alt+Left/Right move between
  clips, Alt+Up/Down between highlights, F favourites, R marks reviewed, 0-5 rate,
  C toggles subtitles.
- [x] A read-only Keyboard shortcuts section in Settings, grouped by category.

**Alt carries clip navigation** because the bare arrows seek, and seeking is far
more frequent, so it keeps the unmodified key.

**Still open, and deliberately so:**

- [ ] **Remapping.** The stable ids exist so a stored preference has something to
  key on. Nothing else needs to change to add it.
- [ ] **A graphical keyboard map.** Worth revisiting now the set is large enough to
  be worth drawing - and a natural fit for the in-app wiki at v1.4.0, which already
  lists keyboard shortcuts as coverage.

---

### F-A - Link clips - DONE (2026-09-10)

- [x] `ClipLink` entity (SourceClipId, TargetClipId, LinkType, Note, CreatedAt) + `ClipLinkType`
  enum (SameMoment, Sequel, Reaction, Variant)
- [x] Migration `Round15ClipLinks`, unique index on (Source, Target, Type), plus an index on the
  target end because links are read from both sides
- [x] `IClipLinkRepository` / `ClipLinkRepository`
- [x] `IClipLinkService` / `ClipLinkService` - create, remove, list for clip (both directions),
  inverse wording for the directional types
- [x] Related clips panel in clip detail - card list with a relationship badge, click to navigate
- [x] Link picker: search by name from the detail view, and `OpenPickerForAsync` pre-fills it with
  a known clip
- [x] Trash and delete handling, logged as DEC-049
- [x] Tests: bidirectional listing, cascade on delete, the trash round trip, the inverse wording of
  every type, and the picker's exclusions. 615 -> 662 passing.

**Two decisions worth knowing (DEC-048, DEC-049).**

A link is stored **once**, in the direction it was created, and read from both ends. `TagRelation`
stores both directions; links deliberately do not, because two of the four types are directional
and storing both ways would leave no way to tell which clip is the sequel. The service resolves the
label from the side being asked - the clip you marked as a Sequel shows its origin as a Prequel -
so no view has to know that rule. The duplicate check looks both ways, because the unique index
only covers the stored direction.

**Trashing a clip hides its links; restoring brings them back.** This is a deliberate departure
from the line in this plan, which said "restore does not recreate them". That conflated the two
deletions the application has. Trash is reversible for 30 days, so discarding the link would
silently lose relationships with no way to know what they were. Permanent deletion still cascades:
a link to a clip that no longer exists is a dangling row, not a relationship.

**"Import and link" closed F-R.** The duplicate dialog has a third button beside Skip and Import
anyway. Duplicates are linked as `Variant`, since the user has deliberately kept two entries for
one recording. The link is a second, separate write: failing to draw it is logged but never undoes
an import the user already got.

**Verified in the running app** under `--profile smoke`: the migration applied cleanly, a link made
through the picker landed in the database, and it renders and navigates from both ends - the target
clip shows the link back with the source clip's thumbnail and game tag.

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

**Partly landed in 1.5.2.** The startup health check already detects an unreachable
source folder and reports it under Attention required, and - importantly - leaves its
clips alone rather than marking every one of them broken. What is left here is the
schema that tells a removable drive from a missing folder, and the monitor that reacts
while the app is running.

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
- [ ] **An out-of-range highlight can be seen but not repaired in place.** Half-closed by 1.5.3:
  it is marked in the list, refused for watching, skipped by the queue, counted in the sanitize
  summary, and now listed under Attention required with an entry that opens its clip. What is
  still missing is the last step - an editor that opens the row with its range ready to re-pick.
  Until then the only route is deleting and recreating it.
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
