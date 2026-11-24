# Testing and verification

Cash change calculator scenarios run in `node frontend/tests/run-tests.js`. The Settings API integration test checks the default, an explicit disable, an older PUT that omits the field, and persistence after restart. Run `node frontend/tests/cash-change-browser.cjs` with Playwright Core and Edge to check the production frontend with fixture API responses. The browser test verifies card order and spacing in Arabic RTL, horizontal and stacked layouts, Light and Dark colors, and keyboard focus and dismissal. Set `THECASHIER_QA_SCREENSHOTS` to an existing directory to save captures.

Supplier UI regression coverage in `frontend/tests/supplier-accounts-tests.js` exercises the single searchable selector, supplier switching and stale-response protection, authoritative statement and invoice loading, the existing invoice/on-account payment actions, and supplier navigation metadata. It does not create real financial records or visually qualify WebView2 geometry; manual acceptance remains required at 1366×768 in Light and Dark themes.

`EstimatedProfitTests` covers persisted actual/estimated splits, purchase-cost selection across warehouses, configured fallback, unknown cost, repeated partial returns, cancellation, price-change immutability, and general/product profit reconciliation. Existing `SalesStockFloorTests` continues to check physical stock projection and legacy incomplete-cost behavior. See [sale cost and profit scope](estimated-sale-profit.md).

Sale-price permission coverage uses the access-state tests for cashier default-deny and administrator access. Sales API writes enforce `manualPriceOverride` for draft create/update, installment creation and posted invoice corrections. SQLite-backed integration coverage should verify permission persistence and a missing grant.

Migration 092 return regressions in `TouchPosInvoiceEditingTests` use disposable SQLite files for installment returns after a down payment and collection, latest-first partial allocation, repeated/overdue/full/fully-paid returns, excess cash refunds, original FIFO restoration, transaction rollback, card/wallet pending obligations, manual external confirmation, duplicate confirmation, repeated electronic partials, and corrected card invoices. `frontend/tests/returns-tests.js` covers the pending confirmation form and double-submit guard. Run the full solution, frontend, syntax, Release build and diff checks after these focused tests.

Returns regression: `TouchPosInvoiceEditingTests` now uses disposable SQLite files to assert that a saved and edited draft returns only its final posted line IDs, quantities and manually overridden price; root-number lookup resolves the latest posted correction; old invoice IDs cannot be returned; saved return lines reference the new revision; and a sale posted beyond available stock restores only its actual FIFO-issued stock with no fabricated layer. The tests also check same-key replay, inventory projection, customer balance, and foreign keys. `frontend/tests/returns-tests.js` checks that the lookup renders historical unit price and final line value. Existing return, purchase, FIFO, treasury, installment, API and report tests remain part of the full solution suite.

Atomic installment regressions use disposable SQLite databases in `SalesPersistenceTests` and an authenticated loopback host in `CustomerInstallmentApiTests`. They cover carton conversion, separate unit sale price, FIFO consumption, the per-product stock floor, one linked invoice/contract/debit/down payment, partial and full collection, replay, overpayment, schedule/down-payment rollback, and the closed legacy write endpoint. `frontend/tests/installment-sales-tests.js` drives the actual installment renderer with a deterministic DOM to check server product search, unit price, quantity conversion preview, atomic payload, and double-submit prevention. Run these with the normal integration and frontend suites; no operational database or native browser session is used.

Touch POS saved-draft interaction coverage in `frontend/tests/touch-pos-invoice-tests.js` exercises focus-based edit intent on quantity, unit price, invoice discount, payment, and customer controls. The shared guard keeps those fields unchanged until the existing three-way confirmation is accepted, then restores focus in the editable draft. Product, cart, barcode, same-ID save/post, and restricted-invoice checks remain in the same suite.

`ExportWriterTests` parses XLSX ZIP/XML and CSV output for RTL/frozen/filter structure, numeric/text cells, Arabic/BOM/escaping/formula safety, leading zeros, empty headers, definitions, and invalid requests. `ExportPersistenceTests` uses disposable migrated SQLite databases for projections, filters, 15,000 unpaged products, stable order, read-only behavior, and authenticated HTTP validation. `frontend/tests/export-tests.js` verifies all six real toolbar integrations and the shared cancel/loading/save/duplicate-submit flow.

Offline backup coverage uses disposable SQLite databases and a loopback host. Run the targeted suite with dotnet test tests/AlKhayal.IntegrationTests --filter FullyQualifiedName~BusinessBackupTests and run node frontend/tests/run-tests.js for Settings renderer integration. The frontend test drives the real manual-backup and restore-confirmation handlers through pending, success, failure, and rapid-click states, including the blocking loading modal and session reset. See [offline backup and restore](offline-backup.md) for compatibility and deployment checks.

`WebViewKeyboardPolicyTests` covers the desktop-only refresh accelerator filter, including F5/Ctrl+R and representative copy, paste, navigation, developer-key, and application-key pass-through cases. The executable frontend product-unit regression continues to exercise the Touch POS multi-unit `contextmenu`, long-press wiring, unit choice, and ordinary product click behavior independently of the WebView2 browser-menu setting.

## FR-002 customer collection treasury regression

`CustomerStatementTests` uses disposable SQLite accounts to check full/partial/repeated collection, selected destination, missing/inactive/incompatible accounts, ledger and treasury reconciliation, shift links, same-key retry/conflict, concurrent duplicates, write-failure rollback, legacy customer payments, and unchanged historical collections. `SalesTreasuryTests` checks invoice paid/outstanding/customer debt, one linked deposit, the same shift event, replay, and shift close without another deposit. Existing installment, maintenance, report, API and frontend suites exercise the destination fields. Run the full integration, unit, and frontend suites before considering the finding closed.

2026-11-03 FR-004 verification before FR-002 changes, after additive migration `085_zero_value_sales_returns.sql`: targeted zero-value return/migration tests 8/8; related return, report, treasury and migration tests 34/34; full integration suite 418/418; full unit suite 248/248; `node frontend/tests/run-tests.js` passed for 131 registered screens; solution build had zero warnings and errors. These counts are historical; the FR-002 final campaign is recorded in the financial reliability audit.

The 2026-11-03 financial audit adds disposable-SQLite regressions for two/four partial purchase returns, original-line credit residuals, supplier payable/ledger and stock movement reconciliation, a cash credit-invoice collection with no treasury movement, and a three-line sales tax/discount residual edge case. It also supplies read-only historical discrepancy SQL. The full integration suite takes about six minutes because it includes a 30,000-sale report stress case; run it sequentially and wait for its TRX result. Exact executed counts and remaining limits are in [the financial reliability audit](qa/financial-reliability-audit.md).

The frontend registry suite includes `customer-language-tests.js`, which rejects literal `FIFO` in customer-facing frontend assets and Arabic backend message strings. Internal identifiers, migration names, technical documentation, developer logs, and test fixtures remain outside this language check. Inventory costing and report completeness behavior are unchanged.

`ManualWeightTests` uses disposable SQLite files to cover default piece behavior, unchanged Units records, weighted creation/edit/restart, purchase posting and return, fractional adjustment/transfer/count, 250 g/500 g/1.250 kg sales and totals, FIFO balances, partial and short-stock sales returns, cancellation, posted correction, receipt quantities, stock and product report units, and idempotent posting. `frontend/tests/manual-weight-tests.js` checks exact gram conversion, invalid input, piece integer validation, Touch POS entry, immediate total, and cart weight editing.

`StartupRecoveryStateTests` covers failure presentation state, single-use retry, close
cancellation, and successful recovery. Native WebView2/runtime and manual startup GUI checks
remain deployment validation items.

Retired product sizes/colors are covered by `CatalogRetiredAttributeTests` using disposable temporary databases only: retired API access/assignment rejection, contract removal, legacy combination/stock/FIFO preservation, tracking retention, rollback and read-only projection of old label JSON. Generic variant tests now use custom attributes. See [scope and compatibility assumptions](retired-product-attributes.md).

`SalesStockFloorTests` uses disposable SQLite databases for stock 0/sell 5, stock 3/sell 5, stock 10/sell 4, actual FIFO allocations, financial returns with zero and partial stock restoration, inventory projection reconciliation, and incomplete net-profit cost evidence. `SalesPersistenceTests` also covers an oversell, a zero-consumption retry, and two concurrent sales competing for the last unit.

Tracked stock-floor coverage runs zero-stock and short-stock sale/return cases for lots, expiry dates, serials, and IMEIs. It checks that only real stock, cost, and physical identities are deducted or restored, rejects an unregistered serial when stock is deducted, verifies no batch is created for a wholly uncovered lot, and covers zero-consumption serialized cancellation. The frontend pricing/FIFO suite covers entry of an unstocked lot and a serialized sale without fabricated identifiers.

Pre-Phase 9 adds `PricingFifoInvariantTests`, `SalesPricingTests` and `frontend/tests/pricing-fifo-tests.js` (included in the normal frontend suite). They cover current prices on old stock, exact selected-unit conversion/variant precedence, immutable sale/COGS facts, the approved two-layer example, inclusive/exclusive tax with discounts, partial returns, cancellation, new purchases, warehouse transfers, retry/restart and potential versus realized margin. See [audit, commands, actual results and limitations](phase-9/pre-phase-9-pricing-fifo.md).

Refund overdraft tests cover negative post-balances, ordinary-withdrawal protection, partial replenishment, replay/rollback, debt-first settlement, exact cash residuals and populated pre-031 migration preservation/failure rollback. See [sales-refund-overdraft.md](sales-refund-overdraft.md).

The returns initial-load regression now has real HTTP coverage for omitted/empty/whitespace search and cleared search after posting, plus frontend coverage for opening the visible Details page directly, selecting a saved return and searching/clearing its picker. The focused `Returns_only_cashier` HTTP test and full frontend suite passed on 2026-10-28. The test reproduced the original `?search=` 400 before the boundary-binding fix. See [returns-management.md](returns-management.md).

