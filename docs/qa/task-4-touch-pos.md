# Task 4 — Touch POS product order and relevant categories

Implemented 2026-11-01. No commit/push, application launch, operator database access or licensing-data change. Existing uncommitted changes were snapshotted before edits; runtime sales UI/CSS and stock-floor changes are preserved.

## Behavior and design

- Touch POS pages sort by **all-time net actual sold base-unit quantity**, descending, then product name and ID. Saved invoice quantities and original unit conversions determine quantity, including zero-stock sales. Financial returns subtract their requested quantity regardless of restored physical stock. Cancellation tombstones exclude canceled sales. No-sales products retain deterministic name/ID ties.
- Additive migration 078 backfills `ProductPopularity` once. Three derived-projection triggers maintain it in the same SQLite transaction as posting, return lines and cancellation. Existing operation keys prevent duplicate events; late-failure rollback also rolls back popularity. No financial, stock or authorization policy is moved into SQL.
- POS categories use `EXISTS` with the same eligibility predicate as product pages: active, unarchived, ready stock product, active/unarchived variant and active sale unit. Categories themselves must be active. Empty and ineligible-only categories are hidden. Stock quantity is deliberately absent from eligibility.
- The existing renderer remains intact: `كل المنتجات` initially selected; category selection and ID-derived colors survive search/page changes; 40-product server pages, live search, load more, stale-response protection and RTL layout remain. General catalog product/category administration is unchanged.

## Changed files for this task

Production:
- `src/AlKhayal.Data/Migrations/078_pos_product_popularity.sql` (new)
- `src/AlKhayal.Data/Catalog/SqliteCatalogStore.Pos.cs` (new eligibility/category query)
- `src/AlKhayal.Data/Catalog/SqliteCatalogStore.cs`
- `src/AlKhayal.Application/Catalog/CatalogPorts.cs`
- `src/AlKhayal.Application/Catalog/CatalogService.cs`
- `src/AlKhayal.Api/Controllers/InventoryController.cs`

Regression coverage:
- `tests/AlKhayal.IntegrationTests/TouchPosBrowseTests.cs` (new)
- `tests/AlKhayal.IntegrationTests/SalesCancellationValuationTests.cs` (all-time ordering expectation)
- `tests/AlKhayal.IntegrationTests/{CatalogMigrationTests,InventoryPersistenceTests,CustomerLedgerMigrationPreflightTests,SupplierLedgerMigrationTests,TreasuryLegacyMigrationTests,RefundOverdraftMigrationTests}.cs` (defer 078 in historical fixtures; capture counts for failed upgrades rather than obsolete hardcoded counts)
- `tests/AlKhayal.UnitTests/CatalogServiceTests.cs` (port fake)
- `frontend/tests/run-tests.js` (production renderer assertions)

Documentation: this report, `docs/api-contracts.md`, `docs/database.md`, `docs/testing.md`.

## Validation

Build/test output and distinct TRX files: `artifacts/task4/`. All write-capable fixtures use unique temporary paths; a separate temporary `ALKHAYAL_DATABASE_PATH` fallback was set for Codex integration runs. No host was started. Licensing test classes were excluded.

Commands:

```powershell
dotnet restore AlKhayal.slnx --artifacts-path artifacts/task4 -v:q
dotnet build AlKhayal.slnx --no-restore --artifacts-path artifacts/task4 -v:q
node frontend/tests/run-tests.js
node frontend/tests/touch-pos-layout-tests.js
dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --no-restore --artifacts-path artifacts/task4 --filter 'FullyQualifiedName~CatalogServiceTests' -v:q
```

The integration filter includes `TouchPosBrowseTests`, `SalesCancellationValuationTests`, six historical-migration/InventoryPersistence fixture classes listed above, `SalesPersistenceTests`, `SalesStockFloorTests`, `PricingFifoInvariantTests` and `CatalogApiTests`; command uses `--no-restore --artifacts-path artifacts/task4 --logger 'trx;LogFileName=task4-final.trx' --results-directory artifacts/task4/results -v:q`.

