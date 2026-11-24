# Stage 3 product, inventory and barcode QA

November 21, 2026. **PARTIAL.** The product directory and barcode manager now display the existing native single-label preview. Exact batch-page preview and native hardware qualification remain open. The full integration suite found a date-sensitive stock fixture failure.

## A. Issues reviewed

| Issue | Classification | Evidence and disposition |
| --- | --- | --- |
| Empty filtered product results gave only a generic next step | Reproduced and Fixed | The Arabic message now identifies filtered results. One button clears search, category and status and returns focus to search. |
| Empty unfiltered catalog and table actions | Already Fixed | The unfiltered catalog already had a first-product message. The existing table, pagination and action menu are preserved. |
| Product validation left focus away from the invalid field | Reproduced and Fixed | Edge reproduced the empty-name error with focus off the field. Both forms now focus the first field with an inline error and set its accessible invalid state. |
| Historical barcode manager null innerHTML error | Not Reproduced | The pre-change Stage 1/2 frontend opened the manager and displayed a saved barcode at all three viewports. |
| Directory print reported missing paper and lacked a preview | Reproduced and Fixed | The options API exposes stock in the profile's paper object. The old dialog looked for template stock. It now requests the native preview directly, waits for decode and printer validation, and keeps Print in the footer. |
| Manager preview Escape closed the whole manager | Reproduced and Fixed | Escape now closes its preview and restores focus to the invoking row. The UI also removes the profile copies hint because both product flows request exact quantities. |
| Batch and inline assignment summaries dropped saved paper | Reproduced and Fixed | Both consume the profile paper returned by the existing options API. Edge exercised the contextual batch route. |
| Categories, units and warehouse presentation | Not Reproduced | Edge exercised the relevant screens and stock dialogs. Existing frontend and integration suites exercised warehouse inputs and catalog data. No production warehouse or category/unit handler was changed for this stage. |
| Exact multi-copy and mixed-label page preview | Blocked | The saved-product preview endpoint accepts one barcode and renders its first page. The existing template preview uses sample data. Exact batch layout requires a different preview contract outside this stage's permitted scope. |
| Physical output and native WebView2 | Not Tested | Edge, recording backend and native PNG evidence are separate. No real job was sent. |

## B. Files changed

| File | Stage 3 correction |
| --- | --- |
| frontend/js/modules/catalog.js | Filter reset and focus after product validation. |
| frontend/js/modules/labels.js | Native preview loading in the directory and manager, printer and saved paper handling, stale-response guard, previewed IDs on submission, and paper summary. |
| frontend/css/modules.css | Fit and white image background for the preview and scoped hidden-image behavior. |
| frontend/tests/label-tests.js | Preview loading, saved IDs, paper and quantity, printer and paper failures, and stale unit response. |
| frontend/tests/stage3-browser.cjs | Edge browser assertions and screenshots with synthetic API responses. |
| tests/AlKhayal.UnitTests/LabelLayoutTests.cs | Exact native PNG dimensions and ZXing round-trip for complete labels. |
| tests/AlKhayal.IntegrationTests/LabelsApiTests.cs | Compare stored preview fields and configuration with the print input captured by a recording backend. |
| docs/product-barcode-label-printing.md | Updated operator workflow. |
| docs/testing.md and docs/catalog-stage3-qa.md | QA instructions, results, evidence and limits. |

## C. Barcode and label printing

