# Azure 部署

本專案改以 Azure Static Web Apps Free 作為下一個部署目標。GitHub 儲存庫使用私人可見性；GitHub 私人原始碼與 Azure 網站存取權是不同設定，網站存取權仍需另行決定。

私人儲存庫：[Tienxiang0520/WCG](https://github.com/Tienxiang0520/WCG)，主分支 `main`。2026-10-08 首次上傳已完成。GitHub Actions 尚未實際執行：帳號的付款或花費上限設定阻止工作流程啟動。需由帳號擁有者檢查 Billing & plans；也可先用本機建置搭配 Azure 官方 CLI 直接部署，GitHub 繼續保存程式。此限制與 Azure Free 方案分開。

## 建置與驗證

需要 .NET 10 SDK、Node.js 22、Python 3 與 Pillow 12.1.1。建置過程會安裝前端鎖定依賴，準備 199 張卡圖與列印圖，執行靜態版檢查。

```sh
dotnet test WcgTests/WcgTests.csproj --nologo -c Release
node SoulOath.Static/tools/build-site.mjs .build-tmp/azure-site --azure
node SoulOath.Static/tools/check-azure-output.mjs .build-tmp/azure-site/dist
```

Azure 只應部署 `.build-tmp/azure-site/dist`。不可把整份原始碼、`.runtime/` 或玩家備份當作網站根目錄。GitHub 的 `Verify WCG Azure build` 流程先驗證規則、建置與公開輸出；Azure 資源建立後，需再接上 Azure 產生的部署流程和部署授權，才能發布網站。

自動建置改用安全的本地輸出目錄，預設不再更新 `SoulOath.Site/`。既有 Sites checkout 保留；除非明確指定該目錄，不會寫入 Sites 設定。此修改不會刪除或停止舊網站。

## Azure 建立表單

- 訂用帳戶：Azure for Students。
- 資源群組：`wcg-game-rg`。
- 名稱：`wcg-game`。
- 方案：Free。
- 來源：GitHub，選新建的私人遊戲儲存庫，分支 `main`。

Azure 的預設 Blazor 工作流程不足以處理本專案共用元件、前端與卡圖準備。必須在部署前將流程改成安裝上述工具、執行本專案建置，再將 `app_location` 指向 `.build-tmp/azure-site/dist`，設 `skip_app_build: true`，`output_location: ''` 與空白 API 路徑。部署憑證存放 GitHub Actions secret，不寫入原始碼或聊天。

## 存檔

網站改網址不會自動搬移瀏覽器存檔。從目前實際有進度的網址，在設定頁匯出，再到新網址匯入；匯入前先檢查內容。原本本地存檔、備份和 5180 服務繼續保留。

官方文件：[Azure 建置設定](https://learn.microsoft.com/en-us/azure/static-web-apps/build-configuration)、[網站設定](https://learn.microsoft.com/en-us/azure/static-web-apps/configuration)、[免費方案限制](https://learn.microsoft.com/en-us/azure/static-web-apps/quotas)。
