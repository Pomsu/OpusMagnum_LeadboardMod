# OpusMagnum_LeadboradMod

A [Quintessential](https://github.com/QuintessentialOM/Quintessential) mod for Opus Magnum that shows data from the
community leaderboard <https://zlbb.faendir.com> in the game's own histograms and (optionally, with confirmation)
submits solutions to it.

Status: early development. See `docs/GAME_HOOKS.md` for the game hook points.

## Design in short
- Histograms are drawn by the game's own code in its original style; only the data is replaced.
- Data comes from the zlbb change feed (`/om/records/changes/{since}`): one full download on first launch, one small
  diff per game start afterwards, nothing else while playing (no polling).
- Submitting a solution always asks first; automatic submit is opt-in.

## Requirements
Opus Magnum with Quintessential 0.5.1 (installed with OpusMutatum). Not affiliated with the zlbb maintainers.

## Development
```
dotnet tool restore        # pins ilspycmd for reading the game's code locally
```
Never commit decompiled game code or game binaries.