Unified return tests cover real lookup/list/detail, both cashbox effects, sales-return cashbox isolation, atomic stock/account/treasury rollback, retry identity, CSRF/screen grants, shell dispatch and inherited detail navigation. See [returns-management.md](returns-management.md) for commands and compatibility assumptions.

`SalesTreasuryTests` and `SalesTreasuryApiTests` cover exact cash-sale deposits, account reuse/creation, restart/retry/concurrency, overflow/rollback and POS-only cashier authorization. Frontend handlers verify destination and receipt messaging. See [sales-shift-revenue.md](sales-shift-revenue.md).

Expense persistence/concurrency/rollback tests, real HTTP permission/CSRF checks and executable frontend handlers verify types, exact amounts, selected treasury effects and retry identity. All 184 integration + 50 unit tests and frontend checks passed on 2026-10-28. Commands and runtime limits: [expenses.md](expenses.md).

## Purchase product selection

`CustomerStatementTests`, `CustomerStatementApiTests` and `frontend/tests/customer-statement-tests.js` cover real statement loading, exact customer-row navigation, existing-tab reopening, rapid customer switching and stale-response rejection, current versus filtered debt, all-time double-count regression, persisted increases/collections, overcollection/overflow/rollback, request identity, notes, role/CSRF checks, mounted filters and the hidden former credit-payment route. See [customer-statement.md](customer-statement.md).

`CustomerInstallmentTests` and `CustomerInstallmentApiTests` cover migration-backed contract/schedule/payment persistence, exact cent allocation, operation-key replay/conflict, duplicate settlement prevention, atomic rollback, product-price verification, session/CSRF/screen authorization, and customer statement debit/down-payment/payment movements. The frontend registry suite checks that installment sale and settlement use the persisted customer installment endpoints. See [installments.md](installments.md).

Maintenance frontend registry checks require the two requested routes under one visible module and ensure intake, advance, customer filtering, status changes and cash/credit delivery use the persisted API. `MaintenancePersistenceTests` covers SQLite-backed ticket, history, advance, cash/credit statement projection and rollback/idempotency behavior.

`SupplierPaymentTreasuryTests`, `SupplierPaymentAllocationTests`, supplier HTTP tests and the supplier frontend handler suite verify atomic source withdrawal, source authorization, exact replay, funds/late-failure rollback, concurrent funds access, due-date/creation priority, partial/mixed-source allocations and refreshed invoice paid/remaining. All mutation tests use disposable SQLite files. See [supplier-payment-treasury.md](supplier-payment-treasury.md) for results and the running-desktop file-lock build workaround.

Supplier Accounts adds `SupplierAccountsTests`, `SupplierAccountsApiTests` and executable `frontend/tests/supplier-accounts-tests.js`. Coverage includes new debt invoice atomicity/replay/credit offset, source invoice ID collisions, return-adjusted payment caps, HTTP CSRF and decimal binding, grouped invoice rendering, unchanged supplier directory, two sidebar destinations, hidden aliases, mounted search and lost-response/stale-request handling. See [supplier-accounts.md](supplier-accounts.md).

Cash purchase integration adds disposable SQLite coverage in `PurchasingTreasuryTests` for selected cash/bank persistence, exact atomic withdrawal with inventory/FIFO/supplier settlement, concurrent replay, insufficient funds and late-failure rollback. `TreasuryApiTests` checks purchase-only cashier authorization and the real cash save/post/replay HTTP flow. The purchasing frontend handler suite verifies required account selection, submitted ID, credit clearing, busy-state release and saved-selection guards. See [purchase-treasury.md](purchase-treasury.md).

The purchase-product visibility regression is included as `frontend/tests/purchasing-tests.js` in the normal frontend command. It executes the actual Purchase Management renderer to verify default first-page loading without typing, single-character and cleared searches, pagination, mounted search input, inactive/archived exclusion, detail selection, saved-line variant/unit/price payload, retry and stale-response rejection. `node --check frontend/js/modules/purchasing.js`, `node frontend/tests/run-tests.js`, and `dotnet build AlKhayal.slnx --no-restore -m:1` passed on 2026-10-28. Native WebView2 theme/layout verification remains separate; no operator data was changed.

Page freshness tests in `frontend/tests/page-snapshot-tests.js` verify that purchase data changes remain detectable, treasury balance changes do not stale credit-mode Purchase Management, and balance-only changes do not stale customer/workforce views that use account names and types. They also verify that safe Arabic conflict reasons remain visible instead of being replaced by a generic message. Purchasing tests cover removing/reinstating the account snapshot as the user switches between credit and cash. Supplier account tests verify modal-only treasury reads do not stale the underlying page. Run with `node frontend/tests/run-tests.js`.

## Treasury and bank persistence

`TreasuryPersistenceTests`, `TreasuryApiTests` and the automatically included `frontend/tests/treasury-tests.js` cover real cash/bank CRUD, migration/restart, exact money, idempotent deposits/withdrawals, overdraw/overflow rejection, concurrent funds access, rollback, immutable audit, session/CSRF/screen permissions, mounted search, retry identity and stale responses. They use disposable SQLite files. See [treasury.md](treasury.md) for commands, environment prerequisites and unexecuted native/Windows 7 checks.

## Financial precision and first-run regressions

`TreasuryLegacyMigrationTests` adds realistic recorded-019 treasury upgrade fixtures, including populated bank/cash/transfer histories, preserved source details/audit, inactive account retention, repeat startup and full rollback for incompatible historical precision, negative balances or duplicate normalized codes. A read-only SQLite backup of the affected local database was also migrated twice successfully, without changing the original operator database.

`MoneyPolicyTests` covers 0.01, 0.10, 0.99, a large signed-64-bit-safe amount, zero, prohibited negatives, excess precision, exact two-decimal formatting, fractional quantity multiplication, and positive/negative half-piastre ties. FIFO tests continue to inspect underlying layer/allocation integers, repeated partial withdrawals, cross-layer consumption, final residual allocation, transfers, rollback, restart, and idempotent replay.

`SalesPricingTests`, `SalesPersistenceTests.Fractional_pack_rate_posts_exact_total_and_partial_returns_reconcile`, and `frontend/tests/pricing-fifo-tests.js` cover the 100 EGP / 3 bottle tier: a displayed 33.33 rate, a posted 100.00 line total, one-rounding line calculations, stock deduction, and partial returns that sum to the original sale. `Historical_carton_return_uses_posted_twelve_unit_snapshot_after_catalog_ratio_changes` verifies return stock uses the saved 12-unit conversion even if the current catalog row later says 24. Legacy migration fixtures seed the schema age they represent; current catalog writes are not replayed into a schema predating bundle support.

`FirstRunBootstrapTests` creates a disposable migrated SQLite database for zero-user creation, successful login after a new service instance, Admin/cashier/inactive-user exclusion, concurrent creation, no partial row after validation failure, the single-character/numeric-only/symbol-only password policy, and the real OWIN API cookie flow.

The realistic financial dataset test uses real catalog, customer, supplier, purchasing, inventory, FIFO, return, payment, transfer, and stock-count services against one disposable SQLite file. It compares invoice, supplier-ledger, supplier projection, customer projection, purchase-return account value, FIFO value, and explicit variance using underlying database integers, reruns migrations, verifies foreign keys, and deletes the test file afterward.

Catalog coverage adds fresh/repeated 001–009 migration, a representative legacy upgrade with ID/value preservation and unknown purchase cost, exact scale-4 sale/reference-cost rounding, negative-cost rejection, invalid legacy sale-price/name quarantine, post-migration reconciliation, foreign-key/integrity checks, category create/edit/duplicate/cycle behavior, protected default unit behavior, invalid references, idempotent create, search/filter, optimistic update conflicts, activation/archive and purchase-cost restart persistence, authenticated API/CSRF/role/screen-permission checks, renderer resolution, contextual product/category/unit actions, inline draft-preserving category creation, two-entry inventory sidebar, hidden-route compatibility, real Warehouse Directory, stale-response suppression, retry states, and explicit non-operational future fields.

Settings coverage includes service validation and concurrent-read caching, real SQLite persistence/restart/audit/rollback, fresh and repeated migration, authenticated reads, administrator/CSRF mutations, cashier rejection, invalid payloads, database-information leakage checks, executable renderer resolution, formatter integration, stale-response handling, and deduplicated frontend loads. Native acceptance must distinguish unauthenticated startup/theme checks from authenticated workflow checks; the latter remain manual if credentials cannot be entered by the available automation.

Run from the repository root:

The shared page header refresh action reruns the active registered renderer, restores search text and reloads matching results. On tab activation the shell reissues the active page's last GET for each API resource and shows a notice only when a response changed.

```powershell
dotnet build AlKhayal.slnx --no-restore
dotnet test AlKhayal.slnx --no-restore
node frontend/tests/run-tests.js
Get-ChildItem frontend/js -Recurse -Filter *.js | ForEach-Object { node --check $_.FullName }
```

The .NET unit suite covers authentication, activity recommendations, feature defaults, core protection, unknown keys, dependency validation, administrator overrides, activity removal, and the three-state product applicability contract. Integration tests use isolated temporary SQLite files, repeat migrations, exercise real OWIN endpoints, verify cookie/CSRF/role behavior, repeat effective-state reads and module/capability toggles, preserve administrator overrides when applying recommendations, persist feature state across restart, verify audit writes and transaction rollback, and clean up only their own files. User-management coverage adds hash preservation, case-insensitive username uniqueness, Admin/Cashier creation, invalid role/screen rejection, administrator-only screen rejection, independent permission persistence, last-active-administrator protection, rollback on audit failure, audit writes, password reset and session invalidation, safe serialization, module-aware effective access, and no-store static responses.