Final results:

| Check | Result |
|---|---|
| Solution build (serial final run) | Passed, 0 warnings/errors |
| Relevant integration suite | 126 passed, 0 failed/skipped; `results/task4-final.trx` |
| Catalog service unit tests | 4 passed, 0 failed/skipped; `results/task4-unit.trx` |
| Frontend registry/behavior | Passed, 129 routes |
| Existing headless Edge POS layout check | Passed, 72 cases (six viewport sizes, empty/single/40-line carts, cash/card, light/dark) |
| JavaScript syntax and `git diff --check` | Passed |

Initial draft errors and fixture counts were corrected, not hidden. A simultaneous final build encountered a testhost file lock; it was stopped and rerun serially after tests. This is a relevant suite, not a full solution test-suite claim; licensing tests were excluded.

## Performance evidence and limits

Disposable synthetic read benchmark: **15,000 eligible products and 30,000 saved sale lines**. Sale lines are built under draft invoices, then transitioned to posted to populate the actual projection triggers. The bulk fixture is for browsing; it does not claim complete accounting/FIFO import validation. Smaller tests exercise real service lifecycle flows, original converted units, financial shortfall returns, replay, rollback, genuine 078 recreation/backfill and repeated startup.

First measured run: first 40 products **77.150 ms**, deep page 375 **156.137 ms**, substring search **103.042 ms**, category page **73.863 ms**. These are elapsed measurements on this development machine, without strict timing assertions. Final-run measurements under the broader regression run: first page **89.555 ms**, deep page **240.936 ms**, search **186.910 ms**, category **107.095 ms**. Full output is retained in the final TRX.

The query plan uses existing variant/unit indexes and `SEARCH pop USING INTEGER PRIMARY KEY`; it contains no sales/returns ledger reads. A temporary B-tree sorts the catalog candidates by quantity/name/ID. The projection has only its useful product primary key, avoiding an unused rank index and associated write overhead. Counts, substring `LIKE`, candidate sorting and `OFFSET` still scale with catalog size; this is bounded server response pagination, not constant-time/keyset pagination. A user can progressively request more pages; the initial catalog is never downloaded in full. Ranking can change between separate page calls when sales change concurrently; no cross-request snapshot is promised.

Migration 078 scans historical sales/returns once and can take longer on very large histories. No target-hardware startup/backfill benchmark, million-product benchmark, concurrent-terminal load test or Windows 7/Chromium 109 native WebView2 qualification was run. Existing .NET Framework 4.8/.NET Standard 2.0 and Chromium 109-compatible JavaScript boundaries remain. Headless current-Edge layout checks do not establish legacy-runtime compatibility.

## Implementation routing

Applied `.codex/skills/alkhayal-opencode/SKILL.md`. Verified CLI models/help. `opencode-go/kimi-k2.7-code` was chosen as the verified multi-file candidate; its initial projection/category work required independent corrections and its test draft had compile/fixture errors. One targeted same-model correction was followed by a capability escalation to verified `opencode-go/glm-5.3`, which immediately returned **Go usage limit exceeded** before changes. Codex disclosed the quota failure and completed corrections, tests and documentation directly. No provider/billing/credential/privacy settings changed. Exact model token usage and Codex Plus usage are unavailable; no cost/savings claim. The ignored `.codex/opencode-log.md` records calls; baseline and review work files are retained under `artifacts/task4/`.


## Preservation check

The 20 task-changed paths above were reviewed against the initial working-tree snapshot (not just HEAD). Existing retired-attribute, stock-floor, layout and other uncommitted changes remain. Pre-existing files outside the eight overlapping task paths are byte-identical to their baseline. Baseline copies/patch and temporary review scripts were moved into ignored `artifacts/task4/baseline` and `artifacts/task4/work-files`. No source-file deletions, commits or pushes occurred.
