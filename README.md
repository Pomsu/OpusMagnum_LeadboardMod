# OpusMagnum_LeadboradMod

A [Quintessential](https://github.com/QuintessentialOM/Quintessential) mod for Opus Magnum that shows the community
leaderboard <https://zlbb.faendir.com> inside the game, drawn by the game's own histogram code in its original style, and
(only after you confirm) submits your solutions to it. Not affiliated with the zlbb maintainers.

## AI disclosure
This mod was developed with heavy AI assistance. The code, the tests, the documentation and the code reviews were written by
[Claude Code](https://claude.com/claude-code) (Anthropic's AI coding assistant; the commits carry a `Co-Authored-By` line). The
human author (Pomsu) set the goals and the design decisions, tested the mod in the game, reads and decides what is published, and is
the contact for questions. Please mention this when you share the mod. If you find a bug, it may well be the AI's - please open an issue.

## Responsible use of zlbb
zlbb's API is open to the community on the condition that it is used responsibly (confirmed by its maintainer):
- no heavy load: one full download on the first start, then one small diff per game start, nothing while playing; a problem on the
  player's PC (unwritable cache folder, a snapshot that cannot be stored) never leads to repeated full downloads;
- **nothing is ever submitted without the player pressing SUBMIT** (there is deliberately no automatic submitting);
- the author name should identify a person, e.g. a Reddit or Discord username - never the mod or the game. Everyone uses their own name.

## What it does
- **Histograms from zlbb.** The cost / cycles / area (or instructions) panels on the puzzle screen and the "machine
  finished" screen show the best 50% of zlbb's record-setting solutions, with a fixed bar width and tidy axes. Your own
  score marker and your rank ("#12 of 203 (top 6%)") are shown under each panel.
- **All metrics at once.** An "ALL N METRICS" button opens a big window with up to 17 panels: **Sum** (always first),
  the game's own three, Sum4, the cost/cycles/area products, instructions, height, width, bounding hexagon, rate and the
  "@infinity" variants. Metrics a puzzle has no data for are left out.
- **Targets and tooltips.** Under every panel: the best scores on zlbb and the scores that reach the top 10% / 25%; the first
  panel also tells you whether any record beats your solve on cost, cycles and area (instructions) together. Pointing at a
  bar shows its score range and how many records it holds.
- **Your solve measured.** Sum, Sum4 and the products are calculated from your scores. Height, width and bounding
  hexagon are measured from the machine's footprint when you run a solution to the end (zlbb's published definitions;
  not yet verified against zlbb's own numbers). Rate and the "@infinity" metrics need a steady-state simulation the game
  does not do, so they are shown as "not measurable".
- **Submitting.** When you finish recording a GIF, a window asks "Submit to zlbb?" - nothing is sent before you press
  SUBMIT. It needs an author name (Mods screen > ZLBB Leaderboard > Set author name) that identifies you, e.g. your Reddit or Discord username. The old game build Quintessential runs on lacks the built-in zlbb submission, so this mod provides it.

## Known limits
- Height / width / bounding hexagon are remembered per solution under its four scores (`solves.txt` in the cache folder), so two
  different solutions with identical cost, cycles, area and instructions share the last measured geometry.
- On production puzzles the game's footprint is not comparable with zlbb's area (zlbb counts the walls), so no marker or rank is
  shown for Area there.
- A puzzle screen opened during the very first full download (about 10 seconds) stays plain until it is reopened.
- A wrong PC clock cannot make syncs skip changes: the server's own time is used for the sync time.

## How it uses the zlbb server
One full download of the change feed (`/om/records/changes/2000-01-01...`, ~22 MB) on the first launch, then one small
"changes since last time" request per game start. No polling, no requests while playing. The "Sync with zlbb now" and
"Re-download zlbb data" buttons (60 s cooldown, the latter asks first) are the only other requests. The very first start
shows "downloading zlbb data (first start only)" on the puzzle screens while the full download runs; "zlbb data status" in the
mod settings shows what is stored and how old it is. The data is kept in
`Documents\My Games\Opus Magnum\<profile>\ModSettings\ZlbbCache\`.

## Requirements
Opus Magnum with Quintessential 0.5.1 (installed with OpusMutatum). Windows (.NET Framework 4.x).

## Install
Build with `scripts\pack.ps1 -Deploy` (or put the zip from `dist\` into the game's `Mods` folder, still zipped) and start
the game with `ModdedLightning.exe`.

## Development
```
dotnet tool restore                          # pins ilspycmd for reading the game's code locally
dotnet test tests\ZlbbCore.Tests             # unit, golden and fake-server tests
scripts\pack.ps1 -Deploy                     # build + zip + copy into the game's Mods folder
dotnet run --project tools\FakeSubmitServer  # local stand-in for zlbb's submit endpoint
```
To try submitting without touching the real leaderboard, start the fake server and launch the game with the environment
variable `ZLBB_SUBMIT_URL=http://localhost:5099`. `ZLBB_BASE_URL` redirects the data sync the same way (use it only with a
server that implements `/om/records/changes/`); that run keeps its own cache file (`om-<hash>.snapshot`), so the real cache is
never touched.

Layout: `src/ZlbbCore` (parsing, sync, snapshot, binning, layout, submit - no game references, fully unit tested),
`src/ZlbbLeaderboard` (the game hooks and screens), `docs/GAME_HOOKS.md` (which game methods are hooked and why).
Never commit decompiled game code or game binaries.

## Status
Early development, no release yet. The zlbb maintainer has been contacted and is fine with this use of the API (conditions above);
a real test submission and the in-game checks are still pending.
Not yet verified in the game: the submit flow end to end, and the measured height / width / bounding hexagon against zlbb.

## License
MIT, see `LICENSE`.
