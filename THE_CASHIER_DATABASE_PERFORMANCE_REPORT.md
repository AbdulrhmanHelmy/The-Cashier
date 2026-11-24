# The Cashier database performance report

## Sales and purchase invoice history

Sales history requested a list capped at 500 and waited for customer and POS setup before rendering. The existing SQL ranked every candidate with a conditional sort expression, even in the unranked history call. Opening a detail already used its own endpoint. Purchase history also stopped at 500. Both lists could omit older records.

Sales history now loads immediately and requests 50 headers by default, with 25, 100, and 200 as alternatives. SQLite applies the search and visibility predicates to the complete population, counts the matches, selects the page IDs in descending order, then projects the headers and return badges. The count and items share a read transaction. The UI debounces searches by 180 ms, rejects superseded results, and requests only the current page after a cancel or deletion. Navigation includes the first and last pages, adjacent pages, a bounded page-number window, and the displayed range.

Purchase history uses 50 rows per page with previous and next controls. Its matching count and results share a snapshot. Migration 101 adds a covering invoice date and ID index for the cross-supplier browse order. The legacy list, detail, and ranked lookup contracts remain compatible. Page requests omit invoice line and product detail loading.

Exports now read the SQL result into the export row list directly, eliminating an additional full-size buffer of Dapper rows. The same filters and complete result scope feed the writers. The export contract and final writer still buffer output.

## SQLite measurements

These are single service-call wall-clock samples from disposable migrated SQLite databases, with valid user and warehouse relationships and draft invoice headers. Times include open, query, and materialization. Values may vary with runtime scheduling and disk cache. They do not measure native screen rendering.

| Sales rows | Legacy list, ms | First page, ms | Next page, ms | Last page, ms | Search for oldest, ms |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1,000 | 29.53 | 13.56 | 9.87 | 30.00 | 16.87 |
| 10,000 | 105.89 | 22.00 | 16.44 | 27.79 | 50.66 |
| 50,000 | 463.15 | 46.41 | 28.89 | 82.97 | 150.07 |
| 100,000 | 737.11 | 60.93 | 55.92 | 89.01 | 194.12 |
| 500,000 | 4,563.06 | 333.21 | 315.05 | 607.71 | 1,551.63 |

The old list issued one invoice SELECT and materialized 500 headers. Each nonempty sales page issues a count and a bounded header SELECT, materializing 50 headers at the default size. Empty sales results issue only the count within the read transaction. The default therefore reduces header row materialization by 90 percent for these datasets. Process memory, UI responsiveness, and SQLite-only execution time were not measured independently. The purchase change has no comparable timing series.

`EXPLAIN QUERY PLAN` showed a rowid-ordered scan for the sales candidate query with indexed probes into draft deletions and corrections and no temporary sort for that query. Sales did not need an additional index for its existing order. Exact counts and contains searches must examine matching candidates, and OFFSET may visit earlier rows on deep pages. Separate page requests see the current database and can shift between requests if invoices change.

## Application audit and outstanding work

This was a targeted source audit of the following data paths. Several confirmed issues remain open.

| Area reviewed | Finding and status |
| --- | --- |
| Sales and purchase invoice directories | Database paging implemented. Legacy array endpoints still serve compatible consumers. |
| Sales and purchase returns | The unified invoice picker pages in SQL. The legacy returns renderers still read capped invoice arrays, and return history has a capped list path. Inspect active route coverage before changing those callers. |
| Catalog, POS and purchase product selection | The catalog and product pickers already request bounded database pages. POS adds batches as needed. Product detail requests remain separate. Stress measurements for these workflows are open. |
| Inventory balances and stock movements | Database paging exists. Inventory count history uses a fixed cap. |
| Customers, suppliers and account statements | Supplier directory and customer lookup have bounded APIs. The customer directory, several customer selection screens, and some customer and supplier statements still fetch complete collections. These paths require further work. |
| Financial transactions, expenses, revenues, treasury | Transaction reports use SQL pages and aggregation. Expense vouchers have a capped history and some account and movement readers require follow-up. |
| Delivery orders, employee management and activity | Delivery orders and shift activity page in SQL. Workforce advance, payroll, and audit history still contain collection readers that need separate performance and UI work. |
| Operation log and Reports Center | Operation log and multiple operational, stock, sales and financial reports already page in SQL. Report totals are computed separately from page rows. This review did not profile every report or prove an absence of scans or repeated work. |
| Global search and exports | Bounded global-search providers already exist. Export reads remove the duplicate source buffer. End-to-end streaming and export memory measurements remain open. |

## Files changed

