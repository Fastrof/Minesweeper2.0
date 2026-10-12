# MineSweeper 2.0

[简体中文](README.md) | **English**

<p align="center">
  <img src="docs/images/icon-256.png" width="176" alt="MineSweeper 2.0 icon">
</p>

A Windows Minesweeper written from scratch: **reproduce the classic game 100% first, then build something new on top of it.**

> Tech stack: **C# + .NET 10 + WinUI 3 (Windows App SDK) + Win2D**. No Visual Studio needed — `dotnet build` is enough.

| | |
|---|---|
| **Version** | `v1.0.0` (first public release) |
| **Build** | `build.cmd` → **0 warnings, 0 errors** |
| **Tests** | `test.cmd` → **162 / 162 passing** (~1 second) |
| **Repository** | https://github.com/Fastrof/Minesweeper2.0 |
| **License** | For learning and discussion only |

---

## There is not a single third-party image or audio file in this project

Themes are **pure palette-based vector drawing**, sound effects are **synthesized in code at runtime**, and the application icon is a hand-assembled multi-resolution ICO.

This is not just tidiness — it directly answers the copyright red line in the design document: **this project contains no assets taken from Windows 7 Minesweeper.**

## What is reproduced

Left click to reveal · right click cycles through three states (flag → question mark → empty) · double click / middle click / both buttons together to chord · the first click is always safe · the timer starts on the first reveal · remaining mine counter · all mines are shown on a loss.

## What is new

- **Level mode**: 6 chapters × 6 levels = **36 levels**. Every level is a fixed board, starred by completion time (★★★ / ★★ / ★), unlocked one after another. Clearing a level shows a settlement card, and an in-game "◀ Previous / Next ▶" lets you keep playing without going back to the level panel.
- **Three items**: magnifier (reveals a 3×3 area), sonar scan (only reports *how many* mines are inside a 3×3 area), time freeze (pauses the countdown for 20 seconds). Every 20 mines you sweep earns one "pick one of three" supply drop.
- **Adjustable themes**: a palette of 19 color keys, with several built-in color schemes.
- **Code-synthesized sound effects**: reveal / flag / chord / explosion / victory / startup — six in total.
- **Zoom, pan and far-zoom degradation**: up to **4096×4096** per game; 1,000,000 cells on screen measured at roughly **70 FPS**.
- **Accessibility**: UI Automation support for the self-drawn board, so screen readers can read the board aloud.

## Quick start

```bat
git clone https://github.com/Fastrof/Minesweeper2.0.git
cd Minesweeper2.0

build.cmd     :: build the whole solution (x64), 0 warnings 0 errors
run.cmd       :: launch the game
test.cmd      :: run the Core unit tests (162 of them)
```

The scripts probe `%ProgramFiles%\dotnet`, `%ProgramFiles(x86)%\dotnet`, `%DOTNET_ROOT%` and `D:\dotnet` in order, and fall back to whatever `dotnet` is on `PATH`. The equivalent manual commands:

```bat
dotnet build MineSweeper2.sln -p:Platform=x64
dotnet test tests\MineSweeper.Core.Tests\MineSweeper.Core.Tests.csproj
dotnet run --project src\MineSweeper.App\MineSweeper.App.csproj -p:Platform=x64
```

> **`-p:Platform=x64` is mandatory** — the WinUI project cannot use the default AnyCPU.

The build output is **self-contained** (`WindowsAppSDKSelfContained=true`), so you can also just double-click:

```
src\MineSweeper.App\bin\x64\Debug\net10.0-windows10.0.19041.0\MineSweeper2.exe
```

⚠️ **It must be launched in place — do not copy the exe out on its own.** There are over a hundred Windows App SDK / WinUI / Win2D runtime DLLs sitting next to it, and copying the exe alone will fail to start. To move it, copy the **whole folder**.

### Requirements

