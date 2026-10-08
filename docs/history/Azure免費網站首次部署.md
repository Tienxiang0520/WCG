# Azure 免費網站首次部署

2026-10-08，完成微軟 Azure 部署。使用者在 GitHub 帳務驗證受阻且暫無付款卡片時，選擇先由本機直接部署。

## 網站與資源

- [遊戲網址](https://wcg-game-tienxiang0520-a7hphvhuexh8ekbh.japaneast-01.azurewebsites.net/)
- 網站：`wcg-game-tienxiang0520`。
- 資源群組：`wcg-game-rg`。
- App Service 方案：`ASP-wcggamerg-a454`，Windows、日本東部、`F1` 免費 SKU。
- HTTPS-only 已啟用，基本部署驗證維持停用，Application Insights 與付費 Defender 未啟用。

原 Static Web Apps 建立遭 `RequestDisallowedByAzure` 拒絕。實際讀取學生訂用帳戶的 `Allowed resource deployment regions` 原則後，確認允許清單為 `indiasouthcentral`、`indonesiacentral`、`uaenorth`、`japaneast`、`malaysiawest`，與 Static Web Apps 當時五個後端區域沒有交集。改以允許的日本東部建立 App Service，入口網站顯示部署完成，CLI 再確認 F1 與 Running。

## 部署方式

保留 WebAssembly 靜態架構，只上傳遊戲公開資產。ZIP 部署包約 127.0 MB、解壓後約 148.0 MB，包含 199 張卡圖。使用 Microsoft Entra 登入取得部署授權，未啟用基本驗證，未在聊天或原始碼保存憑證。登入快取位於忽略的 `.runtime/azure-cli-config`，須視為需保留的本機資料。

使用 IIS 設定提供 WASM、JavaScript、卡圖、音效及字型內容類型，支援路由刷新。JavaScript 類型另外以小型配置部署調整為 `text/javascript`。

## 驗證

- 部署前同一份遊戲原始碼的 184 項 .NET 與 29 項靜態版檢查通過。
- 線上 199 張卡牌 JSON 與本地輸出 SHA-256 完全一致；199 張卡圖均成功回應正確圖片類型。
- WASM 類型、JavaScript 類型與設定／圖鑑／組牌／天梯四個頁面入口成功。實體路由資料夾的正常 301 轉址會導向 index 頁；遺失的 WASM 檔回傳 404。
- Chrome 實際載入訓練場，擺放主動結界、調整生命與能量、拖曳突擊狼騎兵出牌並攻擊，敵方生命 7 → 6。
- 主動結界可行動綠光、詳情彈窗、發動選牌與橫置、AI 單步填能量、AI 自動回合均通過。
- 保存訓練場面，重新整理後成功還原同一批卡片、生命、能量與回合。
- 組牌頁顯示 199 張卡；設定值可保存並在刷新後保留，測試後恢復原值。
- 瀏覽器警告／錯誤紀錄為空。實際戰場截圖為忽略的 `output/azure-game-live.jpg`。

第一次多連線 HTTP 圖片檢查發生連線逾時，改用重用連線與重試後完成全部卡圖檢查；初次 JavaScript 型別檢查也指出 IIS 預設舊型別，已調整並驗證。這些問題未造成遊戲規則變更。

## 存檔與限制

未修改本地玩家進度、玩家備份或使用者的未提交文件修改。5180 預覽服務仍運作，Sites checkout 無修改亦未發布。GitHub 保持私人，帳務恢復前仍以本機直接部署，未宣稱 GitHub 自動流程已可執行。

新網址的存檔仍留在瀏覽器，需從原網址設定頁匯出，再到新網址設定頁匯入。訓練驗證只留下獨立的測試場面，未新增天梯戰績或自訂測試牌組。

F1 適合開發與試玩，有每日 CPU／流量配額及無 SLA 的限制；本次成功驗證不代表大型公開遊戲流量已受支援。