The frontend suite executes every module script in registry load order and requires every registered route to resolve to a function-valued renderer (or an explicit valid compatibility redirect). It also verifies that every sidebar and quick-access route is registered, unfinished workflows resolve through the uniform non-mutating construction renderer, the dashboard uses bounded persisted sales/alerts/workforce endpoints instead of fabricated activity, internal feature keys are not presented to staff, and sidebar colors use semantic Light/Dark tokens. It checks the `users-list` → `user-management` mapping, the registry-derived Settings groups, real Settings and catalog renderers, table-first category/unit pages and their shared compact modal hooks (validation, guarded close, duplicate-save prevention, status actions, initial focus, Escape, and focus trapping), absence of standalone administration modules and duplicate route IDs, legacy redirects, operational shift placement, search/sidebar/quick-access/dashboard/direct-route gating, tab reconciliation, disabled/re-enabled feature navigation, RTL/themes, and compact layout. Startup validation, one-time feature/access listeners, deduplicated loads, formatter updates, and the absence of state-loading calls inside the Settings hub renderer guard the prior recursive-render freeze. These tests verify route-to-renderer execution rather than merely finding a route in JSON.

Customer navigation coverage requires the customer sidebar to contain only the management, statement, credit-payment, and export entries, keeps `customer-add`, `customer-edit`, and `customer-subscriptions` registered but hidden from navigation and search, and resolves the contextual create/edit routes to the real customer form. A VM regression renders `customers-list`, asserts the Arabic `+ إضافة عميل` action and the per-row `تعديل` action, opens both modals, and verifies the saved customer values plus the `POST /api/customers` and `PUT /api/customers/{id}` calls; it also renders the hidden `customer-add` route and verifies its form still saves through the same API.

The maintained screen matrix is `docs/ui-screen-coverage.md`. “Registry verified” means metadata/renderer/navigation consistency was checked automatically; it does not claim a manual browser test. “Browser smoke” records a route actually exercised in the browser.

For a manual smoke run, provision an isolated administrator, start the host with an isolated database, and verify login opens the first-run activity route. Search, choose multiple activities, select the primary, inspect the recommendation delta, cancel once, then save and reload. Remove an activity and confirm no capability is silently disabled. Sign in as an operator and confirm the route is read-only. Also test session restoration, dashboard, POS, sales, purchase editor, customer/supplier lists and search, low stock, financial movements, reports, maintenance tracking, themes, compact sidebar navigation, and logout. In the customer module, confirm the sidebar shows only `إدارة العملاء` and `كشف حساب العميل`, then create a customer from `إدارة العملاء` with `+ إضافة عميل` and edit it with the row `تعديل` action. Confirm that remaining business actions identify themselves as session-only and that logout returns to `login.html`.

The customer statement frontend regression also asserts that the active renderer mounts the scoped directory, customer-modal, filter, five-total summary, current-balance emphasis, and compact ledger hooks. Visual acceptance remains manual: at 1366×768 in both Light and Dark themes, verify RTL alignment, keyboard focus, the balanced current-balance/four-period-card arrangement, bounded table scrolling, and the add/edit modal at the 760px and 560px breakpoints. Compare displayed statement values and movement rows before and after the visual change; the refinement must not alter API requests or financial data.

For native acceptance, set `ALKHAYAL_DATABASE_PATH` to a dedicated local SQLite file and `ALKHAYAL_WEBVIEW2_USER_DATA_FOLDER` to a separate test-artifacts directory before launching WinForms. Verify the canonical database and profile paths and confirm neither overlaps the operator defaults. The profile override is covered by `WebView2UserDataFolderResolverTests`; an invalid path must fail startup visibly rather than opening the default profile.

For the Task 3 hotfix, continue in the isolated database as Admin: open Settings, then Business Activities, Modules & Features, and Users & Permissions; create a temporary cashier, save independent screen grants, log in as that cashier, and confirm every administrative discovery/direct-route path is rejected. Return as Admin, close and reopen User Management at least five times, repeat Settings and Modules & Features navigation, restart, and confirm the account and grants persist. Never run this destructive smoke sequence against the normal LocalApplicationData database.

An isolated in-app-browser smoke run verified real login, automatic first-run activity routing, multi-selection, primary change, merged recommendations, confirmation, successful SQLite save, and compact RTL rendering in Light and Dark themes. The Task 2 pass additionally opened the real feature-management route from settings and verified its catalog in Light and Dark at normal size, 900×600, and 700×600; the 700px view reduced cards to one column while retaining the icon rail and bounded workspace scrolling. The freeze regression pass ran the compiled WinForms host with the installed WebView2 Runtime and an isolated SQLite database, opened the feature screen repeatedly, toggled and restored an optional module and product capability, verified navigation updates, restarted the desktop app, and confirmed the administrator state remained persisted. The native page remained responsive at its 900×600 minimum and in Light, Dark, and System themes. After five navigation cycles, the WinForms process and every descendant WebView2 process recorded 0.00 CPU-second growth during a two-second idle sample; before the fix, one renderer consumed about 1.55 CPU seconds in the same interval. Missing-runtime messaging, unavailable-server messaging, application-origin navigation allowlisting, and clean host/browser disposal still require their separate lifecycle checks. The current x86 target matches the development runtime. Windows 7/Chromium 109 packaging, actual Windows 7 execution, printer drivers, barcode hardware, backups, and LAN firewall/TLS behavior remain unverified deployment work.

The frontend test also performs static shell-contract checks for the single workspace scroller, intrinsic grid sizing, absence of the old POS viewport calculation, and the six responsive target sizes. These checks do not replace real browser layout inspection. A visual/DPI pass must be recorded only after an authenticated shell is opened at 100%, 125%, 150%, and 175% Windows scaling; this environment was able to inspect the real WinForms/WebView2 login window but did not automate credentials.

The product capability regression set also covers the FeatureRegistry-driven Arabic switch labels, the separate global/product state, hidden contextual sections, dependency messages, and retention of the non-operational inventory boundary. Run `node --check frontend/js/modules/catalog.js` before the full frontend registry test, then use isolated SQLite databases for catalog persistence and feature-disable/re-enable scenarios.

The Phase 2.2 form correction adds isolated SQLite regressions for an all-off simple product with automatic exact `1/1` base mapping and subsequent edit, and an initial 2×2 free-text size/color save with SKU/barcode retention and rollback for an invalid base ratio. The frontend registry checks verify hidden global-off switches, inline editable rows, canonical base-unit request formation, and tab-draft hooks. Authenticated WinForms/WebView2 interaction, focus behavior at minimum size, and Light/Dark/System visual inspection require a separate manual desktop run with an isolated database; source-level assertions do not substitute for that run.

The variant visibility regression uses isolated SQLite and a live authenticated OWIN API to verify one paginated row per product, ordered size/color summaries, active combination counts, expiry configuration, detail hydration, restart persistence, unchanged variant IDs and identifiers, safe addition/removal of unreferenced combinations, and transactional rejection of removal when SKU/barcode history exists. A frontend VM regression renders the saved detail model and checks readable combination labels, input values, historical global-disablement treatment, badge truncation, and full-value tooltips. The default local database inspected during this hotfix had no catalog schema or products; actual T-shirt records on another configured database were not inspected or changed.

Batch 2 frontend checks require the product form to expose only expiration, color, and size switches; omit product-type, batch, serial, and IMEI controls; provide a manual barcode field and Arabic generation button; and preserve hidden legacy capability/unit data in edit submissions. Existing catalog integration coverage verifies aggregate initial saves for sizes, colors, barcodes, and expiry tracking, restart persistence, and barcode uniqueness across active and retired rows.
Barcode label regression coverage verifies the product modal's delete confirmation/cancellation, exact barcode retirement, active lookup exclusion, native-renderer preview with zero print submissions, and explicit one-job confirmation. Print integration uses a recording backend and isolated SQLite output; it never sends a physical print job.
# Task 2.4 validation

New Invoice regression: the frontend suite exercises empty/posted reset without a prompt, unposted-item confirmation, cancel preservation, duplicate confirmation prevention, cleared invoice/customer/payment/discount/date state, retained warehouse, in-flight save/post protection and absence of server mutations. The current source/DOM layout contracts check that `فاتورة جديدة` occurs once in the shared summary action area; its responsive placement and keyboard reachability require manual acceptance.

Touch POS checkout regression (updated 2026-11-08): `node frontend/tests/touch-pos-layout-tests.js` performs source-only CSS/layout checks without launching a browser. `node frontend/tests/touch-pos-invoice-tests.js`, also included by `node frontend/tests/run-tests.js`, checks the production renderer in the deterministic DOM: matching new/view/edit panel structure, expected/absent mode actions, toolbar search/customer/refresh controls, unique IDs and resumed-cart retention, alongside existing snapshot, confirmation, quantity/discount, versioned save, permission and stale-load regressions. The previous headless-browser geometry harness has been replaced; these tests do not prove pixel bounds or visual responsiveness. No browser automation or visual inspection was performed for this change.

Manual Touch POS acceptance: test 1024×768, 1280×720, 1366×768, 1440×900, 1600×900 and 1920×1080; expanded/collapsed sidebar; normal invoice and existing-invoice editing; empty and long carts; long Arabic names, large prices/quantities, customer/warehouse dropdowns, card/transfer instruments, delivery/collection controls and Light/Dark themes. Confirm refresh stays in the toolbar without a separate row, panes wrap/stack without clipping, all normal/edit actions (especially New Invoice) can be reached above the footer by the applicable scroll strategy, and keyboard/scanner operation remains intact. Repeat at Windows DPI 100/125/150/175%; Chromium 109/Windows 7 runtime qualification is manual deployment work.

Touch POS barcode-input regression (2026-11-01): `node frontend/tests/run-tests.js` covers Enter-terminated scanner sequences outside the product search, repeated scans merging quantity, unknown-barcode recovery, manual product-search Enter, and customer-search focus safety. Scanner capture is routed through the existing global shortcut listener so active modals and F1-F10 remain protected.