| Item | Version / notes |
|---|---|
| .NET SDK | **10.0.401** (verified) |
| Windows | 10 1809+ / 11 |
| Visual Studio | **Not required** (both Windows App SDK and Windows SDK BuildTools come from NuGet) |
| NuGet packages | Windows App SDK 2.5.1, Win2D 1.4.0, CommunityToolkit.Mvvm 8.4.2 (versions centralized in `Directory.Packages.props`) |

## Where the save data lives

**`%LOCALAPPDATA%\MineSweeper2\`** — the single data directory since v0.6; it is no longer placed next to the exe. Older versions migrate automatically on first launch.

| File | Contents |
|---|---|
| `settings.json` | Player settings (theme, volume, etc.) |
| `progress.json` | Cross-game progress and item inventory |
| `records.json` | Best times per difficulty |
| `levelprogress.json` | Level progress (cleared / stars / best time / attempts) |
| `history.json` | Lifetime statistics (counted since the very first sweep) |
| `config.json` | Item and board configuration (hand-editable, no rebuild required) |
| `app.log` | Diagnostic log |
| `sounds\` | Sound effects synthesized on first run; **existing files are never overwritten** — drop in a same-named wav to replace one |

Every save file is written **atomically**: if a file is corrupted it is backed up and rebuilt, and if its `schemaVersion` is newer than the app understands it is refused and the original file is left untouched.

## Project structure

```
MineSweeper.Core    pure .NET class library, zero UI dependencies → all game rules live here, fully unit-testable
      ↑
MineSweeper.App     WinUI 3 + Win2D → display and input only, no game rules
```

```
src/
  MineSweeper.Core/         core logic: mine placement / flood fill / chord / levels / items / saves / themes / sound synthesis
  MineSweeper.App/          WinUI 3 shell: rendering, input, panels, about windows
tests/
  MineSweeper.Core.Tests/   162 xUnit test cases
```

> **Iron rule: never put game rules into the App layer.** The test is simple — "can this logic be tested without starting a UI?" If yes, it belongs in Core; Core may not reference `Windows.*` or `Microsoft.UI.*` at all.

## Tests

**All 162 test cases pass.**

| File | Cases | Coverage |
|---|---|---|
| `LevelTests.cs` | 35 | Level-table gatekeeper, star ratings, unlocking, saves, clear detection, previous/next navigation |
| `ThemeAndAudioTests.cs` | 23 | Theme palettes, loading/saving settings, sound synthesis |
| `ItemTests.cs` | 17 | Item counts and grants |
| `BoardGenerationTests.cs` | 15 | Mine placement and parameter boundaries |
| `AdvancedFeatureTests.cs` | 14 | Locked boards, reveal fog, info panel |
| `HistoryTests.cs` | 14 | History accumulation and corruption handling |
| `RegressionTests.cs` | 13 | Regression fences for fixed defects |
| `PersistenceTests.cs` | 11 | Saves and records (atomic writes / corruption / versions) |
| `WinConditionTests.cs` | 7 | Win/loss detection + automatic flagging on a win |
| `ChordTests.cs` | 5 | Chord detection and "a wrongly placed flag explodes" |
| `FloodFillTests.cs` | 5 | Flood fill (including a 1000×1000 empty board without stack overflow) |
| `NotificationTests.cs` | 3 | Notification levels and the "log only" dimension |

---

## Want to keep developing?

The architecture notes, change guide, diagnostic switches, code conventions, the **full changelog** and the release checklist all live in
**[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)** (in Chinese).

The ones people look up most often:

| What you want to do | Where to go |
|---|---|
| Change level numbers (board / mines / star thresholds) | `Core/Levels/LevelTable.cs`, **that one table only** |
| Add a theme | add one record in `Core/Themes/BoardPalette.cs`; the dropdown picks it up automatically |
| Add a sound effect | add to the `SoundEffect` enum + a waveform in `WavSynth`; it is synthesized on first run |
| Troubleshoot a problem | the `SAOLEI_*` diagnostic switches in `docs/DEVELOPMENT.md` §11.4 |

## License

**For learning and discussion only.**
