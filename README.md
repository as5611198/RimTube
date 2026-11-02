# RimTube — your colony, your playlist

[繁體中文](README.zh-TW.md) · [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3831920819)
· [Report a bug](https://github.com/as5611198/RimTube/issues)

Your colonists handle the chores. You handle the playlist.

RimTube brings a music library, public YouTube audio/video/live playback and a
floating video window into RimWorld. Assign everyday/combat playlists, import
local music, and browse automatic playlists grouped by base game, DLC or mod.
Keep the mini player nearby, or hide it and use shortcuts. Four themes, caption
language/size controls, a seek bar and saved-copy reuse are included.

**Author and required project attribution: ANLN666.** Created with assistance
from Gemini and Codex (GPT).

## Download and status

Subscribe through [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3831920819).
The Windows x64 bundle includes mpv, yt-dlp, Node.js, FFmpeg and ffprobe; no extra
tool installation or PATH changes are required. The GitHub source ZIP is a
developer checkout, not the ready-to-play tool bundle.

This is a Windows public beta. Separate builds target RimWorld 1.5, 1.6 and 1.7;
compilation does not establish full in-game compatibility. Cross-version gameplay,
all video/caption interactions and large-library/mod-list performance remain under
acceptance testing. Tools and media sources can change independently.

Third-party licenses remain with the tools. Their complete corresponding sources,
patches, dependencies and build recipes are available from the independent
[fixed source Release](https://github.com/as5611198/RimTube-ThirdParty-Sources/releases/tag/tools-20261102-0.2.0).
These optional source downloads are free and require no login. They are not
downloaded on game startup. Keep each source release available while its binaries
are distributed; do not overwrite old source assets.

## Develop and contribute

See [BUILDING.md](BUILDING.md) for local builds and non-game tests, and
[CONTRIBUTING.md](CONTRIBUTING.md) for fixes, translations and pull requests.
Issues are open for reproducible bugs and feature suggestions. Do not upload
saves, songs, copyrighted game DLLs, cookies or credentials.

The runtime C# sources are public here. Core tests use local synthetic fixtures;
Windows CI runs them without launching RimWorld. Build output, external tool
binaries, development-only game launch/capture helpers and private test data are
excluded from this public snapshot.

## License

This is **source-available for noncommercial use**, not MIT/GPL or an OSI-approved
open-source project. [LICENSE](LICENSE) permits free noncommercial use, inspection,
modification and sharing with attribution. Selling the mod/code, paid repackaging,
paywalls and commercial product/service integrations require separate permission.
Monetized gameplay streams, reviews/tutorials and genuinely optional donations
without paid access or features are expressly permitted.

Original artwork, prose and translations follow [CC BY-NC-SA 4.0 and the additional
media permissions](LICENSE-ASSETS.md), attributed to **ANLN666**. Third-party tools
and game assets keep their own terms. Contributions retain their authorship and
must follow the corresponding project license; no ownership transfer is required.
