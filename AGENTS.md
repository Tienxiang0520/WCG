# WCG 專案工作方式

- 主專案使用本地 Git。修改前先檢查 `git status`，保留使用者正在修改的內容。
- 以 Git 提交保存程式版本，不再建立日期備份資料夾或整份專案壓縮包；使用者明確要求備份時例外。
- 操作指南放在 `docs/`，歷史改版紀錄放在 `docs/history/`。保留 `規則.md`、`cards.md` 和 `cards.ods` 作為現行設計資料。
- 玩家存檔、`玩家存檔備份/`、`.runtime/` 與正在執行的服務都是本機資料，不可因清理或版本切換而覆蓋或刪除。
- `SoulOath.Site/` 是獨立 Sites Git，已保留正式來源與初次失敗上傳分支；主專案 Git 不包含它。Sites 更新須沿用它的已發布歷史及既有 project_id，不可強制推送或再建立網站。
- 可重建的檔案已列於 `.gitignore`；驗證輸出放在被忽略的 `output/` 或 `.build-tmp/`。建置靜態版使用 `node SoulOath.Static/tools/build-site.mjs`。
