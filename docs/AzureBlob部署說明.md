# Azure Blob 靜態網站

遊戲網址：[魂誓 WCG](https://wcggametienxiang0520.z11.web.core.windows.net/)。卡圖、音效、程式及規則都在同一個 HTTPS 網站。第一次開啟會顯示完整下載讀條，約 128 MB；所有資源完成並通過 SHA-256 檢查後才進入。重新開啟會沿用瀏覽器快取，只補更新或缺少的資源。

## 主機與費用

- 訂用帳戶：既有 Azure for Students，支出限制 `On`。
- 資源群組：`wcg-game-rg`；儲存體：`wcggametienxiang0520`。
- 日本東部、Standard StorageV2、Hot、LRS，未啟用階層命名空間。
- HTTPS／最低 TLS 1.2。一般 Blob 匿名存取關閉；靜態網站端點依 Azure 設計提供公開唯讀的遊戲檔案。
- 沒有另外建立付費 App Service、CDN、Front Door、資料庫或自訂網域。

此方案依儲存、操作及流量計費，學生點數仍有效且足夠時可抵用。沒有固定的靜態主機月租；用量大仍會增加費用。費用模型與來源見 [儲存體主機評估](Azure儲存體主機評估.md)。本次不變更訂用帳戶的支出限制，也未查證剩餘點數。沒有建立郵件成本提醒；提醒也不等同費用封頂。

## 玩家存檔搬移

不同網址、不同瀏覽器的存檔彼此獨立。新網址不會自動取得舊網站或本地 5180 的資料。

1. 在實際有進度的原網址，開啟「設定」的存檔工具，按「匯出存檔」。
2. 到新網址的「設定」，選取剛下載的存檔，核對摘要後按「確認取代並匯入」。
3. 檢查自訂牌組、天梯紀錄與玩家頭像。若從舊版匯入，舊天梯本季成績會歸入「舊版電腦」歷史，生涯最佳保留，依新版規則重新開始當季。

原網站、本地 5180、既有備份均保留。存檔及上傳頭像只保存在瀏覽器，絕不可上傳到公開 `$web`。舊程式不能讀取含新 `profile` 欄位的存檔，不要將新版備份匯回舊程式。

## 建置與更新

沿用 .NET 10、Node.js 及卡圖準備環境。公開網站的建置根目錄只能是 `dist`。

```sh
dotnet test WcgTests/WcgTests.csproj --nologo -c Release
python3 SoulOath.Static/tools/test-deploy-blob.py
node SoulOath.Static/tools/build-site.mjs .build-tmp/azure-blob-site --azure
node SoulOath.Static/tools/check-azure-output.mjs .build-tmp/azure-blob-site/dist
python3 SoulOath.Static/tools/deploy-blob.py .build-tmp/azure-blob-site/dist
```

最後一行只檢查部署內容，不會上傳。部署工具驗證資源清單、雜湊、199 張卡圖、入口頁、私有檔案與符號連結；音效授權文字保留。忽略未使用的壓縮副本與其他主機設定，發布約 128 MB 的實際資源。

上傳使用已登入的官方 Azure CLI 與 `azure-storage-blob` Python SDK。此工作站既有 Azure CLI 環境包含該 SDK，登入設定位於本機 `.runtime/azure-cli-config`；兩者都不提交 Git。換機時需先由使用者登入 Azure，並使用裝有該 SDK 的 Python 環境。

```sh
AZURE_CONFIG_DIR=/home/pudding/project/WCG/.runtime/azure-cli-config \
  .build-tmp/azure-cli/bin/python SoulOath.Static/tools/deploy-blob.py \
  .build-tmp/azure-blob-site/dist --publish \
  --az /home/pudding/project/WCG/.build-tmp/azure-cli/bin/az \
  --record .runtime/azure-blob-deployment.json
```

帳戶管理權限取得的儲存體金鑰只存在部署程序記憶體，不輸出、不存檔、不放在命令列，也不新增資料角色授權。若管理者改用其他授權限制，需先調整部署認證方式；不要停用既有保護來繞過限制。

工具比對遠端雜湊及 HTTP 屬性，只上傳變動檔案。先上傳並驗證資源，再發布清單及各入口，根首頁最後發布。部署回條只記錄主機、版本、檔案數與大小；原 App Service 回條保留。舊版本檔案不會自動刪除，讓已開啟的分頁繼續完成對戰。

## 頁面與檔案設定

`$web` 的首頁為 `index.html`，錯誤頁為 `not-found/index.html`。Blob 不讀取 IIS 或 Static Web Apps 的轉址設定，因此部署工具另外發布 `/cards`、`/deckbuilder`、`/ranked`、`/battle`、`/rules`、`/settings`、`/tutorial` 等實際 HTML Blob，支援直接開啟；各目錄的 `index.html` 同時保留。遺失資源仍回傳 HTTP 404。

所有檔案明確設定 Content-Type，包含 `application/wasm`、JavaScript、WebP 與 Ogg。`Cache-Control: no-cache` 讓瀏覽器檢查網站更新；遊戲完整下載快取仍由既有 Service Worker 與資源雜湊控制，已下載的圖片不因重新開啟就全部重抓。

## 試玩

先開新網址等讀條完成；到圖鑑看卡圖、設定看頭像，再到訓練場開始一場對戰。拖曳手牌到格位、拖曳怪物到目標，檢查指向線、召喚及攻擊動畫。右鍵可切換蓋牌，點擊卡片可查看資訊。

新手可從側邊選單的「教學」開始，共 13 課。更新前未回答教學提示的玩家也會看到一次邀請，可選稍後或略過。完成課程的紀錄跟著玩家頭像一起保存在 `profile`，並隨靜態版存檔匯出／匯入。

建議同一網址只保留一個有存檔寫入權的分頁。手機版需要再以實機確認；本次部署不會讓模擬器測試自動變成實機驗證。

官方參考：[Storage 靜態網站](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blob-static-website)、[Blazor 部署到 Azure Storage](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly/azure-storage?view=aspnetcore-10.0)。
