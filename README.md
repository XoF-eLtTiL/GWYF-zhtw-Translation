# GWYF zh-TW Translation

這個倉庫是 `Gamble With Your Friends` 的繁體中文翻譯倉庫。

用途：

- 提供這款遊戲的 `zh-TW` 翻譯文本
- 提供翻譯貼圖資源
- 提供公開版 release 安裝包

不包含：

- 私人的加入方法
- 私人的伺服器列表 / Lobby 工具
- 私人的 moderation / 管理功能
- 其他私人插件

內容包含：

- `translations/zh-TW/...`：`Gamble With Your Friends` 實際使用的繁體中文翻譯文字與貼圖
- `manifest.txt`：給自動更新模組比對版本與檔案雜湊用
- `updater/`：BepInEx 自動更新翻譯模組原始碼
- `GWYFTmpPatcher/`：處理 TMP 字體替換、指定 TMP 文字修復，以及內嵌 Logo 貼圖替換
- `releases/`：公開版模組包與 release 說明

## 這是什麼

如果你是玩家，這個 repo 可以讓你：

- 安裝 `Gamble With Your Friends` 的繁體中文翻譯
- 下載包含 `BepInEx + XUnity + 翻譯文本 + 自動更新翻譯模組` 的公開模組包
- 透過 updater 在遊戲啟動時自動同步最新翻譯

如果你是協作者，這個 repo 可以讓你：

- 直接修改翻譯文本
- 更新翻譯貼圖
- 送出翻譯修正

## Raw 路徑

- Manifest:
  - `https://raw.githubusercontent.com/XoF-eLtTiL/GWYF-zhtw-Translation/main/manifest.txt`
- Translation base:
  - `https://raw.githubusercontent.com/XoF-eLtTiL/GWYF-zhtw-Translation/main/translations`
## 遊戲端 updater

遊戲端 updater 會在 `Gamble With Your Friends` 啟動時自動檢查這個 repo 的：

- `manifest.txt`
- `translations/`

並下載有變更的翻譯文件。

raw URL 已經寫死在：

- `updater/TranslationUpdaterPlugin.cs`

為了避免 updater 被濫用覆蓋插件或執行惡意代碼，遊戲端 config 不提供更改遠端來源。updater 只接受：

- `zh-TW/Text/*.txt`
- `zh-TW/Texture/*.png`

所有下載內容只會寫入 `BepInEx/Translation`，不會被載入或執行。
