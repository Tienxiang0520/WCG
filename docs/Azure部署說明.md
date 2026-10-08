# Azure 部署

本專案改以微軟 Azure 為部署目標。學生訂用帳戶的允許區域與 Static Web Apps 五個後端區域沒有交集，因此目前改用 **Windows App Service、日本東部、F1 免費方案**。網站資源 `wcg-game-tienxiang0520` 已建立；原始碼仍在私人 GitHub 儲存庫。私人原始碼與網站存取權是不同設定，部署的遊戲網站提供公開存取，玩家資料留在瀏覽器。

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

## 首次載入

首頁會顯示下載進度條與百分比；達到 100% 後繼續顯示正在準備卡牌與對戰，完成後進入遊戲。進度以實際載入的啟動檔案數量計算，網路快或已有快取時會直接完成。載入失敗時可按重新整理重試。

## Windows App Service 本機部署

目前資源群組為 `wcg-game-rg`，方案 `ASP-wcggamerg-a454`、SKU `F1`、區域 `Japan East`。網站：

[Azure 遊戲網址](https://wcg-game-tienxiang0520-a7hphvhuexh8ekbh.japaneast-01.azurewebsites.net/)

遊戲仍是相同的 WebAssembly 靜態版；App Service 只負責提供檔案，未上傳 `WcgWeb` 伺服器版與本地玩家資料。

```sh
node SoulOath.Static/tools/build-site.mjs .build-tmp/azure-site --azure
python3 SoulOath.Static/tools/prepare-appservice.py
```

部署包是 `.build-tmp/azure-appservice.zip`，內容為靜態網站與 `deployment/azure/appservice-web.config`。IIS 設定支援 WebAssembly、卡圖、音效、字型及頁面刷新，遺失的資源仍回傳 404。只部署這個包，不上傳整份專案。

本地 Azure CLI 2.91.0 安裝在 `.build-tmp/azure-cli`。登入設定保存在 `.runtime/azure-cli-config`，不可提交或上傳；這個目錄屬於需保留的本機資料。部署沿用 Microsoft Entra 授權，網站的基本部署驗證維持停用。

```sh
AZURE_CONFIG_DIR=/home/pudding/project/WCG/.runtime/azure-cli-config \
  .build-tmp/azure-cli/bin/az webapp deploy \
  --resource-group wcg-game-rg --name wcg-game-tienxiang0520 \
  --src-path .build-tmp/azure-appservice.zip --type zip \
  --clean true --restart true --timeout 600000 --output none
```

F1 為試玩／開發測試方案，有每日 CPU 與流量配額且沒有正式服務 SLA；需要更多流量時再評估方案，不能把測試成功視為大型公開服務的容量保證。[App Service 免費方案說明](https://azure.microsoft.com/en-us/pricing/details/app-service/windows/)

官方文件：[Azure 建置設定](https://learn.microsoft.com/en-us/azure/static-web-apps/build-configuration)、[網站設定](https://learn.microsoft.com/en-us/azure/static-web-apps/configuration)、[免費方案限制](https://learn.microsoft.com/en-us/azure/static-web-apps/quotas)。
