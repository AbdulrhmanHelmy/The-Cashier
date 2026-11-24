# Stage 6 purchasing QA

November 21, 2026. Browser results use Microsoft Edge on Windows. The six-combination matrix serves the actual repository frontend with synthetic API responses to reproduce pending requests, failures and interrupted posting. A separate browser smoke test used the running loopback OWIN host and a freshly migrated disposable SQLite database. Financial and inventory mutations were limited to that database and the existing isolated integration fixtures.

## Release build

`dotnet build src/AlKhayal.UI/AlKhayal.UI.csproj -c Release --no-restore -m:1` passed with zero warnings and zero errors.

## Unit and integration tests

| Check | Result |
| --- | --- |
| `dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj -c Release --no-restore -m:1` | 327 passed, 0 failed, 0 skipped. |
| `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj -c Release --no-build -m:1 --filter 'FullyQualifiedName~Purchase\|FullyQualifiedName~Supplier\|FullyQualifiedName~OperationalReport\|FullyQualifiedName~TransactionReport\|FullyQualifiedName~ExportPersistence'` | 79 passed, 0 failed, 0 skipped. |
| `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj -c Release --no-restore -m:1` | 494 passed, 0 failed, 0 skipped. |

## Frontend tests

`node frontend/tests/run-tests.js` passed, including purchasing, purchase invoice return badges, supplier accounts, warehouse, reports, returns navigation, printing transport, permissions, workspace tabs and exports. Edge runners `stage2-browser.cjs` through `stage5-browser.cjs` passed. Stage 2 included dirty-purchase tab closure, cancel/close behavior, theme and broader workspace checks. Stage 3 included catalog and barcode regressions, Stage 4 included shared customer workflows, and Stage 5 included supplier selection, accounts and reports.

## Browser QA

| Workflow | Evidence |
| --- | --- |
| Composition | Supplier lookup by phone with a leading zero, an Arabic supplier name, missing matches, replacement and keyboard activation. Product selection, delayed product load and atomic UI submission gating. Warehouse changed and the selected ID was sent. RTL and long names/reference exercised. Existing executable purchasing tests add rapid alternate searches, pagination/retry, duplicates, unit/account selection and expiry/serial restoration. |
| Draft and navigation | Save rejection retained entered notes. Save uses the confirmed response for line totals. Empty notes remained empty after a subsequent save. Browser coverage includes history-to-draft navigation, long draft, dirty close cancellation, numeric-preview clearing and removal focus. Live smoke confirmed persisted draft reopen and update. |
| Posting and goods receipt | Persistent progress and uncertain feedback, manual retry with the same operation key, retained tab during posting and exact posted invoice link. The live run committed a credit purchase in disposable SQLite. Existing integration tests cover real accounting, stock and posting behavior. |
| History, details and returns | Populated history and exact invoice routing. The live history showed the updated posted row after returning from details and opened the return entry point for that invoice. No live return was posted in the browser smoke. |
| Report | Posted row, posting-time basis, date/filter controls, an empty search result and invoice navigation. A live report showed the newly posted test invoice. |
| Permissions | Restricted fixture with purchase history and reporting access verifies read-only draft details and absent Create and Edit actions. Stage 2 also exercises existing route and feature filtering. |
| Print integration | The browser confirmed that the posted invoice print-job request contained the correct purchase type and ID and surfaced an injected printer error. The live Printing Settings designer found the persisted purchase by reference, showed the matching supplier, rendered the actual saved receipt and closed with Escape. Existing tests cover preview handling and stale responses. |
| Theme and viewport | Light and Charcoal Dark at 1024×768, 1366×768 and 1600×1000. Browser assertions check RTL and document-level horizontal overflow. The existing local table overflow and sticky action area were exercised at 1024×768. |

Run the matrix with `node frontend/tests/stage6-browser.cjs`. Set `THECASHIER_QA_PLAYWRIGHT` to the installed Playwright Core package path if it is outside Node's normal resolution. The optional baseline uses `THECASHIER_QA_STAGE6_BASELINE` with pre-edit `purchasing.js` and `transaction-reports.js` copies.

