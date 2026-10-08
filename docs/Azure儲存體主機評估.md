# Azure 儲存體主機評估

評估日期：2026-10-08。結論：目前的魂誓單人／人機 WebAssembly 靜態版，建議搬到 **Azure Blob Storage 靜態網站、日本東部、Standard StorageV2、Hot、LRS**。先使用 Azure 提供的 HTTPS 網址，不加 CDN、Front Door、資料庫或付費網域。

這是部署前的成本與相容性評估；尚未建立儲存體、註冊新服務、變更主機方案或搬站。

## 已確認的專案與帳號

- 本次讀取的公開輸出約 148.1 MB；完整下載清單 673 項、127,554,716 bytes，估算向上取為每次 128 MB。
- 規則、AI、組牌與本機天梯在瀏覽器執行。`SoulOath.Static/Program.cs` 只向主機讀取公開 JSON，玩家資料透過 `BrowserPlayerStorage` 寫入瀏覽器。現階段不需要持續執行的伺服器或雲端資料庫。
- 已有完整下載門檻、SHA-256 檢查與瀏覽器資源快取；更新時只補缺少或改變的檔案。換瀏覽器、清除快取或換網址會重新下載。
- Azure 訂用帳戶目前 Enabled，方案識別為 AzureForStudents，`spendingLimit=On`。本次沒有查證剩餘贈送點數，所以不把原贈送的 US$100 當作已確認餘額。
- 日本東部在目前訂用帳戶允許區域內。Microsoft.Storage 目前 NotRegistered，正式搬遷時需先註冊，資源建立仍以 Azure 實際回應為準。
- 原 F1 主機每日輸出流量只有 173,015,040 bytes（165 MiB），完整下載約 128 MB 已接近額度；它不適合多人首次下載。

## 日本東部價格與估算假設

透過微軟官方 Retail Prices API 於評估當日查詢 USD、Consumption：

| 項目 | 公開牌價 | 本次模型 |
| --- | ---: | --- |
| Hot LRS 檔案儲存 | US$0.020 / GB／月 | 0.15 GB |
| Hot 讀取操作 | US$0.004 / 10,000 次 | 每次完整下載約 700 次讀取 |
| Hot LRS 寫入操作 | US$0.050 / 10,000 次 | 每月 5,000 次，約四次完整更新 |
| 亞洲一般網際網路輸出流量 | 每月前 100 GB 免費，後續級距 US$0.12 / GB | 假設免費流量尚未被其他 Azure 用量消耗 |

靜態網站功能本身沒有額外主機月租。估算沒有扣掉可能適用的學生儲存體免費用量，也沒有依賴壓縮節省流量；它不是實際帳單或學生合約報價。稅額、其他 Azure 服務、額外版本、反覆下載與失敗重試可能增加費用。操作計費單位按用量比例估算。

每月成本約為：

`0.15 × 0.020 + 5000 / 10000 × 0.050 + 完整下載次數 × 700 / 10000 × 0.004 + max(完整下載次數 × 0.128 - 100, 0) × 0.12`

| 每月完整下載次數 | 輸出流量 | 月費估算（USD） |
| ---: | ---: | ---: |
| 10 | 1.28 GB | 約 US$0.03 |
| 100 | 12.8 GB | 約 US$0.06 |
| 500 | 64 GB | 約 US$0.17 |
| 1,000 | 128 GB | 約 US$3.70 |
| 5,000 | 640 GB | 約 US$66.20 |

「完整下載次數」不是每次開遊戲或玩家總人數。100 個人各下載一次就是 100 次；同一人換裝置或清除快取再下載也會多算。已有快取的人一般只下載更新內容與少量啟動頁面。100 GB 大約容納 780 次這種完整下載，更新與其他用量也會占用流量。

若其他 Azure 用量已用掉整份免費流量，上表應另外增加最多 US$12／月的流量費。朋友試玩約 100～500 次完整下載／月，在上述免費流量假設下，建議先抓 **US$1／月** 作保守規劃；每月 1,000 次可抓 **US$5／月**。若剩餘學生點數足夠且仍在效期內，這些 Azure 使用費可從點數扣除；其他服務用量同樣會消耗點數。

## 與其他方式比較

