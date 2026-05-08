# Release v1.1.4

公開內容：

- BepInEx runtime
- XUnity AutoTranslator
- XUnity ResourceRedirector
- `Gamble With Your Friends` zh-TW 翻譯文本與貼圖
- GWYFTranslationUpdater 自動更新翻譯模組
- GWYF TMP Patcher 修正 TMP 文字材質與指定 UI 翻譯補丁

變更：

- TMP Patcher 更新至 `v0.1.10`
- Outline 改為全 TMP 修正，但不再持續全場輪詢
- 場景載入後會以分批方式補掃 TMP，降低一次性卡頓
- 延遲補掃 1 秒與 3 秒，補到較晚生成的 TMP 物件
- 同時修正 `fontSharedMaterial` 與 `fontSharedMaterials`
- Replacer 仍只處理 `ItemNameText`
- Outline-only 修正不再強制 `ForceMeshUpdate`

不包含：

- 私人的加入方法
- 私人的伺服器列表 / Lobby 工具
- 私人的 moderation / 管理功能
- 其他私人插件

安裝方式：

1. 解壓到遊戲根目錄
2. 覆蓋既有檔案
3. 啟動遊戲
4. updater 會在啟動後自動檢查翻譯更新
