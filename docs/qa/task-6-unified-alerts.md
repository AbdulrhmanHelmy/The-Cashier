# Task 6 — Product Reorder Point & Unified Alerts

Implemented on 2026-11-01, adding to the existing uncommitted working tree. No commit, push, real database migration, licensing-file edit, or running-desktop restart was performed.

## Behavior

- Add/Quick Add/Edit share `حد إعادة الطلب`, default zero disabled, persisted in additive migration 080 at quantity scale 6. Old clients omitting the field preserve it during updates.
- Application emits one real low-stock alert per eligible active stock product exactly when a positive threshold is >= its total base quantity across **all** warehouses, variants and batches. Inactive warehouse/variant balances still count; never-stocked products total zero. Product archive/inactivation and service/non-stock types suppress alerts.
- The single `التنبيهات` route combines the real stock feed with existing typed demo notifications, retaining read/unread and Mark All. Old stock alert routes are hidden redirects. Old stock-only grants retain access without exposing expiry/other notifications. Expiry/installment support in the type selector does not claim a new persisted alert generator; existing demonstration records remain clearly marked.
- The existing Quick Access bell displays unread current alerts. Shared cached state, successful stock-changing write events, in-flight deduplication, revision guards and coalescing update the page and badge. Draft saves, payments and unrelated writes do not query stock. Focus/manual refresh detects external changes without periodic polling. Read markers remain session-only, consistent with existing notifications; an observed resolution clears the stock marker for a later recurrence.
- The filter stays mounted during data/read refresh. RTL, existing themes, shell scroll ownership and Chromium 109-compatible APIs are preserved.

## Validation

| Executed command | Result |
| --- | --- |
| `dotnet build AlKhayal.slnx --no-restore -p:OutputPath=bin/Task6Build/` | Passed; 0 warnings, 0 errors |
| `dotnet test AlKhayal.slnx --no-restore -p:OutputPath=bin/Task6/` | Unit: 237 passed. Initial integration: 381 passed, 10 failed; see explanation below |
| `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --no-restore -p:OutputPath=bin/Task6Final/ --filter 'FullyQualifiedName~ReorderAlert\|FullyQualifiedName~Catalog\|FullyQualifiedName~InventoryPersistenceTests\|FullyQualifiedName~StockReportTests\|FullyQualifiedName~CustomerLedgerMigration\|FullyQualifiedName~RefundOverdraftMigration\|FullyQualifiedName~SupplierLedgerMigration\|FullyQualifiedName~TreasuryLegacyMigration'` | Final relevant integration: **112 passed, 0 failed** |
| `node frontend/tests/run-tests.js` | Passed, including new unified-alert tests and the existing 129-screen registry suite |

The initial full integration run found three task-related test/fixture mismatches: the availability scope assertion, the pre-FIFO fixture's new catalog-column prerequisite, and new multi-variant fixture mode/version setup. These were corrected and passed in the final 112-test run. The full suite was not rerun after those corrections.

The other **seven failures reproduce on the exact pre-task source snapshot** in a separate temporary checkout/build, confirming they predate this task:

1. `WorkforcePersistenceTests.Advances_and_monthly_payroll_post_atomic_treasury_withdrawals_and_immutable_allocations`
2. `WorkforcePersistenceTests.Attendance_opt_out_duplicate_checkout_correction_cross_midnight_overlap_and_audit`
3. `TreasuryApiTests.Workforce_http_module_flags_admin_payroll_privacy_csrf_cashier_ownership_and_replay`
4. `TreasuryApiTests.Authenticated_api_requests_are_logged_with_actor_time_path_and_outcome_and_logs_are_admin_only_immutable`
5. `TreasuryApiTests.Five_stock_reports_HTTP_independent_permissions_invalid_filters_and_read_only_GETs`
6. `TreasuryApiTests.Operational_reports_HTTP_independent_permissions_module_gates_overview_nonleakage_and_read_only`
7. `TreasuryApiTests.Net_profit_HTTP_permissions_filters_CSRF_classification_and_read_only`

