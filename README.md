# BPSR Module Optimiser

A Windows app for **Blue Protocol: Star Resonance** that finds the best 5-module
loadouts from your module inventory.

You weight the stats you care about, optionally set hard minimums
(for example "Life Wave must reach 20"), and it ranks the loadouts that score best.

## Features

- Reads your inventory from ZDPS's `ModulesSaveData.json`.
- A weight slider (1-100) for every stat. The list keeps itself sorted by weight,
  and you can drag a row to move it.
- Optional minimum totals per stat, treated as hard requirements.
- Saved weight profiles. Comes with an **Icicle** profile.
- Compact results: each loadout's stat totals, plus the five modules with their
  type, picture and stats.
- Save results to JSON.

## Scoring

- A loadout is exactly 5 modules.
- Each stat's total across the 5 modules counts up to 20. Anything above 20 is wasted.
- Breakpoints are 4, 8, 12, 16 and 20. Totals earn tier points: 20 = 11, 16 = 6, 12 = 3, 8 = 1.
  Two 16s (12 points) beat one 20 (11 points). Links past a breakpoint are wasted, so 17-19 score the same as 16.
- Reaching 4 is the final tie-breaker: between otherwise equal loadouts, the one that reaches more 4s wins.
- Score = sum over stats of (weight x tier points).

## Getting your module data

This app doesn't read game traffic itself. [ZDPS](https://github.com/Blue-Protocol-Source/BPSR-ZDPS)
does: run it with the game open, refresh your inventory, and it saves your modules to
`ModulesSaveData.json` in its `Data` folder. Open that file in this app.
The app remembers the last file you used.

## Build and run

Double-click `App/Build.bat`. It installs the .NET 8 SDK with winget if needed,
builds the app, and starts it. Close the app before rebuilding, or the build
fails because the exe is locked.

Manual build:

```
cd App
dotnet build -c Release
bin\Release\net8.0-windows\ModuleOptimiser.exe
```

Settings and profiles are stored in `%AppData%\ModuleOptimiser\settings.json`.
Runtime errors are written to `error-log.txt` next to the exe.

## Limitations

- The search is a beam search. It finds strong loadouts quickly but isn't
  guaranteed to find the single best one. An exact solver is planned.

## Repository layout

- `App/`: the WPF app (.NET 8).
- `optimiser.py`: an older Python command-line prototype, kept for reference.
  It uses different scoring and isn't maintained.

## Credits

Data formats, stat names, module names and the stat and module icons come from
[BPSR-ZDPS](https://github.com/Blue-Protocol-Source/BPSR-ZDPS).
Game assets belong to their respective owners.