`InventoryPersistenceTests` uses isolated temporary SQLite databases for migration 011, upgrade from schema 010, receipt/issue/transfer, rollback, idempotency, nonnegative concurrent issue, batch/serial/dual-IMEI transfer, stale and zero-difference count, reversal, immutable history, projection equality, and audited rebuild backup. The Phase 2 stabilization regression adds service-level `InventoryService` coverage for the `v2:` canonical request hash: delimiter/newline payload distinction, identical replay, changed-payload conflict, and seeded legacy unversioned-hash conflict that does not repost. Count and reversal hash replay/conflict behavior are also covered. A focused expiry test verifies server-local-date policy: a lot expiring yesterday is blocked for manual issue and transfer, while a lot expiring today remains allowed. `InventoryApiTests` exercises session, CSRF, screen permission, and persisted posting over the local HTTP host. `frontend/tests/run-tests.js` checks the compact Inventory sidebar, real renderer loading, API wiring, and compatibility route redirects. Native authenticated WebView2 behavior still needs manual target-machine acceptance, especially Windows 7/Chromium 109.

## Warehouse recovery and FIFO regressions

`InventoryFifoTests.cs` extends the isolated `InventoryPersistenceTests` fixture with different-cost receipts, partial/across-layer consumption, residual quantities/value, source-preserving transfers, destination failure rollback, adjustments, count shortages, unit/variant prices, rejected inputs, real pre-016 migration, unresolved-cost blocking, evidenced opening-basis reconciliation, immutable history, restart and replay. `InventoryCostingTests` verifies cumulative rounding, zero actual cost and overflow rejection. Inventory API tests verify session, CSRF, cashier restrictions, variant-price persistence and duplicate receipt replay. Migration expectations advance to 16; pre-inventory fixtures omit migration 016 until inventory tables exist.

`node frontend/tests/run-tests.js` includes `inventory-tests.js`: it executes the production handlers in a deterministic DOM to verify ordinary/tracked/variant/unit fields, selected variant retail guidance, required purchase/sale prices, direct Save, in-flight duplicate protection, preserved inputs, lost-response identity, changed-request identity, refresh, adjustments/transfers and exact/incomplete valuation. This adds to the existing customer create/edit and 99-route regressions. It is not a claim of native or visual verification. Use the Arabic manual checklist in [warehouse-recovery-report.md](warehouse-recovery-report.md), with an isolated database/profile. Chromium 109 supports the BigInt arithmetic used for display totals; Windows 7 runtime qualification is still required.

## Search focus and stale-result regressions (Batch 1 UX)

The frontend registry suite verifies that the compatible `business-activities` route is presented and searchable as `إعداد الموازين`, uses the persisted scale barcode profile API, and contains no business-activity reads, selection controls, or save action. Backend activity records and APIs remain outside this UI-only regression boundary.

`frontend/tests/run-tests.js` also verifies the user-reported focus/navigation batch (`docs/qa/manual-feedback-improvements.md`, UX-001..UX-006): shared search inputs (`#listSearch`, `#catalogSearch`, `#categoryManagementSearch`, `#unitManagementSearch`, Settings-hub search, POS `data-search`, `#salesSearch`, sales-returns search) must keep a single mounted input node while their results region repaints (no full-container redraw, no refocus-after-redraw hack); POS product searches are token-guarded so a slower older response cannot replace newer results; persisting-data fetch repaints in `renderers.js` guard with the shell render generation (`context.isActive`) so a screen left in flight cannot repaint another screen. Invoice-history and returns list searches refresh only their dedicated results regions. These tests use a minimal in-script DOM harness and make no native WebView2 or visual claims.

## Phase 4 purchasing regressions

`SupplierLedgerMigrationTests` starts from the pre-purchasing migration set with historical supplier invoices/payments. It verifies the one-time legacy document imports and derived opening balance preserve the exact current payable without double counting. A mismatched balance must fail the migrator before migration 017 creates ledger tables or changes historical rows.

`PurchasingPersistenceTests` verifies draft persistence and audit-backed posting, exact scale-4 invoice totals, shared-transaction receipt/FIFO/ledger writes, stable post replay, cash purchase payable/payment equality with its selected-account treasury withdrawal, and rollback after an injected late inventory failure. Return tests cover original-price account value versus actual FIFO value, explicit variance fields, retry identity, transferred origin layers, return availability caps, and original serial validation/retirement. The purchasing frontend test verifies supplier search loading, bounded server pages, load-more, selection, and that opening the invoice does not fetch the full directory. `SupplierPersistenceTests` covers unified ledger statements/history, scale-4 idempotent payments, key conflicts, invoice remaining amounts, and the listing of both legacy and ledger-native payments.

Frontend registry tests require real purchase-management, purchase-list, and purchase-return renderers. Purchasing controls are Arabic/RTL, collect batch/expiry/serial/IMEI information for configured products, choose refund versus supplier credit explicitly, and state that the account-only cash events do not create treasury movements. Automated renderer tests do not replace a native WebView2 visual smoke test.

## Sales Phase 5

`SalesPersistenceTests` uses disposable SQLite files to verify fractional quantity/tax snapshots, scale-4 totals, FIFO issue cost, post retry identity and conflicting-key rejection, stock-floor sale behavior, injected late-failure rollback, repeated migration startup, credit customer balance and statement references, partial and full collection, partial return/refund/customer credit adjustment, return quantity limits, FIFO cost restoration, concurrent attempts to sell/return the last unit, and serial/IMEI/batch/expiry restoration. Frontend registry tests check the invoice, touch cashier and returns renderers, APIs, and exact `الكاشير اللمسي` title. `node --check` covers the sales module and shell scripts. The historical Phase 5 browser print path has been replaced by Phase 7.2 native printing. Actual WebView2 viewport and physical printer output require target-machine smoke checks; see the Phase 7.2 verification record.
# Stabilization Part 1 regression

`SalesCancellationValuationTests` uses unique, previously nonexistent SQLite paths under the system temp directory and asserts that they are outside `%LOCALAPPDATA%\AlKhayal`. It covers draft deletion, unpaid credit reversal across multiple FIFO layers, financial and stock reconciliation, cash/partial-payment/return blocks, changed-key conflicts, concurrent retries, injected rollback, and warehouse/variant valuation. Existing sales, purchases, customer operation-key, and FIFO tests remain part of the financial regression set. Do not run these tests against the operator database.

Stabilization Part 2 extends the disposable regression set with Touch POS net-selling order after a return, deterministic pagination, category/search and warehouse availability, a 15,000-product server-side page check, bounded customer lookup by name/code/phone, and percentage-to-fixed discount posting with tax and customer ledger reconciliation. The frontend VM suite tests default loading, stale-response rejection, category and warehouse requests, page append, customer selection and checkout payload, and exact percentage rounding. These checks do not replace a native WebView2 acceptance pass at 1366×768 and approximately 900×600 or a physical scanner test; see [qa/stabilization-part-2.md](qa/stabilization-part-2.md).

Part 3 adds concurrent customer code allocation, paged supplier search, warehouse balance searches and a 15,000-row disposable balance page check. See [qa/stabilization-part-3.md](qa/stabilization-part-3.md) and [qa/stabilization-final-report.md](qa/stabilization-final-report.md) for actual results and unexecuted manual checks.

## Phase 7.1 workforce verification

