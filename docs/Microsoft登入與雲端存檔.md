# Microsoft 登入與雲端存檔

## 玩家操作

1. 在設定頁或側邊選單按「使用 Microsoft 登入」，由 Microsoft 處理帳號、密碼及多因素驗證。
2. 第一次登入時比較本機與雲端進度摘要，選擇「上傳目前本機進度」或「使用雲端進度」。沒有雲端存檔時也可建立新進度。訪客存檔會保留。
3. 日後操作先寫入本機，再自動同步；只有收到伺服器確認後才顯示「雲端已同步」。等待連線、登入過期或存檔驗證失敗時，尚未同步的進度保留在該帳號的本機快取。
4. 每個帳號同時只有一個裝置持有寫入控制權。第二個裝置可按「在此裝置接續」；原裝置失去控制權後不能再上傳。
5. 有版本衝突時不自動覆蓋，先比較兩份進度再選擇。選擇時另外保留未同步的本機副本，可用「匯出保留的本機進度」取回。
6. 登出後回到訪客進度，該 Microsoft 帳號的雲端及未同步本機進度仍保留。登出遊戲不會要求其他 Microsoft 網站一起登出。

同步範圍：自訂牌組、天梯星數與賽季、未完成對局、最近對戰紀錄、教學完成狀態、頭像、介面語言、填能量確認、側邊欄寬度。音量及特效偏好維持各裝置原本的設定。

