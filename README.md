# WCG · 魂誓

**WCG（Web-to-Card Game）**

一種以網頁作為主要遊玩、學習與構築平台，但所有核心規則皆可由實體卡牌執行，並允許玩家將數位牌組輸出為可列印實體牌組的構築式卡牌遊戲。

WCG 是本專案採用的遊戲形式與資料夾名稱；本作遊戲名稱為「魂誓」。

- 遊戲網址：[魂誓 WCG](https://wcggametienxiang0520.z11.web.core.windows.net/)（Azure Blob 靜態網站）
- 原始碼：GitHub `Tienxiang0520/WCG`。功能以分支與 Pull Request 開發，經使用者同意後才合併到 `main`。
- 卡牌：262 張，編號 `WCG-001` 至 `WCG-262`。現行資料是 `WcgWeb/Data/cards.json`，[cards.md](cards.md) 由它產生；規則見 [規則.md](規則.md)。

## 主要內容

| 資料夾 | 用途 |
| --- | --- |
| `WcgWeb` | 共用遊戲規則、AI、卡牌資料、畫面元件及原伺服器版 |
| `SoulOath.Static` | 在玩家瀏覽器執行的 Blazor WebAssembly 靜態版（正式網站），含建置、檢查、部署工具（`tools/`） |
| `Wcg.Cloud` | Microsoft 登入後的雲端存檔 API（Azure Function） |
| `WcgTests`、`Wcg.Cloud.Tests` | 規則、AI、天梯、列印、存檔與雲端 API 測試 |
| `tools` | 卡表文件、天梯牌組、平衡檢查等工具，以及 `check-all.sh` |
| `deployment` | 主機設定與說明範本 |
| `art-production` | 卡圖生成提示 |
| `docs` | 操作指南；`docs/history` 是歷次改版與平衡紀錄 |

## 本機建置與預覽

需要 .NET 10 SDK、Node.js 與 Python 3（含 Pillow，用於卡圖）。

```sh
node SoulOath.Static/tools/build-site.mjs --azure .build-tmp/azure-blob-site
node SoulOath.Static/tools/serve-static.mjs .build-tmp/azure-blob-site/dist 5180
```

開啟 <http://127.0.0.1:5180/>。網站第一次開啟會完整下載所有資源並驗證後才進入；之後只補更新的檔案。原伺服器版可用 `dotnet run --project WcgWeb` 預覽。

牌組構築室可列印編輯中的 50 張牌組（彩色、白底彩色、省墨），見 [列印說明](docs/列印說明.md)。

## 檢查

```sh
tools/check-all.sh          # 全部檢查（約 6 分鐘）
tools/check-all.sh --fast   # 略過標記為 Slow 的長時間測試與網站建置（約 2 分鐘）
```

會依序執行：

- 預設語系與 `zh_CN.UTF-8` 的 `WcgTests`；
- `Wcg.Cloud.Tests`；
- `cards.md` 與天梯牌組的同步檢查；
- 部署工具測試；
- Azure 網站建置（含 Node 測試）與輸出檢查。

只想跑單一測試專案時可直接 `dotnet test WcgTests`；加上 `--filter "Category!=Slow"` 可略過長時間的天梯與 AI 模擬測試。

## 部署

網站與雲端存檔 API 的建置、驗證與發布步驟見：

- [Azure 部署說明](docs/Azure部署說明.md)
- [Azure Blob 部署說明](docs/AzureBlob部署說明.md)
- [Microsoft 登入與雲端存檔](docs/Microsoft登入與雲端存檔.md)

部署工具預設只檢查不上傳，需明確加上 `--publish` 才會發布。帳號、金鑰與玩家存檔不放進 Git。

程式與 Azure 的入門教材見 [專案自學與 Azure 操作教學](docs/專案自學與Azure操作教學.md)。

## 歷史

- 2026-10-04 曾以 Sites 發布私人測試版；2026-10-13 起改用 Azure Blob，舊 App Service 已刪除。見 [靜態部署說明](docs/靜態部署說明.md)（歷史紀錄）與 [Azure 部署說明](docs/Azure部署說明.md)。
- 2026-10-05 完成 [WCG 命名整理](docs/WCG命名整理.md)，包含程式專案、卡牌編號與舊存檔相容性；整理範圍見 [專案整理紀錄](docs/專案整理.md)。
- 卡牌設計想法見 [設計待討論](docs/設計待討論.md)，後續建議見 [專案建議](docs/專案建議.md)。
