# Azure Blob 主機搬遷

日期：2026-10-09。使用者同意依儲存體主機評估，將現行人機 WebAssembly 靜態版移到日本東部 Blob Storage，保留學生支出限制與舊主機。

## 部署結果

- 新網址：<https://wcggametienxiang0520.z11.web.core.windows.net/>。
- `wcg-game-rg`／`wcggametienxiang0520`，Japan East、StorageV2、Standard_LRS、Hot；HTTPS、TLS 1.2、HNS 關閉。
- Microsoft.Storage 註冊完成；一般 Blob 匿名存取關閉，靜態網站提供公開唯讀遊戲檔案。
- 訂用帳戶 Enabled，AzureForStudents，`spendingLimit=On`；未升級訂用帳戶、查證剩餘點數或新增成本提醒收件者。
- 沿用已驗證的新版建置 `.build-tmp/v06-rebuild/dist`，包含 main `7d4c55a` 的天梯與前端功能。此前本機 .NET 202 項、靜態版 50 項、天梯牌組產生器與 Azure 輸出檢查均通過。
- 資源版本 `d0053f4458cadc3999672841a6f9815631a84e031998bc47a1b0e5b73cd75a20`；完整下載清單 682 項、約 127.8 MB，含 199 張卡圖。
- 實際發布 706 個 Blob、127,958,229 bytes（含 HTML 入口、授權文字、工作程序及清單）。保留原始公開資源，不重新生成卡圖；排除未使用的壓縮副本與其他主機設定。

## 部署工具

新增 `SoulOath.Static/tools/deploy-blob.py`，預設只檢查，`--publish` 才上傳。拒絕私有路徑、符號連結、未知檔案、缺少／損壞資源、清單漏列及不一致的入口頁。明確設定 Content-Type 與 no-cache；建立實際的 `/cards`、`/ranked`、`/settings` 等 Blob 入口，遺失資源保持 HTTP 404。

已完成檔案依遠端 SHA-256 metadata、大小與 HTTP 屬性比對而跳過。資源先上傳並核對，清單及各入口隨後發布，根首頁最後發布；不刪除舊檔案。金鑰只在程序記憶體內使用，不記錄或傳到命令列，也未新增帳號角色。

首批一個大型程式檔案上傳逾時，首頁保持未發布；重跑只補 19 個檔案（大型資源與 18 個入口／清單），約 5 MB，後續核對通過。部署工具補上較長的連線逾時、分塊上傳與失敗項目回報。再次執行顯示 `Upload 0/706 files, 0.0 MB`。

部署邊界測試 8 項通過。HTTP 檢查確認首頁、各路由與 `/cards/` 為 200，wasm／WebP 類型正確；遺失資源、玩家頭像 JSON、登入設定路徑與 Static Web Apps 設定皆為 404。輸出保存在忽略的 `output/azure-blob-http-check.json`，部署回條在 `.runtime/azure-blob-deployment.json`，原 App Service 回條保留。

## 資料與服務保留

原 F1 網站資源未變更，驗證時首頁 HTTP 200。本地 `wcg-v06-preview.service` 仍 active，5180 試玩繼續可用。玩家存檔、備份與 Sites checkout 保留，未發布 Sites，未修改 GitHub workflows。README 與設計待討論的使用者修改未納入此次提交。

新網址與舊網址的瀏覽器資料彼此獨立。使用者尚未指定存檔來源，本次不代選來源；搬移步驟放在 [Azure Blob 部署說明](../AzureBlob部署說明.md)。

## 瀏覽器驗收

慢速首次下載顯示真實進度，截圖 `output/azure-blob-slow-download.png` 為 2%、2.7／127.8 MB、7／682 項。限制網路並阻擋一張卡圖的測試中，資源未完成時停在 82%，顯示重新整理接續提示，沒有進入遊戲；恢復網路及阻擋設定後，重新整理可接續完成。

- 進入圖鑑後確認 199 個卡圖元素全部 `complete` 且 `naturalWidth > 0`，截圖 `output/azure-blob-cards.png`。
- 重開 `/deckbuilder` 後，圖鑑的 199 張 WebP 回應均來自 Service Worker。新網址確實使用完整下載快取。
- 設定頁離線仍能開啟。八款頭像可見，更換星辰賢者並重新開啟後保留；測試牌組「Azure 搬遷驗證」保存為合法 50 張牌組，重新開啟後 WebMCP 讀到相同內容。
- 匯出檔案實際落在本機，測試備份含一副牌組及 `profile.Avatar`；選擇備份、摘要與確認匯入流程通過。測試結束匯回該新網址初始空白備份，確認牌組為空且頭像恢復預設，未代選舊網址／5180 的真實存檔。
- 天梯大廳顯示新版青銅 9 流派對手池，本季 0 勝 0 敗；沒有進行天梯測試局。
- 訓練模式實際拖曳地獄戰狂進第三格，拖曳期間顯示指向線、合法格位與落點；放開後進場，能量由 10 降至 5。拖曳攻擊電腦英雄，生命由 7 降至 5。
- 恢復電腦自動行動後結束回合，電腦填能量、召喚靈光學徒，回到玩家第 3 回合。再透過訓練工具將電腦生命設為 2，以合法攻擊結束測試局，顯示本局獲勝與英雄碎裂；沒有保存測試場面或增加天梯戰績。
- 完整特效、扇形手牌、戰場滿版卡圖與攻擊箭頭可見。截圖 `output/azure-blob-drag-aim.png`、`output/azure-blob-attack-aim.png`、`output/azure-blob-victory.png`。實測瀏覽器 console 沒有 error。

測試後已恢復正常網路、取消阻擋與暫時的 HTTP 快取停用，返回首頁並重新開啟以清除暫時場面。所有截圖與測試輸出留在忽略的 `output/`，不公開玩家資料。此輪為桌面瀏覽器驗證，手機實機仍待使用者試玩。