Baseline commands used the same integration project in the saved temporary snapshot, `-p:OutputPath=bin/Task6Baseline/`, and two filters: `FullyQualifiedName~WorkforcePersistenceTests|FullyQualifiedName~Workforce_http_module_flags|FullyQualifiedName~Authenticated_api_requests_are_logged` (10 passed, 4 failed) and `FullyQualifiedName~Five_stock_reports_HTTP|FullyQualifiedName~Operational_reports_HTTP|FullyQualifiedName~Net_profit_HTTP` (3 failed).

The normal default-output build encountered DLL locks from the already running desktop. Separate output directories resolved this without stopping or restarting it. All database tests used isolated temporary SQLite files. No native visual/DPI/Windows 7/WebView2 runtime acceptance is claimed; the approved .NET Framework 4.8 / .NET Standard 2.0 stack remains unchanged.

## Execution and review

The project skill initially routed the backend batch to verified `opencode-go/kimi-k2.7-code` for its interconnected-file scope. That single call failed before implementation with `Go usage limit exceeded`. The user explicitly authorized direct Codex implementation. No alternate provider or billing/privacy setting was used. Implementation and independent diff/build/test review then ran in Codex. No implementation retry or model escalation occurred; actual model-token/quota measurements are unavailable.

A pre-task file snapshot and task-only diff were used to preserve/review existing work. Product/API/Application/Data, registry/permission compatibility, session-only boundaries and event invalidation were reviewed. No stock posting or financial writer logic was changed.

## Changed files for this task

- `docs/api-contracts.md`
- `docs/architecture.md`
- `docs/database.md`
- `docs/design.md`
- `docs/product-catalog.md`
- `docs/testing.md`
- `docs/unified-alerts.md`
- `frontend/css/layout.css`
- `frontend/data/screens.json`
- `frontend/index.html`
- `frontend/js/api-client.js`
- `frontend/js/app.js`
- `frontend/js/mocks/data.js`
- `frontend/js/modules/alerts.js`
- `frontend/js/modules/catalog.js`
- `frontend/js/modules/renderers.js`
- `frontend/js/modules/stock-reports.js`
- `frontend/js/services/access-state.js`
- `frontend/js/services/alerts-state.js`
- `frontend/tests/alerts-tests.js`
- `frontend/tests/run-tests.js`
- `src/AlKhayal.Api/ApiBootstrap.cs`
- `src/AlKhayal.Api/Controllers/AlertsController.cs`
- `src/AlKhayal.Api/Controllers/CatalogController.cs`
- `src/AlKhayal.Application/Alerts/AlertService.cs`
- `src/AlKhayal.Application/Catalog/CatalogContracts.cs`
- `src/AlKhayal.Application/Catalog/CatalogPorts.cs`
- `src/AlKhayal.Application/Catalog/CatalogService.cs`
- `src/AlKhayal.Application/Catalog/CatalogVariantContracts.cs`
- `src/AlKhayal.Data/Alerts/SqliteAlertStore.cs`
- `src/AlKhayal.Data/Catalog/SqliteCatalogConfigurationStore.cs`
- `src/AlKhayal.Data/Catalog/SqliteCatalogStore.cs`
- `src/AlKhayal.Data/Migrations/080_product_reorder_point.sql`
- `src/AlKhayal.Data/Reports/SqliteStockReportStore.cs`
- `src/AlKhayal.Server.Host/AlKhayalServer.cs`
- `tests/AlKhayal.IntegrationTests/CatalogMigrationTests.cs`
- `tests/AlKhayal.IntegrationTests/InventoryPersistenceTests.cs`
- `tests/AlKhayal.IntegrationTests/ReorderAlertApiTests.cs`
- `tests/AlKhayal.IntegrationTests/ReorderAlertTests.cs`
- `tests/AlKhayal.IntegrationTests/StockReportTests.cs`
- `tests/AlKhayal.UnitTests/ReorderPointTests.cs`
- `docs/qa/task-6-unified-alerts.md` (this report)