WorkforceServiceTests, WorkforcePersistenceTests, WorkforceApiTests and frontend/tests/workforce-tests.js cover optional employees, attendance intervals/audit, shift reconciliation, source association, replay/concurrency, rollback, feature lifecycle, owner scope, CSRF and compensation privacy. All writes use disposable databases. Existing migration fixture counts now include additive 071 while historic fixture builders exclude it until the upgrade under test. The customer-statement fixture derives dates from its own ledger instead of assuming a fixed execution day. See [Phase 7.1 validation and handoff](phase-7/phase-7-1.md#validation-and-remaining-manual-acceptance).

Migration 076 workforce payroll regressions cover unique national ID persistence, archive retention, additive attendance breaks, cash advance withdrawals, payroll net calculation, one-time advance allocation, duplicate monthly payout rejection, idempotent retries, treasury balance reconciliation and immutable source rows. All use the disposable Workforce/Treasury fixture. Frontend coverage includes the new Payroll and Advances routes and generated operation identifiers.

For the 2026-10-31 workforce extension, frontend registry/behavior tests passed across 128 screens; all 37 JavaScript files passed syntax checks and `git diff --check` passed. The environment lacked .NET Framework 4.8 targeting references and restored NuGet assets, so the solution build and newly added C# workforce payroll/API persistence regressions remain unexecuted (`MSB3644`, `NETSDK1004`).

## Native printing verification — Phase 7.2

Thermal receipt layout regression: focused `PrintingLayoutTests` assert 58mm/80mm hierarchy, wrapped Arabic names, amounts, discount/tax/delivery-fee rows, bounds, light separators, official-logo and hidden-logo cases. Both sizes are rasterized through `WindowsPrinterBackend.PreviewAsync`; the generated PNGs were inspected visually for alignment and content-height cropping. `DeliveryApiTests` asserts the persisted delivery fee reaches the print projection. These checks do not establish physical paper output or driver behavior.

Live printer-preview tests execute the production Add/Edit modal handlers with a controlled timer and pending API responses. They cover unsaved PNG upload/removal, logo visibility, 58mm/80mm width changes, document type, footer edits, debounce, newer-response priority, failed refresh with stale-image labeling, and cleanup after close. The focused native layout test renders a receipt longer than one 297mm page and checks the preview includes its totals. No test sends a physical print job.

An additional native branding-preview test calls `PrintingService.PreviewAsync` with unsaved template values and the real `WindowsPrinterBackend`/`NativePrintRenderer`. It confirms an uploaded PNG changes the returned raster, hiding the logo changes it again, and switching 58mm to 80mm increases the PNG width without any repository or printer submission.

Printer-management regression: the printing frontend VM test covers the empty state, installed-printer selection, explicit 58mm choice, synthetic native-preview request, delivery assignment, persisted save request, assigned-printer removal and RTL/theme token presence. Focused persistence tests exercise one printer shared by sale/return/delivery, two printers with 58mm/A4 templates, restart, legacy profile fallback, job routing and rejection of dangling assignments. The HTTP test covers the new branding preview without a print submission. All backends are recording fakes and databases are disposable; Windows drivers and paper output still need manual hardware acceptance.

PrintingLayoutTests exercise native Arabic/mixed text rasterization, five paper formats, wrapped descriptions, exact long numeric fields and page conservation without printing. PrintingPersistenceTests/PrintingApiTests use disposable SQLite files and an injected recording backend for restart, optimistic configuration, duplicate suppression/reprints, failed auto-print, source-document totals, financial nonmutation, shifts and HTTP security. `printing-tests.js` executes the shared frontend transport and designer. Final full suites: **83 unit + 218 integration passed**, zero skipped; frontend registry/behavior and all JavaScript syntax checks passed. Complete commands, timings, initial failures repaired, hardware limits and manual acceptance: [Phase 7.2](phase-7/phase-7-2.md). No physical printer jobs were submitted.

Receipt branding/layout regression coverage also verifies the embedded official AlKhayal mark on the existing native 58mm/80mm-capable templates and preserves bounded logo rows. Sales, purchase, delivery, and return document projections include applicable payment-method text in the existing header area; no financial or printer-driver behavior is changed. Physical 58mm/80mm output remains pending target-device testing.

## Barcode and label verification — Phase 7.3

Final complete suites: **97 unit + 224 integration passed**, zero failed/skipped; frontend registry/behavior passed for **105 routes**, all frontend JavaScript syntax and diff whitespace checks passed. LabelLayoutTests cover real encoding/decoding, integer DPI bars/quiet zones, all presets/custom/A4/roll pitch, start cells and overflow. LabelPersistenceTests/LabelsApiTests cover atomic identifiers, persisted fields, independent settings, durable jobs/concurrency/failure/financial nonmutation and HTTP security. `label-tests.js` executes production frontend handlers for replay, bulk pagination/staleness, designer and draft survival. Tests use disposable databases/recording printers. No physical output. See [commands, timings and manual acceptance matrix](phase-7/phase-7-final-report.md).


## Sales payment instruments

Configured wallets and card terminals, atomic full-invoice attribution, administrator-only management/history, legacy contract mapping, electronic refund restrictions and physical cash shift exclusion are documented in [payment-methods.md](payment-methods.md). Migration 074 is additive; original financial records and credit collection workflows remain intact.

The executable payment frontend suite also covers the operational overview aggregate and five-row recent-history request, CSS-native distribution percentages, zero-data states, permission-aware shortcuts, invoice navigation, directory total/active/inactive summaries, mounted search, filter reset, add/edit duplicate protection, instrument details, history reset, and stale-response rejection. Visual acceptance remains required for light/dark themes and the target 1366×768 desktop viewport.


## Maintenance advance regression hotfix

`MaintenancePersistenceTests` now verifies receipt-only intake for customers whose existing debt is zero or smaller than the advance, cash/credit delivery, full prepayment, existing legacy-advance reuse, invalid/mismatched settlements, atomic intake and late-delivery rollback, and operation-key replay/conflicts. The original test was reproduced with `Customers.Balance = -95`, then corrected by moving advance application to final-charge posting rather than weakening the nonnegative customer balance constraint. All databases use the disposable temporary fixture. The focused class passed 11 cases and the full integration suite passed 250/250. See [maintenance-advance-hotfix.md](maintenance-advance-hotfix.md) for commands, evidence, accounting timing and remaining limits.

## Phase 9.1 report regressions

SalesReportTests, TransactionReportTests, ReportsApiTests (TreasuryApiTests partial) and ReportPeriodTests exercise real isolated SQLite queries, real purchase/sales/return lifecycles, gross/event/payment reconciliation, legacy instrument attribution, exact strings, stable pages, 30k sales/payments plus 10k purchases/returns, nonmutation/reopening, timezone/DST boundaries and direct HTTP session/effective-screen authorization. `frontend/tests/report-tests.js` executes all four production report renderers, including filters/reset, summaries, pages, error/retry, permission-aware links and stale results; it runs from the complete frontend suite. Full execution: 279 integration and 130 unit tests passed; frontend passed for 112 routes. Distinct TRX files, complete commands and blocked rendered/native acceptance are recorded in [Phase 9.1](phase-9/phase-9-1.md).

The Phase 9.2 expense voucher report adds disposable-fixture reconciliation and real HTTP permission checks plus frontend handler coverage. See [Phase 9.2](phase-9/phase-9-2.md#expense-voucher-report--recovery-implementation).

`NetProfitReportTests`, `NetProfitApiTests` (TreasuryApiTests partial), `NetProfitServiceTests` and the extended executable report renderer tests cover the real SQLite-to-UI Net Profit slice. Coverage includes original saved tax/discount return residuals, original FIFO, eligibility audit/retry/conflict/CSRF, missing sources, later events, daily/monthly/DST reconciliation, excluded collections/advances, 30k saved sales, bounded ranges, paging and read-only snapshots. Historic migration fixtures now exclude 075 until their upgrade step. [Actual commands/results and compatibility limits](phase-9/phase-9-2.md#this-deliverys-validation).

Resumed Net Profit acceptance on 2026-10-30 completed **296 integration + 137 unit tests**, zero failed/skipped, with distinct TRX files under `artifacts/net-profit-completion/results`. Focused checks passed 15 SQLite/HTTP cases and 7 original-tax/service cases. Added regressions cover cross-layer FIFO returns, missing saved tax/restored cost and final-calendar-month clipping (reproduced date overflow before its fix). Frontend passed 114 routes; syntax passed 50 JavaScript files; the isolated full solution build passed with zero warnings/errors. Native WebView2/Windows 7 acceptance remains separate.

## Phase 9.2 final product/inventory coverage

`StockReportTests`, `StockInventoryReportTests`, `StockReportsApiTests` and `StockReportServiceTests`, plus the converted-unit pricing/FIFO regression, exercise actual saved FIFO batches, price edits, gross-profit reconciliation, original tax/discount repeated returns, cancellation event periods, variants/units, stable sorting/paging, 105-row catalog aggregation, current exact valuation, transfers/counts/adjustments/purchase returns, active availability assignments, missing evidence, independent HTTP permissions and server-side financial-field removal. `report-tests.js` executes all five renderers for filtering/paging, focus-node survival, escaping, empty/error/retry, source navigation and stale responses. All test databases are isolated temporary files; build artifacts/TRX outputs are separate from running binaries. [Final executed commands/counts and outstanding native acceptance](phase-9/phase-9-2.md#final-automated-validation).

## Phase 9.3 operational regression

`OperationalReportTests`, `Operational_reports_HTTP_independent_permissions_module_gates_overview_nonleakage_and_read_only`, `OperationalReportServiceTests` and operational cases in `frontend/tests/report-tests.js` cover ledger snapshots versus period events, registration boundaries, frozen shift cash and separate differences, actual attendance durations, maintenance advances/delivery, schedule allocations/coverage, exact overview reuse/incomplete profit, independent permissions, module gates, read-only bytes, paging, links and stale responses. All SQLite fixtures are disposable; set `ALKHAYAL_DATABASE_PATH` to a unique isolated fallback. Use `--artifacts-path artifacts/phase-9-3`, separate assembly TRX filenames and the commands/results in [Phase 9.3](phase-9/phase-9-3.md). DOM tests do not establish native visual acceptance.

## Phase 10.1 licensing

Use the isolated commands and actual results in [Phase 10.1](phase-10/phase-10-1-licensing.md). LicensingTests uses disposable RSA keys and fake hardware; ActivationDialogTests runs actual WinForms copy/paste, three-control, title-bar-close and 100–150% logical layout checks on an STA thread without WebView2; NodeLicenseCompatibilityTests issues through the JavaScript/SQLite manager and verifies/activates with .NET; LicenseAccessTests exercises the real signed-license/storage pipeline with localhost HTTP and a direct Web API host. Existing HTTP fixtures explicitly inject TestLicenseGate from the test assembly, never an environment bypass. Node tests also exercise authenticated loopback endpoints, hostile-origin rejection, append-only SQLite backup/restore, and the installed Desktop package with external network calls denied and process restart. Windows 7 and complete WebView2/installer acceptance remain separate deployment checks.

Activation dialog coverage also checks the copy/paste-only instruction, LTR code fields, keyboard focus order, copy confirmation, invalid-code feedback, and repeat-click prevention while an async installer is pending. A disposable signed license exercises success feedback and the modal return. TrialDialogTests opens both native color palettes and checks the trial and support actions. The layout cases apply 100%, 125%, and 150% logical sizing to a 1366×768 budget. Windows display scaling and the production UAC prompt require a separate device check.

## Activation storage/UAC regression

LicenseInstallationTests distinguishes actual medium-token Windows ACL/CreateNew denial, native path/rename/hard-link/junction and CLI rejection from simulated elevated targets/UAC results. ActivationDialogTests uses explicit fake installation ports to test preserved text and retry without raising real UAC. Full separate-standard-account credentialed elevation and Windows 7 remain UNVERIFIED; no real license is a fixture. [Actual results and manual acceptance](phase-10/activation-storage-hotfix.md).

OperationLogApiTests exercises authenticated GET and POST success, validation-failure records, actor/time/path attribution, administrator-only listing, text filters and SQLite immutability on an isolated database. The frontend registry test validates the administrator-only Settings route and real renderer; the executable frontend suite covers initial loading, filter/paging controls and escaped API values.

## Task 083 delivery service

`DeliveryApiTests` uses disposable SQLite files and an authenticated OWIN host to cover migration 083 fresh/upgrade/restart paths, driver CRUD and effective permissions, paged/filterable current orders, cash and credit posting, contact/driver snapshots, delivery fees, status transitions, cancellation, invoice correction, delivery documents and an injected late-post rollback followed by retry. The focused run completed **9/9** integration tests. Related focused regressions completed **10/10** sales/printing fixture tests, **8/8** cancellation valuation tests, **14/14** Touch POS invoice/correction tests, and **1/1** required-shift test. `frontend/tests/delivery-tests.js`, included in the full frontend suite, covers the Touch POS delivery modal/prefill/switching/fee/driver behavior plus Delivery Orders loading, filters, pagination, statuses and printing; the full suite passed with the 131-route registry check. All databases are isolated; native WebView2 and physical-printer acceptance remain separate target-machine checks.


## Task 4 Touch POS sorting/categories

`TouchPosBrowseTests` covers real sale/return/cancellation projection updates and replay, failed-post rollback/retry, zero-stock financial return, saved converted-unit backfill and startup replay, deterministic no-sale name/ID ties across pages, exact relevant category IDs, general-catalog compatibility, zero-stock checkout, and bounded pages over 15,000 products with 30,000 synthetic saved sale lines. Query-plan assertions inspect detail rows and verify primary-key projection lookups without sales-ledger reads. The bulk fixture measures browsing only; real accounting/FIFO lifecycle coverage uses the smaller fixtures. Earlier-schema fixtures exclude migration 078 until their upgrade step.

The production sales renderer VM tests assert default All selection, category/search/page retention, stable category-ID colors and enabled zero-stock tiles alongside existing live-search/stale-response and paging tests. [Commands, actual results and performance limits](qa/task-4-touch-pos.md). Native WebView2 and Windows 7 acceptance are separate and unexecuted for this task.

## Task 5: Touch POS invoice view/edit

TouchPosInvoiceEditingTests uses unique temporary SQLite files for snapshot viewing/discard, quantity and line/invoice discount saves, optimistic version rejection, invalid-price rollback, posting replay, FIFO/customer reconciliation, and unchanged complete projection rows after rejected posted cash/credit/paid/returned/canceled/deleted edits. TouchPosInvoiceEditingApiTests.cs adds a real localhost HTTP test in TreasuryApiTests with injected TestLicenseGate: authenticated GET, CSRF enforcement, expectedVersion mapping/stale 409, draft-only saves, immutable posted rejection and duplicate-free posting replay.

frontend/tests/touch-pos-invoice-tests.js executes production renderer handlers for original saved totals versus changed current taxes, inactive warehouse names, navigation, first-change confirmation/decline, quantity/discount/product edits, cancellation, versioned save/double-submit, unsupported states, stale loads and suspended new-cart resumption. It is included in run-tests.js. The historical headless Edge geometry check has been replaced by the source/DOM contracts documented above; no current visual geometry pass is claimed. [Actual commands, results and compatibility limits](qa/task-5-touch-pos-invoices.md).
### Posted invoice correction coverage

`TouchPosInvoiceEditingTests` uses disposable SQLite databases to cover same-day and same-shift eligibility, closed and prior-day rejection, immutable revision history, quantity/price/discount totals, customer credit and receivable changes, partial-payment refund rejection, exact FIFO reversal, sell-without-stock coverage, tracked serial reuse, idempotent replay, concurrent writers, injected failure rollback, restart-safe persisted records, and projection/foreign-key verification. `Touch_pos_invoice_http_view_versioned_edit_and_posted_rejection` covers authentication, CSRF, the correction HTTP contract, explicit cash refund, inventory and treasury reconciliation, and replay. `frontend/tests/touch-pos-invoice-tests.js` covers main-POS invoice loading, first-change confirmation, posted price/cart editing, explicit settlement submission, save, and cancel.

## Task 6: reorder and unified alert regression

`ReorderPointTests`, `ReorderAlertTests`, `AlertPersistenceTests`, `ReorderAlertApiTests` and `frontend/tests/alerts-tests.js` cover scale-6 threshold rules; totals across warehouses, variants and batches without duplicate product rows; inactive references; zero/reorder boundaries; remaining-stock expiry rows; unpaid, partially covered and fully paid installment schedules; authenticated full/legacy/revoked grants; per-user persisted read state; stock-scoped and full Mark All; migration 082 upgrades; related-record metadata/navigation; cached/coalesced refresh; unread badge updates; and every mounted category filter. Existing migration fixtures defer later migrations when their skipped prerequisites require it. All database fixtures use unique temporary paths; no production database or licensing file is used.

Commands: `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --no-restore -p:OutputPath=bin/Task8Alerts/ --filter "FullyQualifiedName~AlertPersistenceTests|FullyQualifiedName~ReorderAlertTests|FullyQualifiedName~ReorderAlertApiTests"`, `node frontend/tests/run-tests.js`, and `dotnet build AlKhayal.slnx --no-restore -p:OutputPath=bin/Task8Build/`. Separate output avoids locks from an already running desktop and requires no desktop restart. Chromium 109-compatible APIs/syntax are retained; native RTL/theme/DPI/Windows 7 visual qualification is not implied by automated tests.

## Task 7: mandatory cashier shifts

Focused disposable SQLite, authenticated localhost API/restart, correction/FIFO and frontend status tests: [Task 7 verification and compatibility limits](qa/task-7-cashier-shifts.md). Uses isolated `bin/Task7` outputs; the full legacy backend suite is outside this focused run.

## Touch POS customer and rider regression

`node frontend/tests/delivery-tests.js` covers the fixed first dropdown customer action, Escape/ArrowDown behavior, preserved cashier state on cancel, automatic customer selection after save, riderless and assigned delivery payloads, and ordinary sales; it now exercises the shared `R.openCustomerModal` path. `Delivery_without_rider_posts_and_preserves_null_assignment` covers persisted null rider, total and stock. `Delivery_http_no_rider_cash_and_credit_drafts_post_and_print_later_assignment` covers HTTP cash/credit drafts, posting, null driver snapshots, and delivery print output showing `تعيين لاحقًا`.

## Multi-unit POS pricing checks
frontend/tests/pricing-fifo-tests.js covers the automatic base-line price at quantities 1, 11, 12, 13, 24 and 25, and manual override persistence/reset behavior. The full suite is run with node frontend/tests/run-tests.js. Backend unit and integration coverage includes unit-price configuration, draft validation and inventory conversion snapshot tests.

## Batch, expiry receipt and alert regression

`frontend/tests/purchasing-tests.js` executes the real purchase renderer and verifies that persisted batch/expiry assignments remain visible even when the optional global capability is disabled, missing values block the draft locally, quantity edits preserve tracking values, and the saved payload carries the correct lot and business date. `frontend/tests/inventory-tests.js` covers the same conditional fields and payload for manual warehouse receipts, including serial/IMEI combinations. `AlertPersistenceTests` uses a disposable migrated SQLite database and a fixed clock to cover the exact 30-day boundary, outside-window and expired batches, separate warehouse alert identities, partial/complete consumption, restart-safe read state, and zero-balance removal. No operational database or native desktop instance is used.

## Purchase draft resume regression

`frontend/tests/purchasing-tests.js` mounts the real Purchase Management renderer with contextual invoice IDs and covers exact-ID loading, complete header and multi-line restoration, batch/expiry values, update-in-place requests, switching between drafts, late-response rejection, and safe handling of posted, missing, and invalid IDs. `frontend/tests/workspace-tabs-tests.js` verifies that contextual navigation remounts an already-open tab, clears stale route state, and invokes the editor's unsaved-change guard before replacement or close. `PurchasingPersistenceTests.Resumed_purchase_draft_restores_every_persisted_field_updates_in_place_and_posts_once_after_restart` uses a disposable migrated SQLite database to verify all persisted line/tracking identifiers and headers after a new repository instance, same-row update without duplication, no pre-post inventory/supplier effects, and exactly-once posting/audit/ledger effects.

Frontend numeric formatting uses the Latin numbering system for Arabic locale dates and numbers. Inventory row selling values follow the server valuation’s per-balance cent rounding; numeric decimal inputs normalize Arabic digit and separator paste without changing identifiers or free text. `frontend/tests/numeric-format-tests.js` and `inventory-tests.js` cover representative formatting, input normalization, precision, and rounding. Native number paste uses Chromium insertText to preserve the editing selection and native validation; identifiers are not transformed.

The numeric-input capture tests cover localized decimal text and native number paste dispatch, preserving free text and identifiers, malformed grouping, selected text, and failed browser insertion. The browser-specific number edit uses `document.execCommand("insertText")` so Chromium owns the current selection and native constraints; the test harness cannot prove legacy WebView2 behavior. Report summary cards and stock/sales report numeric cells use LTR isolation without parsing or altering their exact strings.


Touch POS compact layout validation (2026-11-08): `node frontend/tests/run-tests.js` passed (131 registered screens); both Touch POS test scripts passed. `node --check` passed for the sales module and both Touch POS test files. Release solution build with `--no-restore -c Release -m:1` and isolated `OutDir` passed with 0 warnings and 0 errors. `git diff --check` passed. No backend test execution, browser automation, screenshots, live database changes or visual/DPI qualification were performed.


## Saved sales draft resume and checkout (2026-11-08)

The production renderer keeps new invoices, saved-draft viewing and consented draft editing in the same Touch POS structure. The existing-invoice guard now distinguishes drafts from posted corrections; resumed drafts update their original ID and use the existing idempotent posting endpoint. No production backend/schema changes were needed. The existing container queries/scroll rules were preserved; source/DOM contracts do not qualify visual geometry.

`touch-pos-invoice-tests.js` now covers three-way consent (including real shared-modal Cancel/Escape/backdrop dismissal), declined mutation, one product add, quantity/price/discount/customer edits, same-ID versioned save/reopen and item order, read-only/posted checkout rejection, double-submit, interrupted POST response recovery with the original key and no duplicate draft creation, cancellation after Clear, long-cart structure, selected-ID refresh, missing/invalid/stale responses and unsaved replacement guards. `workspace-tabs-tests.js` executes the production navigate function to verify mounted-tab replacement, cancellation, exact next invoice ID and suspended-cart retention. Product pricing/FIFO, tracking/weight, payment instruments, delivery and shortcut regressions remain in the full suite.

`Resumed_draft_survives_restart_failed_post_and_replay_without_duplicate_effects` adds cash/credit cases using unique disposable SQLite files: recreate service/store, reopen saved quantities/payment selection, preserve original invoice ID/number, inject a failure at the final posted audit, compare complete projection/document snapshots after rollback, retry the same key, verify replay and reject alternate-key repost/draft update. Assertions cover one invoice, one stock deduction, FIFO allocation, customer balance/debt, cash payment/treasury counts, foreign keys and inventory projection verification. No owner database is opened.

Executed commands/results:

```powershell
node frontend/tests/touch-pos-invoice-tests.js
node frontend/tests/touch-pos-layout-tests.js
node frontend/tests/run-tests.js
# All passed; full registry includes 131 screens.
# node --check: all 44 frontend/js files plus the 3 changed test files passed.

dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --no-restore -c Release -m:1 -p:OutDir=%USERPROFILE%/AppData/Local/Temp/alkhayal-draft-resume-validation/ --filter "FullyQualifiedName~TouchPosInvoiceEditingTests|FullyQualifiedName~SalesPersistenceTests|FullyQualifiedName~PaymentInstrumentTests|FullyQualifiedName~DeliveryApiTests" --logger "console;verbosity=minimal"
# 34 passed, 0 failed, 0 skipped.

dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --no-build --no-restore -c Release -p:OutDir=%USERPROFILE%/AppData/Local/Temp/alkhayal-draft-resume-validation/ --filter "FullyQualifiedName~RequiredShiftTests|FullyQualifiedName~SalesTreasuryTests|FullyQualifiedName~SalesStockFloorTests|FullyQualifiedName~PaymentMethodsApiTests|FullyQualifiedName~Touch_pos_invoice_http" --logger "console;verbosity=minimal"
# 22 passed, 0 failed, 0 skipped.

dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --no-restore -c Release -m:1 -p:OutputPath=bin/DraftResume/ --logger "console;verbosity=minimal"
# 282 passed, 0 failed, 0 skipped.

dotnet build AlKhayal.slnx --no-restore -c Release -m:1 -p:OutDir=%USERPROFILE%/AppData/Local/Temp/alkhayal-draft-resume-validation/
# Succeeded: 0 warnings, 0 errors.

git -c core.safecrlf=false diff --check
# Passed.
```

The first unit-test attempt used an external temporary OutDir: 280 passed and two repository-relative licensing tests failed because their assembly-directory ancestry could not locate `AlKhayal.slnx`. Rerunning with the isolated repository-local `bin/DraftResume/` OutputPath resolved the environment issue; all 282 passed without test/production changes for those failures.

Manual acceptance remains: 1024×768, 1280×720, 1366×768, 1440×900 and 1920×1080; expanded/collapsed sidebar; new/view-only/resumed/posted modes; empty and long carts; long Arabic names and large values; light/dark and Windows DPI changes. Verify first/last cart items, totals and New Invoice/Save Draft/Complete Sale reachability; draft A→B while POS is mounted; decline/resume/cancel; tab return and refresh; restart then reopen; cash/card/wallet/credit, delivery and serial/IMEI/batch workflows on disposable test data. No browser automation, screenshots or visual acceptance was performed. Chromium 109 compatibility is retained by source/API choices; Windows 7/WebView2 runtime qualification is not implied.
# Touch POS item rows and invoice-only discounts (2026-11-08)

See [the cart layout verification report](qa/touch-pos-cart-layout.md) for commands, actual results and the manual acceptance matrix. Production-renderer tests cover five-cell rows in new/view/resumed/posted contexts, accessible long names/tracking, zero new line discounts, fixed/percentage invoice discount math/validation, legacy draft/posted view-only protection and unchanged saved totals. Existing quantity/price/remove, same-ID draft save/reopen/post, interrupted-post replay, unit-pricing/FIFO, weight, delivery and authorization tests remain in place. Weight UI assertions now inspect the quantity field instead of requiring the removed duplicate badge, and add scale-6 editing/rounding checks.

The weighted integration report query uses the same local business-day timezone as its invoice dates and `DateTime.Now` filters. Its prior UTC timezone returned an empty report between Cairo midnight and UTC midnight. All financial, stock, FIFO and report assertions are retained; no production backend behavior changed.


## Touch POS product-grid/cart balance (2026-11-08)

Changed only `frontend/css/modules.css`, `frontend/tests/touch-pos-layout-tests.js`, `docs/design.md` and this file. No renderer, business logic, API or database change. Existing uncommitted work was retained; no commit/push or browser/desktop automation was used.

The layout tests now cover actual-product-container thresholds (one/two/three/four equal columns), their exact boundary widths, a readable 128px card minimum and a four-column maximum. Policy arithmetic covers 1024, 1280, 1366, 1440, 1600 and 1920px viewports with the actual sidebar token widths (64/260px), POS gaps/padding and a conservative 17px scrollbar allowance. At 1366px and above both sidebar cases accommodate four columns; narrower cases select fewer when needed. They verify the 460px minimum fits all five existing cart cells, the lower 440px fallback, shared new/draft product/cart DOM, RTL, invoice actions and absence of per-item discount inputs. These are source/DOM and sizing-policy checks, not browser geometry measurements.

Executed successfully:

```powershell
node frontend/tests/touch-pos-layout-tests.js
node frontend/tests/touch-pos-invoice-tests.js
node frontend/tests/run-tests.js
# Passed, including 131 registered screens and existing draft/save/post regressions.

$taskSyntaxFiles=@(rg --files frontend/js -g '*.js')+@('frontend/tests/touch-pos-layout-tests.js','frontend/tests/touch-pos-invoice-tests.js')
foreach($taskSyntaxFile in $taskSyntaxFiles){node --check $taskSyntaxFile; if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}}
# Passed: 46 JavaScript files.

dotnet build AlKhayal.slnx --no-restore -c Release -m:1 -p:OutputPath=bin/TouchGridRelease/
# Passed: 0 warnings, 0 errors.

git -c core.safecrlf=false diff --check
# Passed.
```

Manual visual acceptance remains for 1024×768, 1280×720, 1366×768, 1440×900, 1600×900 and 1920×1080 with expanded/collapsed sidebar, both themes, long Arabic names, large totals and weighted quantities. Check four equal product cards where the pane permits, graceful three/two/one-column fallback, compact five-cell cart rows, metadata expansion and reachable discount/totals/Save Draft/Complete Sale/New Invoice actions. Confirm new/view/resumed invoices share the layout. No screenshot or interactive visual measurement was performed; native WebView2/Chromium 109 and Windows DPI/Windows 7 acceptance are not implied by automated results.


## Collapsed sidebar flyout (2026-11-08)

`node frontend/tests/sidebar-flyout-tests.js` passed **16 regression scenarios**. This dependency-free
Node fixture executes the production sidebar builder, effective feature/access predicate and flyout
event handlers. Coverage includes registry destinations/title, active page, pointer crossing and
delay, switching sections, route dispatch without storage changes, unchanged expanded behavior,
permission/module filtering and access rebuild, focus/Tab/arrow/Escape behavior, keyboard focus
retention, existing nested child trees, source scrolling, outside dismissal, responsive compact
mode and modeled viewport bounds at 1024x768, 1280x720, 1366x768, 1440x900 and 1920x1080.
CSS/source assertions cover bounded scrolling, body-level overlay layering and shell integration.
Nested coverage uses a synthetic child tree because current registry modules contain flat route arrays.

Executed validation:

- `node frontend/tests/sidebar-flyout-tests.js`: passed, 16 scenarios.
- `node frontend/tests/run-tests.js`: passed, 131 registered screens, including flyout,
  effective feature/access lifecycle and workspace-tab regressions.
- `Get-ChildItem frontend/js,frontend/tests -Recurse -Filter *.js | ForEach-Object { node --check $_.FullName }`:
  all 77 JavaScript files passed syntax checks.
- `dotnet build AlKhayal.slnx --no-restore -c Release -m:1 -p:OutDir=%USERPROFILE%/AppData/Local/Temp/alkhayal-sidebar-flyout-release/`:
  succeeded, 0 warnings, 0 errors. Isolated output avoids locks from a running desktop.
- `git diff --check`: passed; existing files have Git LF/CRLF conversion notices.

No browser automation, Computer Use, interactive desktop, database mutation, backend test execution,
commit or push was performed. Node fixtures do not render CSS or emulate native button activation.
The owner must visually verify the five requested desktop resolutions, light/dark themes, long
Arabic labels, pointer transitions/scrollbars, lower rail sections, keyboard focus/Enter/Space,
workspace stability, 100/125/150/175% Windows scaling and multi-monitor DPI transitions. Windows 7
with legacy WebView2/Chromium 109 remains an explicit manual compatibility qualification.

## Quick-access toolbar verification

Run `node -e "require('./frontend/tests/quickbar-tests')()"` for the focused priority, route, width-fit, and interaction checks. `node frontend/tests/run-tests.js` includes these checks, alert-badge duplication coverage, and workspace-tab regressions. Manually check the authenticated toolbar at 1024×768, 1280×720, 1366×768, 1440×900, 1600×900, and 1920×1080 with expanded/collapsed sidebar, Light/Dark themes, and 100/125/150% Windows scaling. Confirm the More menu is reachable by keyboard and pointer, lower-priority actions navigate correctly, labels do not clip, and unfinished POS/purchase work remains mounted when switching tabs. Automated width tests exercise the fit calculation, not rendered browser geometry; Chromium 109/WebView2 visual qualification remains manual.


The frontend registry and sidebar flyout regressions verify that all visible report routes appear exactly once in their related sections, filtered links match the expanded sidebar and collapsed flyouts, active report highlighting follows the route, and the general Reports group disappears when both general reports are inaccessible. Run `node frontend/tests/run-tests.js`.

## Settings cleanup regression

`frontend/tests/settings-cleanup-tests.js` renders the production Settings hub and Modules & Features renderer against representative effective state. It verifies that category/unit management remains registered but is not duplicated in Settings, the warehouse-settings alias is hidden, core modules and unsupported legacy options are absent, implemented optional modules remain available, and supported product properties/dependencies remain visible. The full frontend suite invokes this focused test. Backend feature tests continue to cover core protection, dependency validation, persisted state and administrator override preservation.
# Sales movement invoice viewing

`frontend/tests/report-tests.js` checks movement links to the POS route, `touch-pos-invoice-tests.js` checks posted read-only controls and draft correction behavior, and `touch-pos-layout-tests.js` checks the five-column cart and bounded scrolling source contract. The stock report and invoice correction integration tests check the linked invoice ID and latest revision. Source-level layout tests do not measure browser geometry; 1024×768 and 1366×768 visual acceptance remains a deployment check.
Barcode printer regression: `LabelPersistenceTests.Dedicated_assignment_persists_active_form_without_touching_receipt_and_direct_quantity_ignores_profile_copies` uses a fake XP-style active USER DEVMODE form at 78.7 × 101.6 mm, persists the label assignment through a new service instance, checks receipt settings remain separate, and verifies one direct label request reaches the backend with one application copy. `frontend/tests/label-tests.js` verifies the Product Directory direct request uses the saved barcode and exact quantity and blocks a second in-flight click. These tests do not submit a physical job; actual media alignment and scan quality require the one-label user check in `docs/product-barcode-label-printing.md`.

Run the label designer regression with `node frontend/tests/run-tests.js`, the Release unit suite, and the Release integration suite filtered by `FullyQualifiedName~Labels_HTTP|FullyQualifiedName~LabelPersistenceTests`. The HTTP regression verifies the base label path followed by the customized 50×30 mm path. It sends requests with both an explicit preview template and the default template, decodes native preview and job captures for two saved products, compares the rendered pixels, and checks prices, exact quantities, printer identity, persistence across a new service instance, reset and unreadable-layout fallback. The capture runs before Windows spooling. Physical feed, margins and scanner reliability require the target printer and loaded stock.
Printing designer regression coverage checks independent profile template selection after a SQLite reload, saved-document preview authorization and projection, bounded document search, native optional-field/column/order/typography rendering, and draft preview transport. The automated backend uses a recording printer; it does not verify physical XP-80C output or a real WebView2 display. Manually verify paper feed, Arabic glyphs, physical margins, printer offline behavior, and the 1366×768 Light/Dark modal before production acceptance.

`frontend/tests/catalog-margin-tests.js` covers initial margin, both calculation directions, purchase-price changes, decimal rounding, and zero/missing cost in the shared Add/Edit product binding. Run it through `node frontend/tests/run-tests.js`.

## Expiry-only stock regression coverage

The frontend suite covers removed lot controls in purchasing, inventory, catalog, and sales. Integration tests cover expiry-only receipt, multiple purchase expiry dates, FIFO sale allocation, partial linked returns and return caps, purchase returns, and transfer/adjustment behavior. Run `node frontend/tests/run-tests.js`, `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj -c Release --no-restore`, the Release solution build, and `git diff --check`.

Suspended POS invoices: verify an empty New Invoice starts immediately; a dirty cart offers suspend, discard, and cancel; suspend writes the existing sales draft endpoint and preserves cart data on failure; the count endpoint reads persisted drafts and updates on POS remount or checkout. The cashier no longer shows a Save Draft action. Run frontend tests, Release build, targeted sales tests, and git diff --check.

License unit tests locate repository scripts from the working directory, test assembly directory, or the compiled source path. This keeps the Node cross-signing and public-key guard tests executable when `OutDir` points outside the checkout; their license assertions are unchanged.

For the Windows backup regression, run the integration project with `--filter FullyQualifiedName~BusinessBackupTests` first with the default temporary directory and again with `THECASHIER_BACKUP_TEST_TEMP` set to an existing disposable directory with an absolute path around 128 characters long. Include spaces and Unicode in the deep path. The override moves only the backup fixtures and leaves the test runner's temporary directory available for assembly discovery. Archive paths returned by the backup service remain ordinary absolute paths. The Windows SQLite workspace needs room for rollback journal filenames, so an excessively deep live database location produces an actionable backup error.

# Reports Center regression coverage

`node frontend/tests/run-tests.js` verifies the single Reports sidebar destination, all 18 canonical report routes, permission/module filtering, directory search and category filtering, local 30-day range, primary/secondary KPI order, renderer registration, and the existing report screen behavior. Reporting integration tests validate persisted overview permissions, the posted-only purchases KPI, date filtering, and the existing calculation contracts; the center makes no new financial calculation. See [reports-center.md](reports-center.md).
Returns regression coverage includes server-paged invoice search by number/party/date/type/status, internal-ID lookup, partial/full eligibility, canonical Sales/Purchase list navigation, manual lookup, inline quantity ceilings, damaged-line payloads, duplicate-submit identity, and labeled detail sections for cash/account/no-treasury settlements. `frontend/tests/returns-tests.js` exercises the picker and responsive card contract; `ReturnManagementTests` and `ReturnManagementApiTests` exercise persisted eligibility, permission and API envelopes.

## Sales invoice PDF

The sales invoice list exposes **عرض PDF** on posted invoices. In a normal browser it opens a new tab. In the WinForms shell it retrieves the PDF using the WebView2 session, writes a temporary copy under LocalApplicationData, and launches it through the system browser URI handler; the POS view stays on the invoice list. The temporary copy is removed after seven days on a later PDF open, so users should save their own copy if needed. Verify that the external viewer shows all invoice lines, Arabic text, and the saved sale print template, then return to the still-open POS window. Draft invoices must not show the action. The PDF route requires a session and effective sales screen access, and generating it must not create a print job. The frontend invoice-list test covers both browser and desktop dispatch; the printing integration test covers the saved-document/template path and a long native-rendered invoice. Very long invoices above the 5,000 mm render cap return an error.

The Cashier native trial contact test runs on an STA thread and asserts the Cashier caption, mirrored layout, contact numbers, and button targets. The modal timer closes the dialog in a finally block and rethrows callback failures on the test thread. Clipboard transfer is an isolated desktop acceptance check.

StandaloneFirstRunApiTests serves the real Cashier login assets from a loopback API host, bootstraps a synthetic administrator into a fresh disposable SQLite database, creates an Arabic-named product, logs out, and checks the account and exact prices after recreating the host. Its test-only license gate applies only to the in-process fixture.

## Stage 1 design system

Run `node frontend/tests/design-system-tests.js` for theme tokens, defined contrast pairs and theme initialization. See [Stage 1 QA](design-system-stage1-qa.md) for the browser evidence, route coverage and remaining manual validation.

## Stage 2 navigation

Run `node frontend/tests/run-tests.js` for registry, redirect, sidebar, shortcuts, alert badge and tab regressions. The Edge runner at `frontend/tests/stage2-browser.cjs` exercises the repository frontend with synthetic API responses. See [Stage 2 QA](layout-navigation-stage2-qa.md) for reproduction commands, screenshots and the distinction between browser, API and native coverage.

## Stage 3 product and barcode QA

See [Stage 3 QA](catalog-stage3-qa.md) for the label tests and Edge screenshot runner. The browser uses a PNG from the native renderer and intercepts API traffic. The label HTTP test compares saved preview data to inputs captured by a recording printer backend. Driver and physical tests are reported separately.

## Final POS, sales and delivery QA

Run `node frontend/tests/final-sales-browser.cjs` for focused Edge regressions and add `--matrix` for visible route coverage in both themes. Run `node frontend/tests/run-tests.js` for the frontend suite and the Release integration filter `FullyQualifiedName~Delivery_` for disposable HTTP and SQLite tests. The live Edge runner `frontend/tests/final-delivery-live.cjs` takes a disposable QA host directory with its loopback URL and generated credential, checks for an empty database and uses real sale and delivery writes with printer dispatch intercepted. See [final module QA](final-stage-pos-sales-delivery-qa.md) and the [whole-application report](final-application-qa-report.md).

Run `dotnet test tests/AlKhayal.IntegrationTests -c Release --no-restore --filter FullyQualifiedName~SalesInvoicePaginationTests` for disposable SQLite tests with 1,000 through 500,000 sales invoices and an authenticated loopback purchase history test. Run `node frontend/tests/run-tests.js` for mounted search, return badges, detail navigation, PDF actions, and export wiring.

The authenticated sales pagination HTTP regression sends the actual initial query `search=&page=1&pageSize=50`, verifies a populated disposable database and global search, and checks invalid pagination. Web API can record a model-state error for an empty optional search, so pagination validation must inspect the pagination fields without rejecting that search.

Phase 2 history coverage runs in `ReturnManagementTests`, `ExpensePersistenceTests`, `SalesInvoicePaginationTests`, and the frontend registry suite. The expense history test posts 201 vouchers to a disposable database, then checks that the final page returns the oldest voucher and the treasury balance still reflects all vouchers.

Phase 3 coverage uses the customer directory and statement regression suite, the workforce persistence suite, authenticated loopback API coverage, and the frontend suite. The customer ledger fixture compares period totals and page-three running balances with the legacy statement. The payroll fixture reads the oldest month from 501 entries and checks that its paid status is available while viewing page one.
