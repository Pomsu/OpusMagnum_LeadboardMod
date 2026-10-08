# Game hook points (confirmed against ModdedLightning.exe, Quintessential 0.5.1)

Identifiers only. No game source is stored in this repository; decompile locally with the pinned tool
(`dotnet tool restore`, then `dotnet tool run ilspycmd -- <exe> -t <TypeName>`).
Names are the game's obfuscated ("intermediary") names, as used by Quintessential and other mods.

## Histogram data seam
| Symbol | Meaning |
|---|---|
| `ScoreManager.method_1377(Puzzle, Maybe<Solution>)` -> `Dictionary<enum_133, class_132>` | Builds the 4 histogram data objects. Called by `PuzzleInfoScreen.method_1274` and `class_257` (PuzzleFinishScreen) ctor. Hook: `On.ScoreManager.method_1377`. |
| `ScoreManager.method_1378(Puzzle, Maybe<Solution>, enum_133)` (private) | Builds one `class_132`. Reads `field_3006[saveKey]`. |
| `ScoreManager.field_3006` (private `Dictionary<string,string>`) | Histogram source: key `"B.<puzzleId>.<(int)enum_133>"`, value `"max,count0,count1,..."`. **The first number is the axis MAXIMUM** (labels 0, max/4, max/2, 3max/4, max), the counts are buckets spread evenly over 0..max, and a score v lands in bucket `clamp((v-1)*buckets/max, 0, buckets-1)`; the game normalises by the largest count. **Filling this dictionary makes the game's own code build and draw the histogram.** |
| `ScoreManager.field_3004` | `enum_133[]` = Cycles, Cost, Footprint, Instructions. |
| `class_132` (HistogramData) | `field_1480` type, `field_1481` saveKey, `field_1482` `Maybe<class_182>` scoreData, `field_1483` best, `field_1484` score, `field_1485` now. |
| `class_182` (ScoreData) | `field_1725` int start value, `field_1726` `float[]` normalised counts. |
| `enum_133` | Cycles=0, Cost=1, Footprint=2, Instructions=3 (only four values). |
| `class_140.method_321(class_132, Vector2, bool)` | Draws one histogram panel (info screen passes `true`, finish screen `false`). Shows Cost, Cycles and Instructions (production puzzles) or Footprint. |
| `Puzzle.field_2766` | Puzzle id string (e.g. `P007` for campaign puzzles). `Puzzle.field_2782` = custom puzzle, `Puzzle.field_2779` = `Maybe<...>` production info. |

## Panels, window and footprint
| Symbol | Meaning |
|---|---|
| `class_140.method_321(class_132, Vector2 position, bool isInfoScreen)` | Draws one histogram panel (258 x 376 px). Unknown `enum_133` values draw without a title and with plain numbers, so extra metrics reuse it and we add the title. Hook: `On.class_140.method_321`. |
| `class_140.method_313(text, pos, w, h).method_824(true, true)` | Draws a button, returns true when clicked. `class_140.method_317(text, pos, width, true, true)` draws a window title. |
| `Quintessential.UI` | `DrawLargeUiBackground`, `DrawUiFrame`, `DrawAndCheckCloseButton`, `OpenScreen`, `CloseScreen` - the pattern for a window is `Quintessential/ModsScreen.cs`. |
| `Sim.method_484()` (`Sim.field_3824`) | The footprint: all hexes the machine touched during the run (`HexIndex.Q`, `.R`). Count = the game's Area. |
| `SolutionEditorScreen.field_4022` (private `Maybe<Sim>`) | The running simulation. `class_257`'s constructor (finish screen) runs right after a successful run: hook `On.class_257.ctor` and read the sim there. |
| `class_257.field_2065` (private) | The finish screen's histogram data; `field_1485` of each entry = this run's score ("now"). |
| `Puzzle.field_2767` | Display name of a puzzle. |

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