- **Management.** Browser evidence covers opening with a saved code, keyboard preview and closing. The frontend suite covers copy, select/edit, save/retry, delete confirmation, and preventing generation from replacing typed data. Disposable integration tests cover generated Code 128, invalid and duplicate values, EAN-13, retirement, variants and POS lookup.
- **Shared source.** The browser displays the existing product-preview PNG. WindowsPrinterBackend uses NativeLabelRenderer for both layout and drawing in preview and submission. Preview draws at 300 DPI. Submission validates the driver's printable area and uses its resolution and hard margins. The rendered image reflects orientation. The UI displays the saved width and height and fits the page to the dialog in either theme.
- **Native sizes.** The existing dimension tests cover 30×20, 35×25, 40×30, 50×25, 50×30, 60×40, 73×37 and 100×50 mm plus A4 sheet layout. Stage 3 decodes 35×25, 50×30, 60×40 and 100×50 mm native pages, including a landscape case. Fixtures include a long Arabic name, a price, a long Code 128 and two EAN-13 values. The 35×25 mm PNG appears in the Edge fixture.
- **Output consistency.** The label HTTP test uses a recording backend to compare barcode, product name, unit, price, profile, printer, template and dimensions against the submitted job. It checks three requested labels and one backend copy. The current contract does not pin template or product revisions. Reopen the preview if settings or product details change while the dialog is open.
- **Feature and permissions.** The preview and print API require a session, effective barcode-label-printing module, eligible product-screen access for cashiers and CSRF validation. The contextual route uses products-list access. The server still validates printer and paper on submission.
- **Hardware.** Device alignment, physical orientation, print density, and scanner reads are **NOT TESTED**. The recording backend exercises output data without touching the Windows spooler. The inline editor remains a draft-safe print action. The batch remains a quantity-based print workflow without an exact rendered preview.

## D. Accounting and inventory protection

Reviewed git status, git diff --check, and the hunks in catalog.js, labels.js, modules.css, label-tests.js, LabelLayoutTests.cs and LabelsApiTests.cs. The baseline contained unstaged Stage 1/2 changes, including changes in catalog.js, modules.css and the desktop and host projects. Stage 3 preserved them. Added production hunks affect catalog presentation and label UI. The mixed catalog module retains the existing validation predicates, persistence payloads, price and reorder values, and conversion logic. Stage 3 changed no accounting calculations, financial transactions, product cost or profit formulas, inventory posting, migrations, production database records, barcode identity rules, or Windows printing-engine code. Tests use synthetic data, disposable SQLite files and a recording printer backend.

## E. Regression commands and results

| Command | Result |
| --- | --- |
| `node frontend/tests/run-tests.js` | **PASS for printed suites**. Exit 0. Named frontend suites reported success. The pre-existing runner still exited before its aggregate async completion line. |
| `node frontend/tests/label-tests.js` | **PASS**. Preview first, exact saved ID payload, unit switching, invalid/missing configuration, manager round trip, printer assignment and batch transport. |
| `node frontend/tests/inventory-tests.js` | **PASS**. Warehouse handlers, stock forms, unit/variant selection, count and source references. |
| `node frontend/tests/design-system-tests.js` | **PASS**. Stage 1 theme, contrast and persistence. |
| `node frontend/tests/stage2-browser.cjs` | **PASS**. Stage 2 routing, tabs, permissions, and cross-module route entry, with zero uncaught browser errors. |
| `node frontend/tests/stage3-browser.cjs` | **PASS**. Before and after Edge runs in Light and Charcoal Dark at 1024×768, 1366×768 and 1600×1000. Both runs asserted zero uncaught browser errors. |
| `dotnet build src/AlKhayal.UI/AlKhayal.UI.csproj -c Release --no-restore -m:1 -p:OutputPath=bin/Stage3/` | **PASS**. Desktop Release build, zero warnings and errors. |
| `dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj -c Release --no-restore -m:1 -p:OutputPath=bin/Stage3/` | **PASS**. 327 passed, zero failed or skipped. |
| `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj -c Release --no-restore -m:1` | **FAIL**. 493 passed, 1 failed, zero skipped. The expired-lot fixture failed before the focused integration assertions were added and the updated HTTP assertion passed in the focused run. |
| Focused Release integration command below | **PASS**. 18 passed, zero failed or skipped, including the updated preview-to-job comparison. |
| `dotnet test TheCashier.slnx -c Release --nologo --verbosity quiet` | **BLOCKED**. The selected SDK did not recognize the solution format. Direct project commands worked. |
| `git diff --check` | **PASS**. |

