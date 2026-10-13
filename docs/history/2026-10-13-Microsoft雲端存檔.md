# Microsoft 登入與雲端存檔

日期：2026-10-13。沿用原 Azure 靜態網站，加入 Microsoft 個人及組織帳號登入。網站來源與私人成員存檔分開；獨立的 Sites 專案未更新。

實作採 MSAL、PKCE、C# Azure Functions 與私人 Azure Blob。訪客資料保留，各帳號使用獨立本機快取；離線進度先保存本機，雲端寫入確認後才顯示同步完成。首次登入及版本衝突由玩家比較進度後選擇，另保留未同步副本。ETag 與 60 秒 Blob 租約處理舊版本和其他裝置寫入，支援明確接手。

API 部署：`wcg-cloud-tienxiang0520`，Flex Consumption .NET 10，512 MB，沒有 Always Ready。私人儲存：`wcgcloudtienxiang0520/player-saves`；受控身分只授權該容器，先前版本保存 7 天。前端不包含儲存金鑰，備份不包含登入憑證。

通過 388 項遊戲回歸、14 項 API、85 項前端及 10 項部署邊界測試。真實 Blob 讀寫、舊 ETag 拒絕、裝置接手與玩家路徑隔離已驗證；匿名 API 回覆 401。瀏覽器已確認中英文、桌面及 390 px 版面、收合側欄及進入 Microsoft 登入頁，取消登入後訪客進度不變。

上述 Blob 測試使用臨時管理者儲存授權，不等同玩家登入驗證。本人帳號登入、第一次上傳與跨裝置取回仍待人工作業確認。操作與更新程序見 [Microsoft 登入與雲端存檔](../Microsoft登入與雲端存檔.md)。部署與測試收據在被忽略的 `.runtime/`、`.build-tmp/`，未將登入憑證或本機玩家資料提交。
