# Stage 5 supplier QA

November 21, 2026. The Edge matrix uses the application frontend and synthetic API responses for repeatable timing. A separate Edge smoke test connected to the actual loopback OWIN host and a freshly migrated SQLite database. Both setups used synthetic records. The integrated HTTP and persistence assertions also used disposable SQLite fixtures.

## Workflow coverage

| Check | Result |
| --- | --- |
| Supplier directory | Leading-zero phone, long Arabic name, search, clearing, pagination, empty database and no matches. View targets the selected row. Delete confirmation names the supplier and Escape issues no delete. |
| Create and Edit | Initial input focus, first-invalid focus, required-field feedback, Tab, Escape and X, focus return including after a row refresh, failed Create retaining the entered phone, correct Edit values for A and B, and reopening. Synthetic Create, Edit and Delete succeed under a preserved directory search. The Create loading state blocks a repeat submission. Neither the historical `innerHTML` toast nor any uncaught error appeared. The live smoke test confirmed SQLite persistence of a browser Edit. |
| Supplier Accounts | Unselected state, loading guards, current selection, both statement and invoice content, supplier switching with delayed reads, report link and a missing detail lookup whose ID remains in the list. |
| Payment UI | Correct supplier identity, disabled confirmation while treasury loads, required treasury feedback and no POST from invalid input. A closed payment dialog stays closed when its delayed treasury response arrives. Existing handler tests exercise invoice type targeting, duplicate guard, submission and replay/error feedback. |
| Supplier report | Current payables and period payments render independently at scale 4. Report search, no matches, reset and supplier links were exercised in Edge. Period boundaries, pagination and persisted values are covered by operational report tests. |
| Purchase supplier selection | Leading-zero phone search, correct selected supplier and ID in the real purchase editor markup. Existing purchasing tests exercise keyboard selection, draft resumption, payload and stale responses. |
| Permissions and export | The browser exercise verifies that a cashier with directory access and without the account route grant cannot open Accounts. Export tests exercise filters, chooser bridge cancellation/save and loading using doubles. Integration tests cover real export generation and authorization. The native file chooser remains untested. |
| Theme, viewport and RTL | Light and Charcoal Dark at 1024×768, 1366×768 and 1600×1000. The runner checks RTL, viewport-level overflow and key focus transitions. Local table scrolling and the existing layout are preserved. |

Native WinForms/WebView2 interaction, native export chooser, DPI and Windows 7 Chromium 109 runtime qualification remain unverified. The normal desktop launch uses machine-local trial state independently of the database path, so the live smoke used the test host instead. Integrated payment posting, purchase posting and reopening persisted drafts were covered by automated tests, outside the Edge screenshot matrix. The restricted browser scenario covers the accounts route denial, with per-endpoint coverage supplied by integration tests where present.

## Regression commands

| Command | Result |
| --- | --- |
| `dotnet build src/AlKhayal.UI/AlKhayal.UI.csproj -c Release --no-restore -m:1` | Passed with 0 warnings and 0 errors. |
| `dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj -c Release --no-restore -m:1` | 327 passed, 0 failed, 0 skipped. |
| `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj -c Release --no-restore -m:1 --filter 'FullyQualifiedName~Supplier\|FullyQualifiedName~Purchase\|FullyQualifiedName~OperationalReport\|FullyQualifiedName~ExportPersistence'` | 70 passed, 0 failed, 0 skipped. |
| `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj -c Release --no-build -m:1` | 494 passed, 0 failed, 0 skipped. |
| `node frontend/tests/run-tests.js` | Exit 0. Output included supplier accounts, purchasing, operational reports, customer, export, theme and navigation passes. |
| `node frontend/tests/supplier-accounts-tests.js`, `design-system-tests.js`, `customer-statement-tests.js` and `export-tests.js` under `frontend/tests` | Passed. |
| `node frontend/tests/stage2-browser.cjs` | Passed, including shell contrast, workspace navigation and permission filtering. No page errors. |
| `node frontend/tests/stage4-browser.cjs` | Passed across the customer viewport and theme matrix. |
| `node frontend/tests/stage5-browser.cjs` | Before and after phase assertions passed across six combinations. |
| Live Edge browser smoke | Passed against a fresh isolated OWIN/SQLite host using the existing integration test license gate, random local credentials and synthetic supplier/debt invoice fixtures. Verified directory, persisted Edit, account statement/invoices, report payables and purchase supplier selection. No uncaught page errors. |
| `node --check` on both production files and the new runner, and `git diff --check` | Passed. Git reported existing line-ending warnings. |

## Screenshot evidence

The 122 before, 122 after and two live captures are in [artifacts/suppliers-stage5](../artifacts/suppliers-stage5/). Filenames identify phase, viewport, theme and workflow. They include the directory, Create, Edit, validation, deletion confirmation, no matches, empty state, account selection and loading, payment and validation, report, purchasing picker and restricted account access.

