# GWYF zh-TW Translation Pack v1.1.8

## 更新內容

- Thunderstore 版翻譯資料改放到 `BepInEx/config/Translation`
- GWYF Translation Updater 改為同步到 `BepInEx/config/Translation`
- GWYF TMP Patcher 改為從 `BepInEx/config/Translation/zh-TW/Text` 讀取翻譯
- XUnity AutoTranslator 設定改為讀取 `config/Translation`
- 修正 GitHub raw manifest 快取導致自動更新跳過 `Complate.txt` 的問題
- Updater 更新翻譯後會嘗試觸發 XUnity 重新載入翻譯檔
- 關閉 XUnity `ForceMonoModHooks`，降低 Unity 6000 hook exception

## 安裝

主遊戲整合包可直接解壓到遊戲根目錄；Thunderstore 版使用 Thunderstore Mod Manager / r2modman / Gale 安裝即可。

## 注意

- 這是給 Thunderstore 覆蓋用的新版本。
- 依賴由 Thunderstore 自動安裝：BepInExPack、XUnity AutoTranslator。
