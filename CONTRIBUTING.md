# Contributing / 如何參與

Bug reports, small fixes, translations and feature proposals are welcome. For a
large change, discuss the behavior in an Issue before writing a large PR.

## Bug reports

Include the game/mod version, Windows version, local or online playback, audio or
video mode, reproduction steps, and the exact visible error. For caption issues,
include the public video URL and selected caption language. Redact personal paths,
signed media URLs, credentials and private content. Do not upload songs, saves,
game assemblies or cookies.

## Pull requests

1. Fork this repository and create a focused branch.
2. Explain the trigger, current result and intended result. Keep unrelated work out.
3. Add a meaningful regression check when behavior changes. Run the default Core
   suite and localization checks in BUILDING.md.
4. State what was actually tested and what still needs in-game validation.
5. Preserve noncommercial restrictions, attribution and third-party notices.

UI rendering must not scan the library, parse files, query IPC or allocate large
temporary structures on every OnGUI invocation. Keep background work, event-driven
state and bounded caches separate from drawing.

## Contribution terms

By intentionally submitting a contribution, you confirm that you have permission
to submit it and offer it under the existing license for that content: the custom
noncommercial `LICENSE` for code/scripts/tests/mod definitions, or CC BY-NC-SA 4.0
with the explicit gameplay-media and optional-support permissions in
`LICENSE-ASSETS.md` for artwork, prose and translations. Disclose third-party
material and its license; do not submit incompatible code.

You retain ownership and authorship of your contribution. No copyright assignment
or permission to commercially relicense your contribution is implied. The project
attribution is **ANLN666**; preserve additional contributor credits. AI assistance
may be disclosed, but the submitter remains responsible for correctness, rights
and review. No Contributor License Agreement is required for the initial project.

繁中摘要：歡迎修正、翻譯與建議。PR 請聚焦一件事、附測試與實際驗證範圍，保留
**ANLN666** 及其他作者署名。貢獻者保留自身權利；提交即依該類內容的現有授權
提供貢獻，不代表移轉所有權或授權維護者改作商業再授權。請勿提交無權提供的素材、
歌曲、遊戲 DLL、私人資料或憑證。