- [Directory](../artifacts/suppliers-stage5/after-1024x768-light-directory.png), [Create](../artifacts/suppliers-stage5/after-1366x768-dark-create.png), [Edit](../artifacts/suppliers-stage5/after-1366x768-dark-edit-2.png)
- [Edit focus before](../artifacts/suppliers-stage5/before-1600x1000-dark-edit-restored-focus.png), [Edit focus after](../artifacts/suppliers-stage5/after-1600x1000-dark-edit-restored-focus.png)
- [Account loading before](../artifacts/suppliers-stage5/before-1024x768-light-account-loading.png), [account loading after](../artifacts/suppliers-stage5/after-1024x768-light-account-loading.png), [missing supplier after](../artifacts/suppliers-stage5/after-1366x768-dark-missing-supplier.png)
- [Payment validation](../artifacts/suppliers-stage5/after-1366x768-dark-payment-validation.png), [report](../artifacts/suppliers-stage5/after-1600x1000-dark-report.png), [purchase picker](../artifacts/suppliers-stage5/after-1600x1000-dark-purchase-choices.png), [restricted account](../artifacts/suppliers-stage5/after-1600x1000-dark-restricted-account.png)
- [Live account](../artifacts/suppliers-stage5/live-1366x768-light-account.png), [live report](../artifacts/suppliers-stage5/live-1366x768-light-report.png)

To repeat the browser checks, use installed Edge and set `THECASHIER_QA_PLAYWRIGHT` to the Playwright Core package path if Node cannot resolve it by default. The baseline run accepts `THECASHIER_QA_STAGE5_BASELINE` pointing to a directory with `stage5-renderers-before.js`, `stage5-operational-reports-before.js` and `stage5-ui-before.js` from the starting working tree.

## FINANCIAL, ACCOUNTING AND DATABASE SAFETY

Compared each production file to the exact pre-edit snapshot with `git diff --no-index`. The renderer diff covers form focus, directory messaging and selected-account readiness and error states. The shared dialog diff adds fallback focus, and the report diff changes only column heading selection. These hunks contain no changes to accounting, balances, payment calculation or posting, supplier debt or settlement, purchase pricing or totals, reports aggregation, treasury or financial ledger logic, inventory posting, schema, migrations, financial persistence, authorization or licensing. The report still renders the backend's separate current-payable and period-payment values.

Reviewed the new browser source and documentation separately because ordinary `git diff` excludes untracked files. The baseline already contained unrelated edits, including desktop, host and Stage 4 renderer work.

### Initial working-tree status

```text
 M docs/design.md
 M docs/product-barcode-label-printing.md
 M docs/testing.md
 M frontend/assets/home-frame.svg
 M frontend/css/base.css
 M frontend/css/components.css
 M frontend/css/home.css
 M frontend/css/layout.css
 M frontend/css/login.css
 M frontend/css/modules.css
 M frontend/css/themes.css
 M frontend/css/tokens.css
 M frontend/data/screens.json
 M frontend/index.html
 M frontend/js/app.js
 M frontend/js/components/sidebar-flyout.js
 M frontend/js/components/ui.js
 M frontend/js/core/theme.js
 M frontend/js/login.js
 M frontend/js/modules/catalog.js
 M frontend/js/modules/labels.js
 M frontend/js/modules/purchasing.js
 M frontend/js/modules/renderers.js
 M frontend/js/modules/sales.js
 M frontend/login.html
 M frontend/tests/alerts-tests.js
 M frontend/tests/customer-statement-tests.js
 M frontend/tests/dashboard-tests.js
 M frontend/tests/delivery-tests.js
 M frontend/tests/label-tests.js
 M frontend/tests/quickbar-tests.js
 M frontend/tests/run-tests.js
 M frontend/tests/sidebar-flyout-tests.js
 M frontend/tests/workspace-tabs-tests.js
 M src/AlKhayal.Server.Host/AlKhayalServer.cs
 M src/AlKhayal.UI/ActivationForm.cs
 M src/AlKhayal.UI/ActivationSupport.cs
 M src/AlKhayal.UI/MainForm.cs
 M src/AlKhayal.UI/SplashForm.cs
 M src/AlKhayal.UI/TrialAccessForm.cs
 M tests/AlKhayal.IntegrationTests/AlertPersistenceTests.cs
 M tests/AlKhayal.IntegrationTests/LabelsApiTests.cs
 M tests/AlKhayal.UnitTests/LabelLayoutTests.cs
?? THE_CASHIER_UI_SCREENSHOT_INDEX.md
?? THE_CASHIER_UI_UX_AUDIT.md
?? UI_AUDIT_SCREENSHOTS/
?? docs/catalog-stage3-qa.md
?? docs/customers-stage4-defect-register.md
?? docs/customers-stage4-implementation.md
?? docs/customers-stage4-qa.md
?? docs/design-system-stage1-coverage.md
?? docs/design-system-stage1-implementation.md
?? docs/design-system-stage1-qa.md
?? docs/design-system-v1.md
?? docs/layout-navigation-stage2-coverage.md
?? docs/layout-navigation-stage2-implementation.md
?? docs/layout-navigation-stage2-qa.md
?? docs/layout-navigation-stage2.md
?? frontend/assets/fonts/
?? frontend/assets/home-frame-dark.svg
?? frontend/tests/design-system-tests.js
?? frontend/tests/stage2-browser.cjs
?? frontend/tests/stage3-browser.cjs
?? frontend/tests/stage4-browser.cjs
```
