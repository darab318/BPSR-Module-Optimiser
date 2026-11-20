# BPSR Module Optimiser

Personal Windows app for Blue Protocol: Star Resonance. Reads the module inventory
from ZDPS's `ModulesSaveData.json`, lets the user set stat minimums and a priority
order, and finds the best 5-module loadouts.

## Layout

- `App/` is the current WPF app (.NET 8, `net8.0-windows`). This is what's being developed.
- `optimiser.py` and `README.md` at the root are the **older Python prototype**
  (beam search, different scoring). Don't treat them as the spec.

## Build and run

- Run `App/Build.bat`. It installs the .NET 8 SDK via winget if missing, runs
  `dotnet build -c Release > build-log.txt`, then launches
  `bin\Release\net8.0-windows\ModuleOptimiser.exe`.
- **Close the running app first**, or the build fails with "file is locked by ModuleOptimiser".
- After every change, build and read `App/build-log.txt` to confirm it succeeded.
  If the user says a change didn't work after rebuilding, assume the change didn't
  land, not that they ran the wrong thing.
- Runtime errors go to `error-log.txt` next to the exe. Settings (last file used)
  are in `%AppData%\ModuleOptimiser\settings.json`.

## Code map

- `Core/ModuleData.cs`: `Module {Id, ConfigId, Stats}`; `ModuleLoader.Load(path)` parses the JSON.
- `Core/StatNames.cs`: stat ID to game name (21 stats, e.g. 2104 DMG Stack, 2404 Life Wave,
  2105 Agile, 1408 Attack SPD, 1113 Special Attack, 1409 Crit Focus, 1114 Elite Strike,
  1112 Intellect Boost, 1111 Agility Boost, 1110 Strength Boost, 1410 Luck Focus, 1407 Cast Focus).
- `Core/ModuleTypes.cs`: ConfigId to name/type. 55001xx = Attack, 55002xx = Support, 55003xx = Guard.
- `Core/Optimiser.cs`: the search.
- `MainWindow.xaml(.cs)`: drag-to-reorder stat list (auto-scrolls near edges), "Equal weights"
  toggle, per-stat minimum textbox, Border-based progress bar (stock ProgressBar didn't render),
  result cards (module type, picture, stats with icons), Save results.
- `Assets/Icons/<statId>.png` and `Assets/ModuleIcons/<configId>.png`: must be `<Resource>` items
  in the csproj; loaded with `pack://application:,,,/Assets/...` URIs.
- `Themes/Styles.xaml`: shared glassy aero style, copied from `Documents\Glasscut\Themes\Styles.xaml`
  (same look as Glasspane and Glasscut). Keep that look.

## Data format (`ModulesSaveData.json`)

- `ModulesPackage.Items` (keyed by uuid, has `ConfigId`) and `Mod.ModInfos` (same uuid keys).
- Each `ModInfos` entry: zip `PartIds` (stat IDs) with `InitLinkNums` (values) to get the stats.
- The user's file has 1,735 modules; each module has 1-3 stats, each valued 1-10.
- Reference: ZDPS source https://github.com/Blue-Protocol-Source/BPSR-ZDPS
  (`Data/ModEffectTable.json`, `Data/ModTable.json`, `Data/Images/Modules`).

## Scoring rules (agreed with the user; don't change without asking)

- A loadout is exactly 5 modules.
- Each stat's total across the 5 modules is capped at 20; nothing above 20 counts.
- Tier points: 20 = 11, 16 = 6, 12 = 3, 8 = 1, below 8 = 0. (Two 16s beating one 20 is intended.)
- Each stat has a weight (0-100, decimals allowed): a slider plus a typed number box, always shown.
  The list stays sorted by weight; dragging a row sets its weight between its new neighbours.
  With no profile, weights start from list order (top = N ... bottom = 1).
- Weight profiles are saved in settings.json. Built-in "Icicle" profile uses the user's
  "Module values" gain % directly (ranges at midpoint): Life Wave 10.56, Agile 8.59, DMG Stack 7.74,
  Crit Focus 4.92, Attack SPD 4.5, Special Attack 4.5, Elite Strike 4.48, Intellect 4.14,
  Luck Focus 1.03, every other stat 0.1. `IcicleVersion` in MainWindow.xaml.cs: bump it to push
  new Icicle numbers over the saved copy.
- Score = sum over stats of weight x tier points.
- Typed minimums are hard constraints. Fill the remaining capacity with the best
  overall build, not just the minimums.

## Search must be exact

The old beam search (`BeamWidth` 4000) is heuristic and misses the best builds.
The intended replacement is an exact solver: OR-Tools CP-SAT (NuGet `Google.OrTools`),
or an exhaustive search with deduplication of identical stat sets plus dominance pruning.
Run it off the UI thread and keep the progress bar meaningful.

Verified exact answers on the user's data (use as regression checks):

1. Min Life Wave 20 + Attack SPD 20, default order: Life Wave 20, Attack SPD 20,
   Special Attack 20, Intellect 12. Only two loadouts reach LW/AS/SA 20 + Int >= 12.
2. Min Life Wave 20: three 20s is the max; four 20s including Life Wave is impossible.
3. Min DMG Stack 20: three 20s max; best fourth stat is 14 (no 16 possible).
4. Min Agile 20, order Attack SPD > Agile > Life Wave > DMG Stack > Crit Focus:
   Attack SPD 20, Agile 20, Crit Focus 20, Agility 16.
5. No minimums: four 20s is possible (Attack SPD, Crit Focus, Special Attack, Elite Strike).

## Priority orders

- Default (top first): Life Wave, DMG Stack, Attack SPD, Special Attack, Crit Focus,
  Elite Strike, Intellect Boost, Agility Boost, Agile, Luck Focus, Cast Focus,
  Strength Boost, Life Steal, Team Luck & Crit, then the healing and defence stats.
- Chinese guide: Attack SPD > Agile > Life Wave > DMG Stack > Crit Focus, then the default list.

## Wanted after the solver

- Exclude a stat entirely (e.g. no Strength) and restrict to Attack modules only.
- Show smaller links (4s and 1s) on result cards.
- Saved priority presets (default vs Chinese guide).

## Coding rules

- `<Nullable>disable</Nullable>`: don't put `?` on reference types (causes CS8632 warnings).
- Null-check every image before adding it to a panel. A missing module picture once
  crashed the results view.
