# WCG 本地 Git

在 `/home/pudding/project/WCG` 使用。主專案以本地 Git 保存版本，目前也有 GitHub 遠端 `origin`（`https://github.com/Tienxiang0520/WCG.git`）。本機提交與推送是不同動作，推送不會自動更新 Azure。循序實作見 [專案自學與 Azure 操作教學](專案自學與Azure操作教學.md)。

## 日常保存修改

```bash
git status
git diff
git add 這次要提交的檔案路徑
git diff --cached
git commit -m "描述這次修改"
git log --oneline -8
```

`這次要提交的檔案路徑` 是佔位文字，要換成實際檔案。只選這次修改的檔案，避免包含別的未完成工作；`.gitignore` 排除玩家資料、登入資料、套件、建置結果及 Sites 獨立 Git。首次提交使用本機識別 `pudding <pudding@localhost>`，可自行調整；目前可用 `git remote -v` 查遠端，選好分支後用 `git push` 推送。玩家進度的 Microsoft 雲端同步與 Git 分開。

## 查看與還原舊內容

清理前主程式標籤：`baseline/pre-cleanup-2026-10-05`。舊備份、廢稿及驗證資料分支：`archive/pre-cleanup`，另有標籤 `archive/legacy-2026-10-05`。

查看封存的檔案，不會改動現在的程式：

```bash
git ls-tree -r --name-only archive/pre-cleanup
git show archive/pre-cleanup:ARCHIVE.md
```

需要整批取出時，匯出到另一個資料夾：

```bash
mkdir -p ../WCG-歷史取出
git archive archive/pre-cleanup | tar -x -C ../WCG-歷史取出
```

舊伺服器壓縮包與 Phaser 原始碼壓縮包已解開後存於封存分支，可用上述檔案清單指令查看當時的實際路徑。重複卡圖由 Git 共用資料，沒有再保留整包壓縮副本。舊玩家資料另保留在被忽略的 `玩家存檔備份/整理前歷史/`。

需要查看某份主程式舊版，可先用 `git show`；要還原到工作目錄前，先確認現在的修改已保存。不要用版本切換刪除本機玩家資料。

## Sites 與建置

`SoulOath.Site/` 有自己的 Git，主分支保留正式發布來源，`archive/failed-initial-upload` 保留首次失敗上傳來源。這不是主專案的子模組；不向 Sites 推送 WCG 全部原始資料。

靜態建置會依鎖檔補齊缺少的前端套件，再產生壓縮卡圖、列印卡圖與發布檔案：

```bash
node SoulOath.Static/tools/build-site.mjs
```

可用輸出資料夾參數建立隔離驗證檔案，例如 `node SoulOath.Static/tools/build-site.mjs .build-tmp/site-check`，不必改動目前正在供應的預覽目錄。

本地 Git 仍需保留整個 `.git/`；刪除它會失去版本紀錄。若要更換電腦，需另外複製專案和 Git，玩家存檔再用遊戲備份功能搬移。

被忽略的檔案也包含玩家資料和正在使用的 Sites Git，因此不要對整個專案執行 `git clean -fdx` 或 `git clean -fdX`；清理時只移除已確認能重建的套件、`bin/`、`obj/` 和驗證輸出。
