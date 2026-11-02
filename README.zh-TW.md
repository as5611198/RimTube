# RimTube｜小人負責上班，你負責放歌

[English](README.md) · [Steam 工作坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3831920819)
· [問題回報](https://github.com/as5611198/RimTube/issues)

又是熟悉的背景音樂？那就幫殖民地換個台吧。

RimTube 把本機音樂庫、公開 YouTube 音樂／影片／直播和浮動影片窗搬進
RimWorld。日常建設配自己的歌單，突襲來了換戰鬥音樂；原版、DLC 與模組歌曲
也會按來源整理。迷你列、四種主題、字幕語言／大小、進度條及下載副本重用都有。
小人的精神崩潰還是得自己處理，至少 BGM 可以自己挑。

**作者與專案署名：ANLN666。** 本模組使用 Gemini 及 Codex（GPT）協助製作。

## 下載與目前狀態

玩家請從 [Steam 工作坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3831920819)
訂閱。Windows x64 包已含 mpv、yt-dlp、Node.js、FFmpeg／ffprobe，不需另外
安裝工具或設定 PATH。GitHub 的原始碼 ZIP 供開發使用，不是可直接遊玩的完整工具包。

目前是 Windows 公開測試版，1.5／1.6／1.7 有獨立編譯；跨版本實機、完整影片／
字幕互動及大型音樂庫／大量模組效能仍持續驗收，不把編譯通過當成實機驗證。

第三方工具維持原授權，完整對應來源、修補、依賴與建置配方在獨立的
[固定來源 Release](https://github.com/as5611198/RimTube-ThirdParty-Sources/releases/tag/tools-20261102-0.2.0)，
免費且不用登入。玩家不必下載來源，遊戲啟動也不會下載。對應執行檔仍可取得時，
維護者必須保留該版本來源，不以新版覆蓋舊資產。

## 開發、翻譯與回報

建置與無遊戲測試見 [BUILDING.md](BUILDING.md)，提交修正和翻譯見
[CONTRIBUTING.md](CONTRIBUTING.md)。歡迎用 Issues 回報問題、提出建議，也歡迎 PR。
請不要上傳存檔、歌曲、遊戲 DLL、Cookie 或憑證。

此倉庫公開全部執行期 C# 原始碼與 Core 測試。測試使用本機合成資料，不啟動
RimWorld；Windows CI 也只做無遊戲測試。建置產物、工具執行檔、開發專用的
遊戲啟動／拍攝助手與私人測試資料不包含在這份公開快照。

## 授權

這是**原始碼公開、限非商業使用**的專案，不採 MIT／GPL，也不是標準開源授權。
[程式碼授權](LICENSE) 允許非商業使用、修改及免費分享並保留署名；禁止販售程式碼
或模組、付費重包、付費解鎖，以及商業產品／服務整合，除非另獲權利人許可。
**有收益的遊戲實況、介紹／教學影片及不影響免費取得功能的自願贊助明確允許。**

原創美術、文案與翻譯採 [CC BY-NC-SA 4.0 與額外媒體使用許可](LICENSE-ASSETS.md)，
署名 **ANLN666**。第三方工具與遊戲資產維持原授權。貢獻者保留自身著作權，
提交的內容須遵循其對應授權，不要求移轉所有權。
