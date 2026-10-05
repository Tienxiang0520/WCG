# Phaser 對戰改造紀錄

驗收日期：2026-10-01。計畫階段 0–4 已實作並逐項驗收；新版為預設對戰畫面。

## 使用與回復

- 對戰：[http://localhost:5169/](http://localhost:5169/)，亦可使用 `/battle`。
- 固定測試場景：`/battle-lab`，使用獨立引擎，不修改正式對局或牌組。
- 原版回復入口：`/legacy`；同一 circuit 內共用規則、命令橋接與 AI 協調器。
- `LcgWeb/appsettings.json` 的 `BattlePresentation:Mode` 預設為 `phaser`；改為 `legacy` 並重啟，首頁會導向原版。
- 改造前原始碼備份：`備份/2026-10-01-Phaser改造前/source.tar.gz`。專案當時沒有 Git 儲存庫，因此保存檔案快照。

平常在專案根目錄執行：

```bash
dotnet run --project LcgWeb
```

`dotnet build/test/publish` 會建置前端；首次缺少套件時執行 `npm ci`。開發啟動使用原本的 launch profile（Development），才能載入未發布專案的框架靜態資產。正式部署則啟動發布目錄的 DLL：

```bash
dotnet publish LcgWeb -c Release -o /tmp/lcg-publish
dotnet /tmp/lcg-publish/LcgWeb.dll --urls http://localhost:5169 --contentRoot /tmp/lcg-publish
```

已有服務占用 5169 時，先停止該服務再啟動。完整重新整理／服務重啟會建立新 circuit，對局不持久化；同一 circuit 的頁面切換可保留對局。

## 已實作的分工

| 檔案 | 實作 |
| --- | --- |
| `Components/Pages/BattlePage.razor` | 房間、牌組、先攻、投降、重開與模式回復；動畫中的重開使用引擎目前版本 |
| `Components/Battle/BattleBoard.razor` | 模組與 Phaser 掛載、命令／確認／重同步、卸載和載入失敗提示 |
| `Models/Battle/BattleContracts.cs` | 命令、不可變可見快照、合法目標／預覽、待選流程、結構化事件 |
| `Services/BattleBridge.cs` | 命令白名單、固定玩家身分、去重、版本防護與一致性快照 |
| `Services/BattleCoordinator.cs` | 單一呈現者、玩家與 AI 步驟、動畫批次確認、8–35 秒逾時、取消與錯誤回復 |
| `Services/GameEngine.PresentationEvents.cs` 與引擎結算節點 | 抽牌、能量、支付、出牌、召喚、攻擊、傷害、狀態、死亡、離場觸發、回合及勝負事件 |
| `Client/battle/index.ts`、`contracts.ts` | Phaser 場景、瀏覽器內拖曳、高亮、箭頭、詳情、HTML／鍵盤／觸控操作、動畫、音效與同步 |
| `Client/package.json`、lockfile、`LcgWeb.csproj` | Phaser 4.2.1、TypeScript 5.9.3、esbuild 0.25.12 固定版本，前端自動納入 .NET 發布 |
| `wwwroot/battle/battle.css` | 手機、橫向、全螢幕、長文字、焦點及詳情圖布局 |
| `Components/Pages/Home.razor` | 原版僅在 `/legacy`，命令和 AI 已改用共同橋接／協調器 |

卡牌定義、預設／自訂牌組與卡圖沿用原檔。能量只傳實例 ID 與橫置狀態，電腦手牌只傳張數；牌庫不傳順序。檢視效果提供當次規則允許的卡片快照，並標明「檢視時」。舊文字日誌可能含 AI 私有檢視，因此新版只顯示公開結構化事件。

原版拖曳腳本僅供 `/legacy` 的回復用途；舊版重複的規則命令與 AI 驅動已移除。依計畫保留一個驗收週期後，才考慮移除原版呈現與 `battle-drag.js`；此時移除會破壞要求保留的回復入口。

## 規則、發布與完整對局證據

`dotnet test LcgTests`：94 項通過、0 失敗、0 略過。改造前本次基線為 82 項，新增 12 項涵蓋重複命令、錯局／過期／非法命令、資訊遮蔽、取消、事件次序、獨立快照、確認與呈現者、遺失確認逾時、AI 例外及合法揭露。見 `tests.log` 與 `LcgTests/BattleBridgeTests.cs`。

Release 發布成功。Phaser bundle 約 1.4 MiB，Brotli 約 300 KiB；正式發布排除 node_modules、TypeScript 原始碼與 sourcemap。靜態資產在本機發布目錄，不依賴浮動 CDN 或前端開發伺服器。見 `publish.log`。

五套預設牌組各自在發布產物的真實瀏覽器中完成玩家／AI 對局。以下為最後一輪，不代表平衡性或勝率研究；抽牌隨機，測試策略優先確認操作能完成。

| 預設牌組序號 | 結束回合 | 玩家操作數 | 結果          |
| ------ | ---- | ----- | ----------- |
| 1      | 26   | 54    | 本局敗北：生命降至 0 |
| 2      | 9    | 27    | 本局獲勝：生命降至 0 |
| 3      | 23   | 51    | 本局獲勝：生命降至 0 |
| 4      | 10   | 22    | 本局敗北：生命降至 0 |
| 5      | 14   | 28    | 本局敗北：生命降至 0 |

見 `browser-flow.mjs`、`browser-games.json`、`published-games.log` 與 `game-0` 至 `game-4-finished.png`。操作透過頁面按鈕／JS interop／C# 引擎；診斷介面只讀取狀態及座標，沒有直接呼叫規則或繞過命令契約。

觸控完整對局：390×844、5 點觸控模擬，以 Chrome CDP `Input.dispatchTouchEvent` 執行所有玩家操作，32 次操作、第 16 回合結束，包含填能量、出牌、攻擊、目標、抉擇及結束回合。見 `touch-game-results.json`、`touch-game-finished.png`。這是明確標示的模擬證據，沒有實體手機測試。

## 效能與版面

測試設備：Intel i7-11370H、8 個邏輯 CPU；Linux 7.0.0-34-generic；HeadlessChrome 154。軟體 WebGL 會造成額外 CPU 負載，因此偵測 SwiftShader／llvmpipe 等後改用 Phaser Canvas；硬體 WebGL 保留 AUTO 路徑。以下為 Canvas 實測；先前 WebGL 路徑也曾完成五套對局，但不宣稱已測實體 GPU 或所有裝置。

拖曳使用真實滑鼠 down/move/up；記錄原生 pointer 進入到場景更新後下一個 animation frame 的時間，排除 GPU 合成與螢幕掃描延遲。網路代理在 WebSocket 每個方向增加 75ms，實測 RTT：230.7, 152.7, 158.1, 152.5, 152.4ms。45 個回饋樣本 p95 = 5.8ms，小於 50ms；移動／取消期間送出 0 個遊戲伺服器命令。見 `drag-performance.json`、`drag-performance.mjs`。

舊版基線 p95 8.8ms 是本地 DOM PointerEvent 模擬到拖曳 ghost 插入，新版使用真實滑鼠到下一幀；方法不同，不能直接宣稱改善比例。確定的架構改變是移動與高亮完全留在瀏覽器，不再依賴 BeginDrag／HoverDrag 的伺服器往返。

每種版面記錄約 5 秒可見畫面的幀間隔；以下約 60 FPS。超過 25ms 的樣本另列，不以截圖代替效能量測。

| 版面        | viewport | 中位數    | p95    | >25ms 幀／樣本 |
| --------- | -------- | ------ | ------ | ---------- |
| desktop   | 1366×768 | 16.7ms | 16.7ms | 1/300      |
| mobile    | 390×844  | 16.7ms | 16.7ms | 0/301      |
| landscape | 844×390  | 16.7ms | 16.8ms | 0/301      |

各版面皆只有 1 個 canvas，橫向溢出 0px；手機詳情以 HTML 長文字與動作按鈕呈現。橫向旋轉會重算容器高度，再由 Phaser FIT 換算落點。見 `layout-performance.json`、`desktop-layout.png`、`mobile-layout.png`、`landscape-layout.png`。

## 計畫逐項驗收

| 計畫要求 | 完成證據／結果 |
| --- | --- |
| 0.1 本次規則基線與建置 | `baseline.json` 82 項；最後 `tests.log` 94 項與 Release 發布 |
| 0.2 拖曳、能量、召喚、攻擊、選擇、AI、重開、離開返回基線 | `baseline.json` 含原始 .NET 行程的實際走查與追加攻擊／返回／重開截圖；填能量確認屬選擇流程，複雜待選另有新版專項 |
| 0.3 現行指定攻擊、狀態、嘲諷與待選 | `BattleBridge.Monster/BuildSnapshot` 採引擎合法性；`drag-command-results.json` 嘲諷排除玩家直擊；`lab-results.json` 狀態與待選 |
| 0.4 固定版本及發布資產 | package.json／lockfile 與 BuildBattleClient；發布目錄完成對局 |
| 0.5 可回復基線與資料保護 | source.tar.gz、`data-before.sha256` 三個資料檔最後核對一致 |
| 1 獨立固定測試場景 | `/battle-lab` 使用獨立 Random(17) 引擎；沒有更動正式對局或牌組 |
| 1 本地拖曳、放大、箭頭、高亮、詳情與基本動畫 | `Client/battle/index.ts`；`drag-performance.json` 0 移動命令；`drag-command-results.json` 真實拖放填能量、召喚、攻擊成功 |
| 1 非法落點、Escape、pointercancel、縮放 | 非合法落點直接 redraw；`lab-results.json` Escape；`touch-results.json` touchCancel 不變更 revision／command；FIT 真實拖放與橫向版面證據 |
| 1 進出不累積實例／監聽／timer | `lifecycle-results.json` 三次退出 0 canvas／0 registry，返回 1；dispose abort 監聽、disconnect ResizeObserver、清 watchdog、取消 tween、銷毀 Game／參照 |
| 2 命令與一致性可見快照 | DTO + Bridge；同一引擎鎖內複製、無可變 Engine 參照；固定 player 身分與白名單 |
| 2 去重、過期、錯局、非法目標不重扣資源 | BattleBridgeTests；瀏覽器忙碌鎖與本地等待提示；動畫中重開使用目前版本並通過 lifecycle |
| 2 隱藏資訊不外洩 | JSON 序列化 DTO／事件測試、敵方抽牌與能量事件測試；公開 DTO 不含電腦牌面、牌庫順序或背面能量牌面 |
| 2 原版／新版共同協調器 | Home、BattlePage 都用同一 Bridge／Coordinator；同 circuit 切換維持 match；沒有兩套 AI loop |
| 3 法術、額外代價、抉擇、目標、檢索與排序 | `lab-results.json` 取消、檢索＋底排序、聖盾能量、沉默附著、犧牲死亡觸發＋生命代價、預付代價取消；全對局涉及更多既有選擇 |
| 3 預覽、禁用理由、嘲諷、所有狀態 | 由引擎提供 preview／problem／targets；顯示嘲諷、聖盾、冰凍、潛伏、沉默、劇毒、貫穿；canvas 與 HTML 同命令入口 |
| 3 有序事件動畫與連鎖死亡 | 規則具體節點 Present，不解析日誌或只比較快照；事件 ID／revision／order；事件次序測試和犧牲離場瀏覽器流程 |
| 3 五套完整真實對局與勝負 | `browser-games.json` 5/5 結束；手機模擬另完成 1 局 |
| 3 AI 加速、確認與遺失確認逾時 | Coordinator 控制每批；確認檢查呈現者／match／revision／batch；fake clock 測試遺失確認後重同步；加速及減少動畫可切換 |
| 3 背景分頁 | `background-results.json` 實際 hidden 分頁切換，返回同 match、revision 前進、busy=false |
| 3 動畫例外 | `animation-failure-results.json` 瀏覽器單次動畫拋錯，略過後校正，下一個正式填能量命令成功；測試只攔截該瀏覽器的模組，不修改正式 bundle／C# |
| 3 重開、離開返回與斷線 circuit 恢復 | `lifecycle-results.json`、`disconnect-results.json`；切斷／重啟 WebSocket 代理後同 match 同步，再重設成功 |
| 3 AI 例外 | 可控引擎例外單元測試驗證通知與重開回復；不將此列為實際伺服器故障事件 |
| 4 正式預設與設定回復 | appsettings phaser；`/legacy` 可用；`rollback-results.json` 最新發布產物設定 legacy 後首頁 302 導向原版；5169 重新啟動新版 |
| 4 音效、資源進度、缺圖及初始化失敗 | 音效開關＋AudioContext，`live-results.json` 記錄實際產生音訊節點（不宣稱實體喇叭測試）；載入卡圖張數／逾時；`fallback-results.json` 阻擋卡圖仍可操作，阻擋模組顯示回復入口且開始鈕停用；初始化有 8 秒上限與錯誤提示；`context-failure-results.json` 實際讓 WebGL／Canvas getContext 拋錯，顯示回復入口、開始鈕停用、0 canvas |
| 4 中文、手機、橫向與鍵盤 | layout／touch 證據；`lab-results.json` Enter 選牌、Escape、Enter 完成填能量確認；HTML 所有動作都有標準 button／選項入口 |
| 4 發布無開發服務／浮動 CDN、資料一致、清理 | 本機發布 DLL 完整對局；本機 bundle；資料 SHA256 一致；保留計畫要求的原版回復，其餘共用規則／AI 已清理 |

## 界限與後續

本次已完成對戰呈現改造。實體手機、Safari、實體硬體 WebGL 的效能尚未實測；觸控驗收依計畫允許的明確模擬方式完成。現有自訂牌組的無效牌組仍由原本規則停用，沒有擅自改寫資料。完整重新整理、服務重啟恢復對局與真人連線仍是原計畫排除的工作。

測試用代理、瀏覽器和隔離伺服器會在收尾時關閉，只保留 5169 正式試玩服務。驗證腳本與 JSON／截圖保留在 `驗證/Phaser改造/`，可重跑核對。
