# Azure 部署

現行主機改為 **Azure Blob Storage 靜態網站、日本東部、Hot LRS**。新網址：[魂誓 WCG](https://wcggametienxiang0520.z11.web.core.windows.net/)。後續建置、更新與存檔搬移請依 [Azure Blob 部署說明](AzureBlob部署說明.md)。

## 一鍵部署：網站＋雲端存檔 API

網站與雲端存檔 API（Function `wcg-cloud-tienxiang0520`）一定要一起部署。兩邊各自用自己的卡表驗證存檔；只更新網站時，舊 API 不認得新卡，就會出現「雲端存檔驗證未通過」（2026-10-13 發生過）。統一用下面這支指令，在儲存庫根目錄執行：

```sh
# 只建置與驗證，不上傳
python3 tools/deploy-all.py
# 實際部署（需先 az login；用裝有 azure-storage-blob 的 Python 執行）
/workspace/.azvenv/bin/python3 tools/deploy-all.py --publish --az "$(which az)"
```

流程：

1. 跑 `WcgTests`（`--skip-tests` 可略過；API 打包時一定會跑 `Wcg.Cloud.Tests`）。
2. 建置網站 `.build-tmp/azure-blob-site/dist`，執行 `check-azure-output.mjs`，並確認網站裡的 `data/cards.json`、`preset_decks.json`、`ranked_decks.json` 與 `WcgWeb/Data` 完全相同。
3. `deploy-blob.py` 驗證網站部署內容（`--blob-arg` 可傳額外參數給它）。
4. 打包 API（`tools/CloudSave/deploy_api.py`）。沒有 `--publish` 就到此為止，不上傳任何東西。
5. 加 `--publish` 時，**先發布 API**（新卡表接受舊網站的存檔，反過來不行），再輪詢 API 的 `/health`，直到它回報的三個資料檔 SHA-256 與這次建置的網站相同（預設最多等 300 秒，`--api-timeout` 可調）；不一致就停止，不上傳網站。
6. 上傳網站，確認線上的 `asset-manifest.json` 版本與本機建置相同，最後寫入回條 `.runtime/wcg-deployment.json`（commit、網站資源版本、API 卡表雜湊，不含任何憑證）。

建置工具（卡圖需要 Pillow）會用系統的 `python3`，即使整支指令是用上傳用的虛擬環境執行；`--blob-python` 可以另外指定上傳網站用的 Python。

### 版本檢查

API 的 `GET /api/health` 回傳 `data: { cards, presets, rankedDecks }`，是它內建資料檔的 SHA-256。網站登入 Microsoft 後會在背景比對自己正在使用的同名資料檔；不一致時，「Microsoft 帳號與雲端存檔」面板會提示「雲端存檔伺服器與網站的卡表版本不同，同步可能被拒絕」，進度先保存在本機。舊版 API 沒有這個欄位時視為未知，不提示。

---

以下保留先前 Static Web Apps／Windows App Service F1 的部署紀錄供查考，並非現行更新指令。2026-10-13 已刪除舊 F1 資源 `wcg-game-tienxiang0520` 與方案 `ASP-wcggamerg-a454`；下方舊主機網址及指令僅作歷史紀錄。原始碼儲存庫已於 2026-10-09 依使用者要求改為公開。訪客進度保存在瀏覽器；Microsoft 玩家進度另存私人儲存體，詳見 [登入與雲端存檔](Microsoft登入與雲端存檔.md)。從修改程式到 Azure 操作的循序教材見 [專案自學與 Azure 操作教學](專案自學與Azure操作教學.md)。

公開儲存庫：[Tienxiang0520/WCG](https://github.com/Tienxiang0520/WCG)，主分支 `main`。2026-10-08 首次上傳已完成。GitHub Actions 尚未實際執行：帳號的付款或花費上限設定阻止工作流程啟動。需由帳號擁有者檢查 Billing & plans；也可先用本機建置搭配 Azure 官方 CLI 直接部署，GitHub 繼續保存程式。此限制與 Azure Free 方案分開。

## 建置與驗證

需要 .NET 10 SDK、Node.js 22、Python 3 與 Pillow 12.1.1。建置過程會安裝前端鎖定依賴，準備現行卡表（`WcgWeb/Data/cards.json`）的卡圖與列印圖，執行靜態版檢查。

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
- 來源：GitHub，選遊戲儲存庫，分支 `main`。

Azure 的預設 Blazor 工作流程不足以處理本專案共用元件、前端與卡圖準備。必須在部署前將流程改成安裝上述工具、執行本專案建置，再將 `app_location` 指向 `.build-tmp/azure-site/dist`，設 `skip_app_build: true`，`output_location: ''` 與空白 API 路徑。部署憑證存放 GitHub Actions secret，不寫入原始碼或聊天。

## 存檔

網站改網址不會自動搬移瀏覽器存檔。從目前實際有進度的網址，在設定頁匯出，再到新網址匯入；匯入前先檢查內容。原本本地存檔、備份和 5180 服務繼續保留。

## 首次載入

首頁會先下載完整遊戲資源，包含現行卡表的全部卡圖、音效、列印圖、介面與程式，全部完成並通過完整性檢查後才進入。讀條依實際收到或已保存在瀏覽器的資源大小計算，顯示百分比、MB 與完成項數；首次約 128 MB。下載中斷可重新整理，接續下載缺少的檔案。下次開啟只補缺少或更新的資源，不再重新下載全部卡圖。

請保留此網站的瀏覽器資料。資源快取與玩家存檔分開；載入流程不會清除存檔。新網址、不同瀏覽器或清除資源快取後，需要重新下載。網站首頁與清單會檢查更新，已開啟的對戰分頁沿用自身版本。

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

## 2026-10-08 配額狀態

完整下載更新已部署，但線上驗證途中 F1 每日輸出流量達上限，網站回傳 403 Site Disabled。管理 API 確認 QuotaExceeded，下一次流量重置為台灣時間 2026-10-09 08:00；重置後重新整理可接續已下載的資源。此狀態與卡圖完整性或存檔無關。

F1 每日流量上限為 165 MiB，而完整首次下載約 128 MB，無法作為多人試玩的長期主機。保留免費方案時只能等額度重置；更換 Azure 主機或靜態儲存體之前，應先確認使用方式與費用。[微軟配額與停止服務說明](https://learn.microsoft.com/en-us/azure/app-service/web-sites-monitor)
