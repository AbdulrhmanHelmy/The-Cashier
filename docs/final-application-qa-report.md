# Final application QA report

**November 21, 2026. Overall QA is PARTIAL.** The Release desktop built with zero warnings and errors. The registry contains 132 routes, including 40 compatibility redirects and 56 hidden entries. Edge verified 66 visible implemented destinations in Light and Charcoal Dark at 1024×768, 1366×768 and 1600×1000. Each matrix entry checked navigation, RTL and horizontal document bounds. Registry tests cover metadata, route and redirect validity. Contextual hidden routes are outside the generic Edge navigation matrix.

## Whole-application route coverage

| Surface | Current evidence |
| --- | --- |
| Home, shell, alerts, operations and access | Route and RTL smoke, with frontend tests for navigation, permissions, alerts, operation logs, shortcuts and workspace tabs. |
| Catalog, product setup, barcodes, units, warehouse and inventory | Route smoke plus automated product, label transport, inventory, paging, quantity and price tests. |
| Customers, statements, collection, suppliers, supplier accounts and purchasing | Route smoke plus frontend record, search, statement, invoice resume and stale-response tests. |
| Returns, treasury, expenses, payment overview, wallets, terminals and history | Route smoke plus frontend record and contextual-navigation tests. |
| Employees, payroll, advances, attendance, shifts, installments and maintenance | Route smoke plus retained workflow and request-state tests already in the frontend suite. |
| Reports Center | All 18 report routes opened, including the overview. Existing report suites cover supported API-driven filters, paging, totals and detail links. |
| General/company/appearance, printers, users and grants, backup, business activities and modules | Available settings routes opened through the Settings hub. Recovery, native dialogs, printing and all permission combinations require further qualification. |
| POS, Sales, Delivery Orders and drivers | Matrix, new focused browser reproduction and existing automated regressions. See [module QA](final-stage-pos-sales-delivery-qa.md). |

The ten visible unfinished destinations are `profit-sales-report`, `profits`, `analytics-sales`, `analytics-purchases`, `analytics-suppliers`, `tracking-reports`, `branches-list`, `branch-stock-transfer`, `branch-finance`, and `cash-services`. Manufacturing is a hidden unfinished destination. The matrix checked shell navigation with generally empty fixtures. [396-entry Edge log](../artifacts/final-stage/edge-matrix-final.txt) · [Route inventory and feature keys](../artifacts/final-stage/route-inventory.tsv)

## Automated results

| Check | Result |
| --- | --- |
| Release WinForms build | Passed with zero warnings and errors. A repeat in the normal output folder met a lock held by an existing Cashier window. The isolated output build recovered the check. |
| Release unit tests | 327 passed. |
| Frontend suite | Passed, including POS invoice timing and registry checks for all 132 entries. The POS harness originally left an unresolved promise because its suspended-count GET entered the invoice-detail queue. The amended fixture resolves both paths and the suite reaches its completion line. |
| Release integration | The 494-test baseline passed in the repository output directory. The expanded suite ran 495 tests from an isolated output directory and 494 passed there. The first-run static asset test received 404 because it resolves `frontend` relative to its assembly output directory. The same test passed on rerun from the standard output directory. |
| Focused delivery integration | 12 passed, including all 20 repeat assertions and restart. |
| Focused Edge | Invoice tab-switch recovery, saved-driver identity, blocked implicit removal, and driver save recovery passed at all six theme/viewport combinations. |
| Live Edge delivery | 20 actual POS checkouts followed by successful delivery corrections and reopen checks on disposable OWIN/SQLite. |
| Live Edge sales follow-up | Report-linked invoice, exact and empty search, PDF and saved receipt output, inline validation and focus, Escape, expected cash refusal, and persisted unpaid-credit cancellation passed. |
| Prior batch browser regressions | A, B and C passed again from isolated output locations. |

[Build](../artifacts/final-stage/build-isolated.txt) · [Frontend](../artifacts/final-stage/frontend-final.txt) · [Integration](../artifacts/final-stage/integration.txt) · [First-run rerun](../artifacts/final-stage/first-run.txt) · [Delivery integration](../artifacts/final-stage/delivery-api-final.txt) · [Edge](../artifacts/final-stage/edge-focused-final.txt) · [Live delivery trials](../artifacts/final-stage/edge-live-delivery.txt) · [Live Sales](../artifacts/final-stage/edge-live-sales-final.txt) · [Batch A](../artifacts/final-stage/batch-a-edge.txt) · [Batch B](../artifacts/final-stage/batch-b-edge.txt) · [Batch C](../artifacts/final-stage/batch-c-edge.txt)

## Native desktop and legacy environment

**Native desktop qualification is BLOCKED.** The isolated Windows 11 Pro build 26300 launch opened the Release WinForms window, created the isolated browser profile, and started WebView2 157.0.4341.60. The computer-use helper rejected the returned isolated window identity twice with an owner mismatch despite identical owner strings. Native authentication, application interaction, console, focus, DPI and dialogs remain unverified. No debug switch was used. VirtualBox had registered guests and none running during the probe. A safe isolated Windows 7 SP1 environment with the approved Chromium 109 runtime was not verified. [Native evidence](../artifacts/final-stage/native.txt) · [Manual checklist](final-manual-native-test-checklist.md)

## Acceptance

| Surface | Status |
| --- | --- |
| POS UI | PARTIAL |
| Sales Management UI | PARTIAL |
| Sales Reporting | PARTIAL |
| Delivery Orders | PARTIAL |
| Driver Management | PARTIAL |
| Cross-module regression | PARTIAL |
| Native desktop qualification | BLOCKED |
| Whole-application final QA | PARTIAL |

See the [final release blockers](final-release-blockers.md) for release decisions and the [earlier stage summaries](remaining-ui-modules-final-summary.md) for historical evidence.
