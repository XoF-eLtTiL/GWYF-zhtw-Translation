# GWYF zh-TW Translation

這個倉庫是 `Gamble With Your Friends` 的繁體中文翻譯與配套更新模組倉庫。

用途：

- 提供這款遊戲的 `zh-TW` 翻譯文本
- 提供翻譯貼圖資源
- 提供自動更新翻譯文件的 BepInEx 模組原始碼
- 提供公開版 release 安裝包

不包含：

- 私人的加入方法
- 私人的伺服器列表 / Lobby 工具
- 私人的 moderation / 管理功能
- 其他私人插件

內容包含：

- `translations/zh-TW/...`：`Gamble With Your Friends` 實際使用的繁體中文翻譯文字與貼圖
- `manifest.txt`：給自動更新模組比對版本與檔案雜湊用
- `scripts/Update-Manifest.ps1`：每次修改翻譯後重建 manifest
- `updater/`：BepInEx 自動更新翻譯模組原始碼
- `GWYFTmpPatcher/`：修正遊戲 TMP 材質描線，並補處理 XUnity 漏掉的指定 TMP 文字
- `releases/`：公開版模組包與 release 說明

## 這是什麼

如果你是玩家，這個 repo 可以讓你：

- 安裝 `Gamble With Your Friends` 的繁體中文翻譯
- 下載包含 `BepInEx + XUnity + 翻譯文本 + 自動更新翻譯模組` 的公開模組包
- 透過 updater 在遊戲啟動時自動同步最新翻譯

如果你是協作者，這個 repo 可以讓你：

- 直接修改翻譯文本
- 更新翻譯貼圖
- 重建 manifest 後發布新版

## 更新翻譯

1. 修改 `translations/` 內檔案
2. 執行：

```powershell
.\scripts\Update-Manifest.ps1
```

3. 發布前確認公開包已關閉自動翻譯：

```powershell
.\scripts\Verify-ReleasePackage.ps1 -PackagePath .\releases\GWYF-zhtw-Translation-Pack-vX.Y.Z.zip
```

4. 提交並推送

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

預設 raw URL 已經寫在：

- `updater/TranslationUpdaterPlugin.cs`

也可以在遊戲生成的：

- `BepInEx/config/codex.gwyf.translationupdater.cfg`

手動改成別的分支或 fork。