- Sales API, service, contracts and SQLite store in `src/AlKhayal.Api/Controllers/SalesController.cs`, `src/AlKhayal.Application/Sales/`, and `src/AlKhayal.Data/Sales/SqliteSalesStore.cs`.
- Purchase API, service, core model and port, store and migration in `src/AlKhayal.Api/Controllers/PurchasesController.cs`, `src/AlKhayal.Application/Purchasing/PurchaseService.cs`, `src/AlKhayal.Core/Models/PurchaseInvoice.cs`, `src/AlKhayal.Core/Interfaces/IPurchaseRepository.cs`, `src/AlKhayal.Data/Purchasing/SqlitePurchaseRepository.cs`, and `src/AlKhayal.Data/Migrations/101_purchase_invoice_paging.sql`.
- Export materialization in `src/AlKhayal.Data/Exports/SqliteExportDataSource.cs`.
- History UI in `frontend/js/modules/sales.js`, `frontend/js/modules/purchasing.js`, and `frontend/css/modules.css`.
- Regression coverage in `tests/AlKhayal.IntegrationTests/SalesInvoicePaginationTests.cs`, `frontend/tests/run-tests.js`, `frontend/tests/invoice-return-badges-tests.js`, and `frontend/tests/touch-pos-invoice-tests.js`.
- Contract, migration and verification notes in `docs/api-contracts.md`, `docs/database.md`, and `docs/testing.md`.

## Compatibility and validation limits

Tests used disposable databases. The new routes share the existing authentication checks and API envelope. The UI targets the existing WebView2 browser APIs. Native desktop interaction, Chromium 109, actual native printing, concurrent stress, and posted data at the largest scale require separate validation. The application-wide optimization remains incomplete.

## Executed checks

- `dotnet build src/AlKhayal.UI -c Release --no-restore` passed with zero warnings and errors. This built the WinForms application and its data, API, printing and host dependencies.
- The installed .NET 8 SDK could not parse the solution-level `.slnx` command. The project-level Release build above succeeded.
- `dotnet test tests/AlKhayal.IntegrationTests -c Release --no-restore --filter 'FullyQualifiedName~SalesInvoicePaginationTests|FullyQualifiedName~ExportPersistenceTests|FullyQualifiedName~TouchPosInvoiceEditingTests'` passed 36 tests. The Release loopback host authenticated an administrator, served the page endpoints, and verified that the original array routes still respond. It also verified that the purchase candidate query uses the migration 101 index and can reach invoice 1 from a 551-row dataset.
- `node frontend/tests/run-tests.js` passed for 132 registered screens. It exercised mounted history search, stale-response rejection, return badges, navigation, and existing export and PDF action wiring.
- `git diff --check` passed.
- `dotnet test tests/AlKhayal.UnitTests -c Release --no-restore` passed all 328 unit tests.

### Phase 2 history changes

Return history and the return detail selector now request at most 50 records per page by default. Both query sales and purchase returns, search the full matching history, and keep the selected return available when paging the selector. The two legacy sales and purchase return routes already redirect to the paged management route.

Expense voucher history now has 50-row SQLite pages and previous and next navigation. Exact-ID links from global search continue to use the existing detail endpoint. No migration was needed for these changes.

Unfiltered sales invoice pages now count all invoice IDs and subtract the IDs excluded by draft deletion or correction. The subtraction avoids double counting an ID represented in both sets. Filtered queries continue to evaluate search and visibility together. This reduces the work of the unfiltered count when exclusions are sparse. Deep OFFSET and contains-search work remain.

The 500,000-invoice Phase 2 test recorded 215 ms for page 1, 195 ms for page 2, 483 ms for the last page and 1,226 ms for the oldest-invoice search. The previous runs on the same test workload recorded 333–435 ms for page 1. These were separate runs with different concurrent workloads, so the difference is an observation and does not isolate the effect of the count change.

Changed files in this phase include `ReturnService.cs`, `ReturnsController.cs`, `SqliteReturnReadStore.cs`, `returns.js`, `returns-tests.js`, `ReturnManagementTests.cs`, `ExpenseService.cs`, `ExpensesController.cs`, `SqliteExpenseStore.cs`, `expenses.js`, `expense-tests.js`, `ExpensePersistenceTests.cs`, `SqliteSalesStore.cs`, and `docs/api-contracts.md`.

### Phase 2 completion status

