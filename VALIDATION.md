# Initial public source snapshot validation — 2026-11-02

The public snapshot was verified locally on Windows without launching RimWorld:

| Check | Result |
| --- | --- |
| Default Core regressions | 484 assertions passed |
| Source-distribution workflow tests | 6 tests passed |
| Localization | 489 bilingual keys, 482 used, 0 pending C# literals |
| RimWorld 1.5 build | Passed, 0 warnings/errors |
| RimWorld 1.6 build | Passed, 0 warnings/errors |
| RimWorld 1.7 build and staging helper | Passed, 0 warnings/errors |

All 52 runtime C# files were copied unchanged from the working source tree. The
public project file replaces developer-specific fallback paths with an explicit
`GameDir` or `RIMWORLD_MANAGED_DIR`. Each build used the matching locally supplied
game assemblies; none are committed to this repository.

The initial publication audit checks the actual Git file list and blob hashes,
not just .gitignore. No tool executables, game DLLs, songs, saves, logs, private
paths or credential patterns are included. Build/test outputs stay ignored.

These results are local validation. Remote workflow results are reported by
GitHub Actions separately; the presence of a workflow file is not a passing run.
Cross-version gameplay, audible output, native video layering, all caption
interactions and large-library/mod-list performance still require game testing.