Focused integration command

```powershell
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj -c Release --no-restore -m:1 -p:OutputPath=bin/Stage3/ --filter "FullyQualifiedName~Labels_HTTP|FullyQualifiedName~LabelPersistenceTests|FullyQualifiedName~CatalogApiTests|FullyQualifiedName~InventoryApiTests|FullyQualifiedName~ReturnManagementTests" --logger "console;verbosity=minimal"
```

Cross-module evidence is split across layers. The Stage 2 runner opened POS, purchasing, returns, warehouse and product-related reporting routes and verified permissions. Frontend suites exercised POS unit selection, purchase product search, returns lookup and warehouse quantity and source links. The focused .NET integration run exercised the saved barcode-to-product mapping, product/card permissions, inventory API and returns persistence against disposable data. A manual checkout-to-return scenario in WebView2 and a real scanner pass are **NOT TESTED**.

## F. Screenshots and reproduction

The Edge runner loads the actual Stage 1/2 frontend before the Stage 3 changes or the current frontend after them. API responses are intercepted on a reserved hostname. Before screenshots came from the pre-change files or a temporary copy retaining Stage 1/2 and reversing only the Stage 3 source hunks. The label preview uses a genuine 300 DPI PNG produced by the native unit test. No screenshot in this set is native WebView2 or physical printer evidence.

The runner produced 108 before and 108 after screenshots. Files are in [artifacts/catalog-stage3](../artifacts/catalog-stage3/). Names use the phase, width and theme before the screen name. Each set includes products, no results, create, validation, edit, barcode manager, both preview entry points, categories, units, warehouse, count, adjustment, stock history, stock receipt and issue, printers and the contextual batch page.

- Directory preview [before](../artifacts/catalog-stage3/before-1024-light-label-preview.png), [after light](../artifacts/catalog-stage3/after-1024-light-label-preview.png) and [after dark](../artifacts/catalog-stage3/after-1366-dark-label-preview.png).
- Filtered empty state [before](../artifacts/catalog-stage3/before-1024-light-no-results.png) and [after](../artifacts/catalog-stage3/after-1024-light-no-results.png).
- Validation [before](../artifacts/catalog-stage3/before-1366-dark-product-validation.png) and [after](../artifacts/catalog-stage3/after-1366-dark-product-validation.png).
- Native PNGs for [35×25](../artifacts/catalog-stage3/stage3-label-35x25.png), [35×25 landscape](../artifacts/catalog-stage3/stage3-label-35x25-landscape.png), [50×30](../artifacts/catalog-stage3/stage3-label-50x30.png), [60×40](../artifacts/catalog-stage3/stage3-label-60x40.png) and [100×50](../artifacts/catalog-stage3/stage3-label-100x50.png).

Run the .NET unit command first, then copy the 35×25 fixture and run Edge. The frontend override and before phase are for an isolated pre-change frontend snapshot.

```powershell
New-Item -Type Directory -Force artifacts/catalog-stage3 | Out-Null
Copy-Item tests/AlKhayal.UnitTests/bin/Stage3/stage3-label-35x25.png artifacts/catalog-stage3/35x25.png
$env:THECASHIER_QA_PLAYWRIGHT = '<absolute path to playwright-core>'
node frontend/tests/stage3-browser.cjs
```

## G. Remaining issues

- **Blocked** — exact multi-copy and mixed-batch page previews require a preview contract capable of rendering submitted label lists. The current preview displays one saved label. Config or product changes made in parallel are not revision-pinned by the API.
- **Not tested** — native WebView2, Windows 7/Chromium 109 runtime, physical printer and scanner qualification. Preview uses standard image decoding supported by Chromium 109. Edge was the browser test target.
- **Failed** — full integration suite fixture for expiring lots. The test creates LOT-1 with a November 20, 2026 expiry and attempts to issue it using the running service on November 21. The stock rule correctly returns the expired-lot error. The restricted stock code and existing test fixture were preserved.
