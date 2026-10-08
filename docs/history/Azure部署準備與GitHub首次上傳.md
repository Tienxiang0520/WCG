# Azure 部署準備與 GitHub 首次上傳

2026-10-08，使用者改以微軟 Azure 為部署目標，並選用既有 GitHub 帳號。

- 建立私人儲存庫 [Tienxiang0520/WCG](https://github.com/Tienxiang0520/WCG)，上傳 `main` 程式歷史；未上傳其他本地分支。
- 保留 README 與設計待討論文件的使用者未提交修改。
- 玩家存檔、玩家備份、`.runtime/`、Sites checkout 與執行中的本地服務均保留。
- 建置預設輸出改為 `.build-tmp/v06-site`；Azure 可用 `--azure` 輸出至 `.build-tmp/azure-site/dist`，不產生 Sites metadata。
- 增加 Azure 的頁面路由、WASM MIME 與快取設定，以及部署輸出檢查。
- 增加 GitHub 自動驗證流程；尚未接上 Azure 發布流程，亦未建立 Azure 網站資源。

本地驗證：184 項 .NET 測試、29 項靜態版測試通過。Azure 套件為 1200 個檔案、148.0 MB、199 張卡圖，符合 Free 單一環境 250 MB 限制。額外確認輸出檢查會拒絕 `.env`、私人牌組／天梯檔，以及錯誤的頁面 fallback。

遠端 GitHub Actions 啟動被帳號帳務設定阻擋。[第一次工作流程結果](https://github.com/Tienxiang0520/WCG/actions/runs/37721022568) 的畫面指出近期付款失敗或花費上限需要調整；未建立執行 job，因此未取得遠端測試或建置結果。只能由帳號擁有者決定帳務調整，或改採本機直接部署至 Azure。
