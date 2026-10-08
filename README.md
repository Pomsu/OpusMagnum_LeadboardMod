# OpusMagnum_LeadboradMod

A [Quintessential](https://github.com/QuintessentialOM/Quintessential) mod for Opus Magnum that shows the community
leaderboard <https://zlbb.faendir.com> inside the game, drawn by the game's own histogram code in its original style, and
(only after you confirm) submits your solutions to it. Not affiliated with the zlbb maintainers.

## What it does
- **Histograms from zlbb.** The cost / cycles / area (or instructions) panels on the puzzle screen and the "machine
  finished" screen show the best 50% of zlbb's record-setting solutions, with a fixed bar width and tidy axes. Your own
  score marker and your rank ("#12 of 203 (top 6%)") are shown under each panel.
- **All metrics at once.** An "ALL N METRICS" button opens a big window with up to 17 panels: **Sum** (always first),
  the game's own three, Sum4, the cost/cycles/area products, instructions, height, width, bounding hexagon, rate and the
  "@infinity" variants. Metrics a puzzle has no data for are left out.
- **Your solve measured.** Sum, Sum4 and the products are calculated from your scores. Height, width and bounding
  hexagon are measured from the machine's footprint when you run a solution to the end (zlbb's published definitions;
  not yet verified against zlbb's own numbers). Rate and the "@infinity" metrics need a steady-state simulation the game
  does not do, so they are shown as "not measurable".
- **Submitting.** When you finish recording a GIF, a window asks "Submit to zlbb?" - nothing is sent before you press
  SUBMIT (or turn on automatic submitting in the mod settings). It needs an author name (Mods screen > ZLBB Leaderboard >
  Set author name). The old game build Quintessential runs on lacks the built-in zlbb submission, so this mod provides it.

## How it uses the zlbb server
One full download of the change feed (`/om/records/changes/2000-01-01...`, ~22 MB) on the first launch, then one small
"changes since last time" request per game start. No polling, no requests while playing. A "Sync with zlbb now" button
(60 s cooldown) is the only other request. The data is kept in
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
server that implements `/om/records/changes/`).

Layout: `src/ZlbbCore` (parsing, sync, snapshot, binning, layout, submit - no game references, fully unit tested),
`src/ZlbbLeaderboard` (the game hooks and screens), `docs/GAME_HOOKS.md` (which game methods are hooked and why).
Never commit decompiled game code or game binaries.

## Status
Early development. Contact with the zlbb maintainers is pending; real submissions should wait for their reply.
