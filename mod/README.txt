RimTube 0.2.0 — Windows x64 隨包工具公開測試版（2026-11-02）

小人負責上班，你負責放歌。
Your colonists handle the chores. You handle the playlist.

Windows x64 / RimWorld 1.5, 1.6, 1.7 — separate builds.
Traditional Chinese and English follow the game's selected language.

完整包已含 mpv、yt-dlp、Node.js、FFmpeg／ffprobe，自動找到工具，不需設定 PATH。
The full bundle includes mpv, yt-dlp, Node.js, FFmpeg and ffprobe. No tool installation
or PATH edits are needed. Tools run as separate processes, without auto-updates.

設定底部可手動「更新解析核心」或「回復隨包版本」。新版經官方雜湊與版本驗證，
保存在玩家資料夾，不覆寫模組。先停止播放並等候下載工作完成；自訂工具覆蓋仍優先。
Use Update Resolver or Restore Bundled Version in Settings after stopping playback
and finishing jobs. Verified updates live in the game profile, outside the mod.
Custom tool overrides take priority. Updates happen only when requested.

解壓至 Mods/RimTube，確保 About、Common、Tools 與版本資料夾位於下一層。
啟用模組並重開遊戲；在底部「音樂」→「全部音樂／全部影片」貼網址，選「YT 媒體」。
Extract into Mods/RimTube, enable the mod and restart the game. Open Music in the
bottom bar; paste a URL on All music / All videos and choose YT media.

- 公開 YouTube 音樂、影片、直播；串流、先下載再播、只下載、保存副本重用。
- 本機匯入、來源篩選、自訂歌單、日常／戰鬥與原版／DLC／模組來源歌單。
- 迷你列、可縮放影片窗、音量、畫質上限、字幕、100%～300% 字幕倍率及進度條。
- 廢土工業／閃耀科技／中世紀奇幻／部落原始四主題。
- 移除歌單項目保留檔案；從媒體庫刪除會處理 RimTube 管理的保存檔案。
  原始匯入檔、遊戲資產及其他曲目仍共用的檔案會保留。

- Public YouTube audio/video/live streams, local imports, playlists and saved-copy reuse.
- Everyday/combat BGM and automatic playlists grouped by game/DLC/mod source.
- Mini player, floating video, volume, quality ceiling, captions and timeline.
- Four themes; covered video continues playing by default.
- Playlist removal keeps files. Library deletion handles RimTube-managed saved copies;
  original imports, game assets and files shared by other records are kept.

Hotkeys: Ctrl+M library; Ctrl+Alt+Space play/pause; Ctrl+Alt+Right next;
Ctrl+Alt+M mute; Ctrl+Alt+R restore mini; Ctrl+Alt+L toggle click-through.
Bindings can be changed in Settings. Text input disables playback shortcuts.

玩家資料在遊戲使用者目錄下 RimTube/Library；全域介面設定在 RimTube/Interface。
更新模組不覆蓋玩家資料；舊有明確指定的音樂庫／自備工具路徑保留。
Library: game user data directory/RimTube/Library. Global UI: RimTube/Interface.
Updating the mod keeps player data. Explicit library/tool overrides remain supported.

No game assemblies, songs, cookies, credentials or test saves are included.
Restricted sources may be unavailable. Only use content you are entitled to play/save.

工具授權與版本索引保留在 Tools/licenses、Tools/Sources 及 Tools/provenance。
mpv／FFmpeg 使用相符 MSYS2 UCRT64 shared builds；Node.js 使用官方套件；
yt-dlp 為未修改核心的精簡本機封裝，設定仍可手動取得官方 stable 更新。
完整對應原始碼、相依來源、修補與建置配方改由以下固定版本 GitHub Release 免費提供，
無須登入；直接下載連結、大小與 SHA-256 見 Tools/Sources/README.txt。
https://github.com/as5611198/RimTube-ThirdParty-Sources/releases/tag/tools-20261102-0.2.0
來源材料不是執行期依賴，遊戲啟動時不會下載，播放工具仍全部隨包。
Third-party licenses, dependency notices and version records remain under Tools.
Complete corresponding sources, patches and build recipes are provided free, without
login, at the fixed GitHub Release above. Tools/Sources/README.txt lists direct links
and SHA-256 checksums. Source downloads are optional, never fetched on game startup.
Native binaries are unmodified MSYS2 builds; Node.js is the
official package. The bundled resolver uses an unmodified yt-dlp core with a minimal
local packaging recipe; the manual updater still obtains official stable builds.

三版編譯與非遊戲測試通過；本輪未啟動遊戲。跨版本遊戲／聽感／效能仍待驗收。
Three builds and non-game tests pass; this update did not launch the game.
In-game auditory, cross-version and performance acceptance remains pending.

本模組使用 Gemini 及 Codex（GPT）協助製作。
Created with assistance from Gemini and Codex (GPT).
