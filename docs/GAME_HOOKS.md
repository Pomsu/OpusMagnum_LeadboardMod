# Game hook points (confirmed against ModdedLightning.exe, Quintessential 0.5.1)

Identifiers only. No game source is stored in this repository; decompile locally with the pinned tool
(`dotnet tool restore`, then `dotnet tool run ilspycmd -- <exe> -t <TypeName>`).
Names are the game's obfuscated ("intermediary") names, as used by Quintessential and other mods.

## Histogram data seam
| Symbol | Meaning |
|---|---|
| `ScoreManager.method_1377(Puzzle, Maybe<Solution>)` -> `Dictionary<enum_133, class_132>` | Builds the 4 histogram data objects. Called by `PuzzleInfoScreen.method_1274` and `class_257` (PuzzleFinishScreen) ctor. Hook: `On.ScoreManager.method_1377`. |
| `ScoreManager.method_1378(Puzzle, Maybe<Solution>, enum_133)` (private) | Builds one `class_132`. Reads `field_3006[saveKey]`. |
| `ScoreManager.field_3006` (private `Dictionary<string,string>`) | Histogram source: key `"B.<puzzleId>.<(int)enum_133>"`, value `"start,count0,count1,..."` (raw counts per consecutive integer; the game normalises by the max). **Filling this dictionary makes the game's own code build and draw the histogram.** |
| `ScoreManager.field_3004` | `enum_133[]` = Cycles, Cost, Footprint, Instructions. |
| `class_132` (HistogramData) | `field_1480` type, `field_1481` saveKey, `field_1482` `Maybe<class_182>` scoreData, `field_1483` best, `field_1484` score, `field_1485` now. |
| `class_182` (ScoreData) | `field_1725` int start value, `field_1726` `float[]` normalised counts. |
| `enum_133` | Cycles=0, Cost=1, Footprint=2, Instructions=3 (only four values). |
| `class_140.method_321(class_132, Vector2, bool)` | Draws one histogram panel (info screen passes `true`, finish screen `false`). Shows Cost, Cycles and Instructions (production puzzles) or Footprint. |
| `Puzzle.field_2766` | Puzzle id string (e.g. `P007` for campaign puzzles). `Puzzle.field_2782` = custom puzzle, `Puzzle.field_2779` = `Maybe<...>` production info. |

## Settings (class_159, config.cfg)
- `field_1619` = `EnableHermitMode`: **the histogram screens test this one** (hermit mode on = "Histograms and leaderboards are hidden"). Default False; nothing to force.
- `field_1620` = `ShowGlobalRecords` (not used by the histogram screens in this build).
- `ExternalLeaderboardURLs` does not exist in this (old) build; submission is implemented by this mod.

## GIF recording / submission
| Symbol | Meaning |
|---|---|
| `class_250` (GifRecorderScreen), ctor `(Solution)` | Owns encoder `field_2031` (`class_92`) and the simple editor `field_2022` (`class_194`); `field_2022.method_502()` returns the recorded `Solution`. Hooks: `On.class_250.ctor`, `On.class_250.method_50` (update/draw). |
| `class_92` (GIF encoder), ctor `(string path)` | `field_922` FileStream (path = `field_922.Name`). |
| `class_92.method_77()` | **Finish** (writes trailer, closes). Hook `On.class_92.method_77`. |
| `class_92.method_78()` | **Cancel** (finishes, then deletes the file). Do not submit on this one. |
| `Solution.method_1959()` -> `byte[]` | Serialises the solution. `Solution.field_3918` = score dictionary, `Solution.method_1934()` = Puzzle. |

## Corrections to earlier assumptions
- The `ShowGlobalRecords` gate does not exist for the histograms in this build (it is hermit mode, already off).
- `patch_GifRecorder` in Quintessential only watermarks frames; not a hook point for finish.
