# Stage 4 customer QA

November 21, 2026. Native desktop acceptance remains open.

## Historical and current evidence

Reviewed `THE_CASHIER_UI_AUDIT_PACKAGE.zip`, the audit and screenshot index, and the six archived customer screenshots. SHA-256 checks matched all eight unpacked evidence files to their archive entries. The older Create and Edit screenshots show a null `innerHTML` toast. Both current dialogs opened repeatedly in the pre-change and post-change Edge runs without that toast or an uncaught browser exception.

The Edge runner serves actual frontend files on a reserved test hostname and intercepts API calls using synthetic records. The before run uses a temporary copy of the frontend taken before production edits, including the work already present for Stages 1–3. The browser scenarios never contact a live API or database. The .NET integration runs use their existing disposable fixtures.

## Browser and workflow checks

| Check | Observed result |
| --- | --- |
| Directory | Populated list, paging, long Arabic name, leading-zero phone, phone search, clearing search, empty and no-match messages, refresh, row-to-statement context and delete cancellation. The browser assertion checks document width without disabling table-level scrolling. |
| Customer forms | Repeated opens across workspace navigation, correct persisted Edit code and phone, Create initial focus, Edit initial focus, keyboard Tab wrap and Escape, focus restoration to Create, inline required validation, preserved secondary phone, and a rejected save retaining the entered phone. Successful persistence and generated codes are covered by `CustomerPersistenceTests`. |
| Statement and account dialogs | Customer selection and report-to-statement ID, exact fixture balance formatted by the existing formatter, movement reference and receipt control presence, collection and increase dialog layout, and delayed collection after a customer switch. An unavailable customer from an old report link shows an explicit prompt and keeps actions disabled. Focused JS covers dates, invalid ranges, retry, late renders, input validation, duplicate submission, definitive versus uncertain failure and statement refresh. |
| Collection and increase persistence | `CustomerStatementApiTests` uses the existing HTTP routes, permissions and CSRF, and observes persisted financial results and same-operation replay against disposable SQLite. The browser screenshot harness does not post collections or debt. |
| Customer report | Current balance and period collection figures displayed independently at their supplied precision. Search, no matches, reset and row-to-statement navigation exercised in Edge. Operational report tests cover periods, pagination, permissions and returned totals. |
| Export | Existing frontend export tests exercise format choice, native-bridge cancellation and save through a double, loading, duplicate guard and filters. `ExportPersistenceTests` checks synthetic export data, leading-zero contacts and authorization. |
| Related workflows | Stage 2 Edge runner checked workspace tabs, route permissions, a dirty purchase form, customer quick action, POS, sales, returns, installments and reporting route entry. Stage 3 Edge runner and focused label and inventory frontend tests covered the product/barcode and warehouse UI. |
| Themes and viewports | All six combinations of Light and Charcoal Dark at 1024×768, 1366×768 and 1600×1000. The matrix checks RTL, an unoverflowed document, and modal keyboard interaction. Screenshots show the existing scrollable table and page layouts. |

Native WebView2, DPI and Windows 7 Chromium 109 runtime qualification, the actual native export chooser and receipt output remain untested. A different database path alone does not isolate a desktop launch because trial startup also uses machine-local files and registry state. This run used Edge and disposable SQLite test fixtures. Changes use the existing dialog API and standard JavaScript features already supported by the application's Chromium baseline. The isolated tests do not establish a full native cashier/view-only walkthrough or a live checkout-to-return workflow.

## Regression commands

| Command | Result |
| --- | --- |
| `dotnet build src/AlKhayal.UI/AlKhayal.UI.csproj -c Release --no-restore -m:1 -p:OutputPath=bin/Stage4/` | 0 warnings, 0 errors. |
| `dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj -c Release --no-restore -m:1 -p:OutputPath=bin/Stage4/` | 327 passed, 0 failed, 0 skipped. |
| `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj -c Release --no-restore -m:1 -p:OutputPath=bin/Stage4/ --filter "FullyQualifiedName~Customer\|FullyQualifiedName~OperationalReport\|FullyQualifiedName~ExportPersistence"` | 58 passed, 0 failed, 0 skipped. |
| `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj -c Release --no-restore -m:1` | Full integration suite passed. 494 passed, 0 failed, 0 skipped. |
| `node frontend/tests/run-tests.js` | Exit 0 with named suites reporting pass. The existing runner did not print an aggregate completion marker. |
| `node frontend/tests/customer-statement-tests.js`, `customer-language-tests.js`, `design-system-tests.js`, `label-tests.js`, `inventory-tests.js`, `export-tests.js` (under `frontend/tests`) | Focused frontend checks exited 0. |
| `node frontend/tests/stage2-browser.cjs` and `node frontend/tests/stage3-browser.cjs` | Passed. |
| `node frontend/tests/stage4-browser.cjs` | Before and after passed with phase-specific expectations and zero uncaught browser errors. |
| `node --check frontend/js/modules/renderers.js` and `git diff --check` | Exit 0. Existing working-tree files emitted Git line-ending warnings. |