## Screenshot evidence

The 84 before, 98 after and six live captures are in [artifacts/purchases-stage6](../artifacts/purchases-stage6/). They use synthetic business records. The screenshots from the original audit ZIP were reviewed as historical evidence.

- [Supplier replacement before](../artifacts/purchases-stage6/before-1366x768-dark-supplier-replacement.png) and [after](../artifacts/purchases-stage6/after-1366x768-dark-supplier-replacement.png)
- [Pending selection before](../artifacts/purchases-stage6/before-1366x768-light-loading-product.png) and [after](../artifacts/purchases-stage6/after-1366x768-light-loading-product.png)
- [Saved draft before](../artifacts/purchases-stage6/before-1600x1000-light-saved.png) and [after](../artifacts/purchases-stage6/after-1600x1000-light-saved.png)
- [Interrupted post](../artifacts/purchases-stage6/after-1366x768-dark-uncertain.png) and [report](../artifacts/purchases-stage6/after-1366x768-light-report.png)
- Live [draft](../artifacts/purchases-stage6/live-draft.png), [posted detail](../artifacts/purchases-stage6/live-posted.png), [report](../artifacts/purchases-stage6/live-report.png), [return entry](../artifacts/purchases-stage6/live-return-entry.png) and saved receipt preview [before](../artifacts/purchases-stage6/live-receipt-preview-before.png) and [after](../artifacts/purchases-stage6/live-receipt-preview.png)

## Limitations

Native WinForms/WebView2, legacy Chromium 109 runtime qualification, Windows DPI and scanner hardware were outside the executed browser runs. No physical printer or native export dialog was used. The restricted-user matrix covered route visibility and read-only draft affordances. The focused and full automated suites provide additional endpoint and persistence coverage. Individual numeric edge cases and unavailable historical catalog rows were not all exercised in a live browser session. See the [defect register](purchases-stage6-defect-register.md) for the blocked unsaved-line amount.

## FINANCIAL, ACCOUNTING, INVENTORY AND DATABASE SAFETY

The Stage 6 production diff was compared with exact pre-edit copies of the four JavaScript files. The full working-tree status and diff were also reviewed against the starting baseline, which already had unrelated desktop, host, frontend and test modifications. The starting [Git status](../artifacts/purchases-stage6/git-status-before.txt) is retained with the evidence. The four production files contain renderer and UI state changes. The browser test, purchasing regression and QA documentation were reviewed separately.

The review confirmed no Stage 6 changes to accounting, purchase calculations or line math, supplier balances, supplier payments, posting and ledger transactions, financial report algorithms, costs or profits, stock receipt or inventory posting, warehouse balances, unit conversions, schema, migrations, financial SQL, purchase persistence, business validation rules, authorization or licensing. The report continues to use the saved backend fields. The client still sends the original purchase payload through the same routes and retains the existing operation-key generator. No commit, push or deployment was made.

## Final closure pass

November 21, 2026. **PARTIAL** is the final acceptance status. The Windows 11 application started and launched the installed WebView2 runtime. The requested authenticated Purchasing workflow inside that native window remains a required acceptance check.

### Native Windows and WebView2

- **Startup passed.** The actual Release `TheCashier.UI.exe` opened its WinForms window on Windows 11 Pro build 26300 and started its own `msedgewebview2.exe` (157.0.4341.60). The launch used a fresh WebView2 user-data directory and an isolated loopback OWIN host with a freshly migrated temporary SQLite database. See the [native startup probe](../artifacts/purchases-stage6/closure-native-startup.txt).
- **Purchasing workflows NOT TESTED in native WebView2.** The automatic approval review rejected the launch command containing a WebView2 debugging port. The ordinary launch succeeded. The Windows computer-use helper then returned “window id … no longer belongs to … current owner is …” on both the initial bind and a fresh-window retry, showing the same owner string in both positions. This prevented inspection and interaction inside the native window. Native Purchasing console, focus, RTL, theme, loading, draft, posting, report and preview checks remain open.
- **Legacy Windows NOT TESTED.** The repository targets Windows 7 SP1 through Windows 11 with runtime caveats, including a separately packaged Chromium 109-compatible WebView2 runtime for Windows 7. This session had Windows 11 and current WebView2. VirtualBox listed one powered-off Windows 7 guest with cached SP1 metadata. An isolated Purchasing setup and Chromium 109-compatible runtime were not established for that guest, so it was left powered off.

