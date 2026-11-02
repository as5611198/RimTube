# Building and testing

## Requirements

- Windows x64, .NET SDK 10, PowerShell and Python 3.11+.
- A legitimate local RimWorld installation for the version you build. Game DLLs
  are not supplied by this repository and must not be committed or redistributed.
- For 1.5/1.6 builds, install the .NET Framework 4.7.2 developer/targeting pack.

## Test without the game

From the repository root:

```powershell
dotnet run --project tests/RimTube.Tests.csproj -c Release
python tools/Localization.py
python -m unittest discover -s tests -p test_external_sources.py
```

The default C# suite uses local fixtures. It does not launch RimWorld, read game
assets, download media or need installed playback tools. Test outputs go under
ignored `artifacts` directories. Windows CI runs the same commands. Optional
network/backend test flags in the test runner require separately supplied tools
and appropriate media; they are not part of default CI.

## Build a mod DLL

Supply the directory that actually contains `Assembly-CSharp.dll` using `GameDir`.
RimWorld 1.7 uses .NET 10 and Waylith; older Unity releases normally keep managed
DLLs in `RimWorldWin64_Data/Managed`. Paths below are examples; choose your own.

```powershell
# RimWorld 1.7: replace the example directory with your own installation.
dotnet build src/RimTube.csproj -c Release -p:GameVersion=1.7 -p:GameDir="D:/Games/RimWorld"

# RimWorld 1.6:
dotnet build src/RimTube.csproj -c Release -p:GameVersion=1.6 -p:GameDir="D:/Games/RimWorld16/RimWorldWin64_Data/Managed"

# RimWorld 1.5:
dotnet build src/RimTube.csproj -c Release -p:GameVersion=1.5 -p:GameDir="D:/Games/RimWorld15/RimWorldWin64_Data/Managed"
```

You can instead set the process environment variable `RIMWORLD_MANAGED_DIR` to
the DLL directory for a single build. `GameDir` always takes precedence. No local
path is committed to the project. Different versions must use matching game DLLs.

The DLL appears under `artifacts/build/<version>/`. To stage a local mod folder
with definitions, languages and artwork:

```powershell
./tools/Build.ps1 -Versions 1.7 -GameDir "D:/Games/RimWorld"
```

This copies build outputs into `artifacts/RimTube-0.2.0`; it neither deploys to
your installed Mods folder nor launches the game. It does not include tool
binaries. To test playback, use the release's separately installed playback
tools or your own compatible versions via the in-mod settings.

## Localization

Edit `docs/localization/strings.tsv`, then regenerate and validate:

```powershell
python tools/Localization.py --write
python tools/Localization.py
```

Keep format placeholders identical between languages. Do not edit the generated
`src/Core/LocalizedText.Defaults.cs` directly. Generated resources and translation
values retain the asset/content license; surrounding code follows `LICENSE`.

## Limits of verification

Core regressions and compilation do not demonstrate in-game audio, video layering,
caption readability, compatibility with a large mod list or performance. Test
those separately in an isolated profile. Never include game DLLs, saves or private
media in a pull request. The initial public snapshot was tested without launching
RimWorld; see the repository validation report for the checks actually executed.