The first full integration run reused the custom build output and finished with 493 passed, 1 failed and 0 skipped. `StandaloneFirstRunApiTests.Fresh_host_serves_Cashier_login_and_persists_bootstrap_and_Arabic_catalog_after_restart` failed at `tests/AlKhayal.IntegrationTests/StandaloneFirstRunApiTests.cs:39` with HTTP 404. The fixture resolves `frontend` relative to the normal build directory. The custom output was one level shallower. A targeted rerun with the standard path (`-c Release --no-build --filter FullyQualifiedName~StandaloneFirstRunApiTests`) passed without requiring a code change.

## Screenshot evidence

There are 90 before and 90 after Edge screenshots under [artifacts/customers-stage4](../artifacts/customers-stage4/). Each combination has directory, empty directory, no matches, Create, Edit, validation, rejected save, delete confirmation, statement, collection, add debt, delayed collection, unavailable requested customer, report and scrolled report table. The path format is `<phase>-<width>x<height>-<theme>-<screen>.png`.

- [Directory](../artifacts/customers-stage4/after-1024x768-light-directory.png), [empty before](../artifacts/customers-stage4/before-1024x768-light-empty-directory.png), [empty after](../artifacts/customers-stage4/after-1024x768-light-empty-directory.png), [no matches](../artifacts/customers-stage4/after-1024x768-light-no-matches.png).
- [Create before](../artifacts/customers-stage4/before-1366x768-dark-create.png), [Create after](../artifacts/customers-stage4/after-1366x768-dark-create.png), [Edit](../artifacts/customers-stage4/after-1366x768-dark-edit.png), [validation](../artifacts/customers-stage4/after-1366x768-dark-validation.png), [delete confirmation](../artifacts/customers-stage4/after-1366x768-dark-delete-confirmation.png).
- [Stale dialog before](../artifacts/customers-stage4/before-1024x768-light-stale-collection.png), [same scenario after](../artifacts/customers-stage4/after-1024x768-light-stale-collection.png), [statement](../artifacts/customers-stage4/after-1600x1000-dark-statement.png), [collection](../artifacts/customers-stage4/after-1600x1000-dark-collection.png), [add debt](../artifacts/customers-stage4/after-1600x1000-dark-add-debt.png), [report](../artifacts/customers-stage4/after-1600x1000-dark-report-table.png).
- [Unavailable customer before](../artifacts/customers-stage4/before-1024x768-light-missing-customer.png), [same scenario after](../artifacts/customers-stage4/after-1024x768-light-missing-customer.png).
- [Rejected save with entered values retained](../artifacts/customers-stage4/after-1366x768-dark-save-failure.png).

Use installed Edge and set `THECASHIER_QA_PLAYWRIGHT` to the path to `playwright-core` if it is outside Node's normal resolution path. For the baseline run, set `THECASHIER_QA_PHASE=before` and `THECASHIER_QA_FRONTEND` to a copy of the pre-Stage-4 frontend. Remove the two overrides for the after run.

## FINANCIAL AND DATABASE CODE SAFETY

Inspected the baseline `git status --short`, final status, diff statistics and the full Stage 4 production and test diff. The working tree already contained modifications to the host, desktop shell, frontend and earlier-stage tests and documentation. Stage 4 production hunks are confined to modal focus, directory messages, delete confirmation copy, statement customer selection and the pre-dialog context guard in `frontend/js/modules/renderers.js`.

The Stage 4 diff contains no changes to accounting algorithms, balance or debt calculations, collection and payment posting, ledgers, transaction processing, sales and invoice calculations, report aggregations, inventory posting, SQL, schema or migrations. The guard runs before opening the dialog. The request payload, replay key behavior, validation, monetary rendering and success reload remain the existing code. No production database or financial history was used for verification.