### Purchase-line amounts

The product-detail lookup returns catalog data and unit pricing. It does not return an authoritative invoice-line total for the current edit. Purchase create/update runs `PurchaseService` validation, which uses `MoneyPolicy.FinalizeQuantityAmount` and stores `LineTotalScaled`. Saved invoice retrieval formats the stored value as `LineTotalFormatted`. The editor shows a dash on unsaved or financially edited lines and renders returned values after saving or reopening. The existing invoice-level preview remains separate. An authoritative pre-save per-line amount requires separate business/API authorization. The closure made no calculation, display or contract change for this limitation.

### Receipt preview

The owner reports previously verifying physical printing. This closure opened the saved-document designer in Edge against the isolated host, searched by the posted test invoice reference, verified the selected supplier and invoice identity, rendered the actual receipt preview and dismissed it with Escape. Native printer, driver and spooler qualification remain outside this run. No print job was sent. See the [closure receipt preview](../artifacts/purchases-stage6/closure-edge-receipt-preview.png).

### Closure Git review

The closure [starting status](../artifacts/purchases-stage6/closure-git-status-before.txt) and [final status](../artifacts/purchases-stage6/closure-git-status-after.txt) match. `git diff --stat` was reviewed and `git diff --check` passed. The existing dirty worktree includes earlier desktop and host changes. This closure modified only this QA report and the defect register, plus local ignored evidence. No new Purchasing UI defect was established. The implementation report was retained as-is. No changes were made during closure to accounting, costs, taxes, supplier or warehouse balances, financial validation, purchase or inventory posting, SQL, schema, persistence, authentication, authorization, licensing, or printing integrations. No commit, push, deployment, or installer packaging was performed.

### Closure regression results

| Check | Result |
| --- | --- |
| Desktop Release build | Passed, 0 warnings and 0 errors. |
| Complete unit suite | 327 passed, 0 failed, 0 skipped. |
| Complete integration suite | 494 passed, 0 failed, 0 skipped in 8 min 45 sec. |
| Focused purchase, supplier, report and export integration suite | 79 passed, 0 failed, 0 skipped. |
| Frontend regression runner | Passed, including purchasing, supplier, inventory, printing and workspace checks. |
| Stage 2 workspace navigation and dirty-state Edge runner | Passed, including purchase draft close/cancel. No page errors. |
| Stage 3 catalog/inventory-adjacent Edge runner | Initial parallel run failed at `frontend/tests/stage3-browser.cjs:149`. It expected `35 × 25` while the batch settings still displayed `جارٍ تحميل الإعدادات…`. Standalone rerun passed. Test and production files were unchanged. |
| Stage 5 supplier Edge runner | Passed across six viewport and theme combinations. |
| Stage 6 purchasing Edge runner | Passed across six viewport and theme combinations, including delayed selection and uncertain posting behavior. |
| Live Edge with isolated OWIN/SQLite | Passed two-line draft save/reopen/update, editable quantities/prices, saved line amount, posting, history and return navigation, report and receipt preview. See the [draft](../artifacts/purchases-stage6/closure-edge-draft.png), [posted invoice](../artifacts/purchases-stage6/closure-edge-posted.png), [report](../artifacts/purchases-stage6/closure-edge-report.png), and [return entry](../artifacts/purchases-stage6/closure-edge-return-entry.png). |