- Returns management and detail selector. Database paging implemented. Sales and purchase return fixtures and frontend tests passed.
- Sales and purchase compatibility return routes. Registry redirects to returns management. The frontend registry test passed.
- Sales history count. Optimized and tested at 1,000, 10,000, 50,000, 100,000 and 500,000 invoices. The focused sales and returns integration run passed 14 tests.
- Expense voucher history. Database paging implemented. The frontend suite and Release desktop build passed.
- Customer and supplier management and account histories, workforce, inventory, delivery, product workflows, treasury, financial transactions, logs and reports. Further audit, implementation and scale verification remain open. The frontend suite covering 132 registered screens does not qualify their performance.
- Native WebView2, memory and physical printing. The desktop database and browser profile paths can be redirected, but trial startup can still write the current installation's trial security files and registry. Native verification requires an isolated Windows environment with independent licensing and a printer for physical output. No native performance or memory measurements were recorded.

A separate query-only comparison used Node SQLite 3.54.1 on the same in-memory 500,000-row fixture for both statements, with 2,000 deletions and 1,997 corrections, including overlaps. Both returned 497,005 visible invoices. Over seven alternating runs, median count time was 550.26 ms with the original visibility probes and 4.13 ms with the final count query. The final plan scans the sparse exclusion sets first and uses indexed invoice and deletion lookups. This isolated result does not measure the production provider, file I/O, or page rendering.

The Release unit suite passed all 328 tests. The first full integration run discovered migration test fixtures that applied migration 101 to intentionally older databases without `PurchaseInvoices`. The customer and supplier migration fixtures now defer that migration along with the purchasing schema. Eleven focused expense and migration tests passed after the fixture correction, including retrieval of the oldest voucher from a 201-voucher history.

The final sales query passed all six focused integration tests and the final Release desktop build passed with zero warnings or errors. In the final 500,000-invoice production-provider sample, the first page took 68.47 ms, the next page 32.27 ms, the last page 514.64 ms, and the oldest-invoice search 1,395.31 ms. The file-backed integration samples remain sensitive to concurrent workload.

The full integration run used a build from before the migration fixture corrections and final sales count plan. A complete post-fix integration run remains unverified.

## Phase 3 customer collection work

The customer directory used an unrestricted list followed by browser search, sorting and slicing. Its active UI now requests a SQL page after a debounced search, resets the page on a changed sort or page size, and preserves export scope. Customer creation no longer loads the directory at startup or after the POST. The legacy edit route still has a full-list selector.

The active customer statement requests a page of movements. SQLite computes the period debit and credit, opening balance, current balance and the amount preceding the page in one read snapshot. The endpoint reuses the legacy movement mapping. It uses the existing CustomerId, OccurredOn and Id index and adds no migration. A 10,000-movement test compares the page's running balances and all period totals to the existing unpaged implementation on the same data. A separate 10,000-customer test verifies a 50-row first page, reverse sort, and a literal percent search reaching the last record. Focused integration tests for the customer changes passed. The frontend suite covering 132 screens passed.

The changed source files are the customer models and repository interfaces, CustomerService, CustomerAccountService, CustomersController, both SQLite customer repositories, and the shared entity and customer statement renderer in `frontend/js/modules/renderers.js`. Tests changed in `CustomerStatementTests.cs`, `GlobalSearchApiTests.cs`, `run-tests.js`, and `customer-statement-tests.js`.

The [global performance checklist](THE_CASHIER_GLOBAL_PERFORMANCE_CHECKLIST.md) inventories all 132 routes and records known implementation and verification gaps. The customer statement selector, supplier account histories, workforce history and other open data paths still require implementation. Native browser measurements and print verification remain open. No native launch was performed against production licensing or data.

### Phase 3 workforce history

The advances and payroll workflows now retrieve 50-row SQLite history pages. The old payroll UI inferred the outstanding advance total and whether the selected period was already paid from two capped histories. The new endpoint reads those values from the complete employee records in the same snapshot as the page. It reuses the posting path's unsettled advance and period predicates. The employee selectors still cap their options and are open.

Changes are in `WorkforceContracts.cs`, `WorkforceService.cs`, `WorkforceController.cs`, `SqliteWorkforceStore.cs`, and `frontend/js/modules/workforce.js`. Regression coverage lives in `WorkforcePersistenceTests.cs`, `WorkforceServiceTests.cs`, and `workforce-tests.js`. No migration was added.

### Phase 3 verification

- The latest focused integration run passed all 24 tests covering customer directory and statements, the workforce persistence changes, authenticated loopback requests and standalone first-run startup. The customer search fixture verifies Arabic letter variants, diacritics and a literal percent sign. The 501-row payroll fixture verifies that the oldest month is reachable and the paid-period check is independent of the visible page.
- The unit run after the workforce changes passed all 328 tests on retry. An earlier native activation dialog test failed transiently when Windows rejected a clipboard operation.
- The frontend suite passed for all 132 registered screens. The final desktop Release build passed with zero warnings and errors in disposable output because the current desktop instance held the normal build files open.
- The full integration run was started against the latest build. Full results and application-wide stress and native validation remain open.