| 方式 | 目前適用性 | 日本東部成本概念 |
| --- | --- | --- |
| 原 Windows App Service F1 | 很少量測試；流量已造成停站 | 主機免費，但每日流量不足 |
| Blob Storage 靜態網站 | 適合現行人機、組牌、本機天梯與大量卡圖 | 按儲存、操作、流量付費，無固定主機月租 |
| Windows App Service B1 | 可保留原主機配置，但純靜態檔案用途成本高 | US$0.0885／小時，按 730 小時約 US$64.61／月，另計相關費用 |
| Linux App Service B1 | 比 Windows 低，但需要另一套主機部署與區域／配額確認 | US$0.019／小時，按 730 小時約 US$13.87／月，另計相關費用 |

目前沒有購買持續運行主機的必要。學生點數若用在 Windows B1，僅固定主機費就會較快用完。

## 搬遷需要處理的內容

1. **完整網站一起搬到新網域。** 卡圖、音效、資料與程式維持同來源，完整下載與 Service Worker 可沿用；不要只把圖片跨網域搬出去，造成額外跨站載入問題。
2. **頁面入口與重新整理。** Blob 不執行 IIS 的 `web.config` 或 Static Web Apps 設定。根首頁、`/cards`、`/deckbuilder`、`/ranked`、`/settings` 等需建立實際頁面入口／對應 Blob，或採微軟 Blazor 的錯誤頁回退配置。正式驗收要確認直接開網址與重新整理都能進入，以及缺少的資源仍不會被當作成功下載。
3. **檔案類型與更新。** 上傳時明確設定 JS、wasm、webp、ogg、JSON 的 Content-Type。首頁、啟動模組、資源清單與工作程序需能檢查更新；資源先上傳、入口最後發布，驗證雜湊與版本綁定。可在後續利用 Content-Encoding 壓縮程式傳輸，上表先以未節省的大小計算。
4. **存檔搬移。** 新網址不會自動取得舊網址的瀏覽器存檔。從原網址設定匯出、到新網址匯入。若舊 Azure 網站仍因額度停用，需要使用還開著的原分頁或等額度恢復；保留本地 5180、舊網址資料與既有備份。不要把任何玩家存檔上傳到公開儲存體。
5. **部署驗收。** 模擬慢速首次完整下載、下載中斷接續、199 張卡圖全數載入、快取重開、更新不重下載未改圖片，以及一場人機對戰與牌組保存。評估不能取代正式搬遷後的線上驗證。

Azure 提供的預設網址可使用 HTTPS。日後若要自訂網域且保留 HTTPS，需要另評估前端服務與費用。Blob 靜態網站提供公開唯讀檔案，沒有網站登入控制，也不提供真人配對、帳號登入、雲端存檔或共同天梯後端；這些新功能需要另外設計。

## 成本控制與使用者操作

目前學生訂用帳戶的支出限制開啟，應保留。建議另設每月 US$5 的成本提醒（帳號支援 Cost Management 後），但提醒不會自動停止下載或封頂費用；它也有計費資料延遲。真正的學生贈送額度支出限制與成本提醒不同。

我可以處理建置、公開資源打包、Content-Type、頁面入口、上傳工具、檢查與試玩驗證。需要使用者處理的項目是任何新增登入／MFA，以及新網址存檔的匯入確認。正式建立按量計費資源與搬遷，需要使用者同意本評估的方案和預算；本次只做評估。

## 來源與證據

- [Azure Storage 靜態網站與計費](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blob-static-website)
- [微軟 Blazor WebAssembly 的 Azure Storage 部署方式](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly/azure-storage?view=aspnetcore-10.0)
- [官方公開牌價 API 說明](https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices)，API：https://prices.azure.com/api/retail/prices
- Blob 查詢：`serviceName eq 'Storage' and armRegionName eq 'japaneast' and productName eq 'General Block Blob v2' and skuName eq 'Hot LRS' and priceType eq 'Consumption'`，幣別 USD。
- [Azure 流量牌價與每月 100 GB 免費流量](https://azure.microsoft.com/en-us/pricing/details/bandwidth/)
- [學生帳戶與贈送點數](https://learn.microsoft.com/en-us/azure/education-hub/faq)
- [支出限制](https://learn.microsoft.com/en-us/azure/cost-management-billing/manage/spending-limit)、[成本提醒不會停止用量](https://learn.microsoft.com/en-us/azure/cost-management-billing/costs/tutorial-acm-create-budgets)
- [Blob 的 HTTP 屬性](https://learn.microsoft.com/en-us/rest/api/storageservices/set-blob-properties)
- 忽略的 `output/azure-hosting-retail-prices.json` 與 `output/azure-hosting-selected-prices.json` 保存本次牌價回應；`output/azure-storage-policy-review.json`、`output/azure-student-spending-policy.json` 保存本次唯讀設定檢查，沒有登入憑證。這些輸出不提交 Git。