登入快取使用 MSAL 的 `localStorage`，關閉分頁後重開會恢復已選取的帳號。整個瀏覽器關閉後能否保留登入，仍依 Microsoft 的「保持登入」、組織政策與瀏覽器設定；未選擇保持登入、清除網站資料或登入過期時，可能需要重新登入。由舊版分頁暫存更新後，請先登入一次建立新快取；玩家存檔與待同步快取保留。[Microsoft 快取規則](https://learn.microsoft.com/en-us/entra/msal/javascript/browser/caching)

同一瀏覽器使用持續的裝置識別，重開不會被當成另一台裝置。Web Locks 仍只允許一個分頁寫入；其他分頁顯示「另一個魂誓分頁正在使用」，請關閉另一頁並重新整理。唯讀分頁不取得、更新或釋放正在使用的雲端控制權。登出使用 MSAL `clearCache` 清除本遊戲帳號，不會改成另一個已快取的帳號，也不會要求其他 Microsoft 網站一起登出。

這是目前單人遊戲的雲端存檔與跨裝置同步；對戰仍在瀏覽器運算。既有 JSON 備份格式可繼續使用，備份中沒有登入憑證。

## 部署與權限

- 遊戲：<https://wcggametienxiang0520.z11.web.core.windows.net/>
- 存檔 API：<https://wcg-cloud-tienxiang0520.azurewebsites.net/api>
- 私人儲存帳號：`wcgcloudtienxiang0520`，容器 `player-saves`，與公開網站儲存分開。
- Microsoft 應用程式註冊：`WCG Cloud Save`（前端）、`WCG Cloud Save API`（API），支援個人及工作／學校 Microsoft 帳號。組織帳號仍須遵守所屬組織的同意政策。
- 前端使用 MSAL 與 PKCE；公開設定為 `SoulOath.Static/wwwroot/cloud-config.json`。此檔只包含公開 Client ID、Authority、Scope 與 API URL。
- API 驗證 Microsoft 簽章、有效期限、Issuer、API Audience 及 `Save.ReadWrite` 權限；以驗證後的 Tenant ID／Object ID 決定玩家路徑，不接受前端指定玩家身分。
- Function 的系統受控身分只獲得 `player-saves` 容器的 Storage Blob Data Contributor 權限。前端沒有儲存帳號金鑰。
- API 只允許既有 Azure 網站及本地 5180／5186 的來源。私人容器禁止匿名存取。
- 每次寫入核對 ETag 及 60 秒裝置租約；前端每 20 秒更新控制權。存檔上限 5 MiB，雲端資料會重用現行牌組、頭像與天梯還原驗證。
- Azure 保存先前版本 7 天，生命週期規則只處理 `player-saves/players/`，不刪除目前存檔。

API 採 Flex Consumption、512 MB 執行個體、沒有 Always Ready 常駐個體，最大 40 個執行個體。運算、儲存與流量依實際用量計費；學生訂閱額度與正式帳單以 Azure 為準，不把一般付費訂閱的免費額度視為學生訂閱的保證。

## 更新程式

先登入管理這些既有資源的 Azure 訂閱，若此電腦沿用既有登入，可指定 `AZURE_CONFIG_DIR=/home/pudding/project/WCG/.runtime/azure-cli-config`。不把此資料夾或任何登入憑證加入 Git。

```bash
dotnet restore Wcg.Cloud/Wcg.Cloud.csproj
dotnet test Wcg.Cloud.Tests/Wcg.Cloud.Tests.csproj --nologo
dotnet test WcgTests/WcgTests.csproj --nologo
node SoulOath.Static/tools/build-site.mjs .build-tmp/cloud-save --azure
python3 tools/CloudSave/deploy_api.py --publish --az /home/pudding/project/WCG/.build-tmp/azure-cli/bin/az
```

Functions SDK 會將索引資料產生至組件中，套件包含 `host.json`、`worker.config.json`、API 組件及依賴；不要求舊 SDK 的 `functions.metadata` 檔案。新 SDK 的還原需先對 Function 專案直接執行。

網站部署沿用 `SoulOath.Static/tools/deploy-blob.py`。本地預覽採逐檔原子更新並保留舊資源；不修改玩家資料、原預覽服務或獨立的 Sites checkout。

## 驗證範圍

- 遊戲回歸：388 項 .NET 測試。
- API：14 項 .NET 測試，含不同玩家身分、錯誤 Audience／Scope／Issuer、過期與偽造憑證、非法牌組、頭像、設定，以及匿名 API 拒絕。
- 同步及本機儲存：27 項測試，含帳號隔離、離線重試、衝突保留、同步期間新操作、控制權接手及待同步標記寫入失敗。
- 靜態建置的前端測試總計 91 項。
- 真實 Azure Blob 測試：讀寫、拒絕舊 ETag、拒絕另一裝置、接手後拒絕原裝置、玩家路徑隔離；使用隨機測試路徑並刪除測試主檔。此測試使用管理者的臨時儲存授權，不等同 Microsoft 玩家登入測試。
- API 已回應健康檢查，匿名讀取回覆 401，合法來源的 CORS 預檢通過，其他來源不獲得允許。
- 本地及正式站介面已確認中英文、桌面／390 px 手機、收合選單，並成功進入 Microsoft 登入頁；取消登入後訪客進度保留。
- 正式站 757 個部署檔案已驗證，登入回跳使用專用頁面；主要登入資源與存檔設定另以公開網址讀回，逐檔雜湊相同。

2026-10-13 使用者已完成本人 Microsoft 登入，提供「雲端已同步」與同步時間的畫面，確認首次同步已收到伺服器回覆。跨裝置取回同一玩家資料仍待第二台裝置實測，不能只由上述模擬身分或儲存測試推定成功。

想學會自己修改與操作 Azure，可從 [專案自學與 Azure 操作教學](專案自學與Azure操作教學.md) 開始。

## 官方文件

- [Microsoft 身分平台與 PKCE](https://learn.microsoft.com/en-us/entra/identity-platform/v2-app-types)
- [Microsoft 支援的帳號類型](https://learn.microsoft.com/en-us/entra/identity-platform/howto-modify-supported-accounts)
- [Blob ETag 與裝置租約](https://learn.microsoft.com/en-us/azure/storage/blobs/concurrency-manage)
- [使用受控身分存取 Blob](https://learn.microsoft.com/en-us/azure/storage/blobs/authorize-access-azure-active-directory)
- [Functions Flex Consumption](https://learn.microsoft.com/en-us/azure/azure-functions/flex-consumption-plan)
- [Functions 計費](https://azure.microsoft.com/en-us/pricing/details/functions/)
