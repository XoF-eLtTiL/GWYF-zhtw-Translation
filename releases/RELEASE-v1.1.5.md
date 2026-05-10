# GWYF zh-TW Translation Pack v1.1.5

## 更新內容

- TMP Patcher 內嵌繁中字體 AssetBundle，不再需要外部 `notosanstc-medium sdf`
- TMP Patcher 整合主選單 Logo 貼圖替換
- Logo 替換新增 fallback 名稱偵測，並排除 `tenstack_logo_grayscale_*`，保留給 XUnity Texture 翻譯處理
- Replacer 維持只處理指定 TMP 目標，降低掃描造成的卡頓
- Updater 遠端來源寫死並限制只同步翻譯文字/貼圖，避免下載或覆蓋插件
- 公開倉庫移除本機維護用 workflow/scripts 追蹤

## 安裝

將壓縮檔內容解壓到遊戲根目錄。

## 注意

- 已關閉 XUnity 自動翻譯 endpoint
- 已關閉 XUnity TMP 字體覆蓋
- BepInEx Console 視窗預設關閉
