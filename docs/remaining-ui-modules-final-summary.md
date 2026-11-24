# Remaining non-POS UI modules

November 21, 2026. **Overall acceptance is PARTIAL.** Seven reproduced defects have browser regressions. The route matrix and full automated suites ran, and the module QA reports identify workflows still requiring acceptance.

| Batch | Defects corrected | Acceptance and evidence |
| --- | --- | --- |
| A | Return picker accepted a cancelled selection. Viewing instrument history reset directory pagination. | **PARTIAL.** [Returns, treasury, expenses and payments QA](remaining-stage-batch-a-qa.md) |
| B | Late payroll history overwrote the current employee. Installment collection retained an unsent amount after the customer changed. | **PARTIAL.** [Workforce, shifts, attendance, installments and maintenance QA](remaining-stage-batch-b-qa.md) |
| C | Backup success feedback failed to explain why output was absent from configured-folder history. A delayed permissions response opened the dialog for the wrong cashier. Company and general settings could accept edits or cancellation while saving. | **PARTIAL.** [Reports, settings, access, backup and general UI QA](remaining-stage-batch-c-qa.md) |

## Verification

| Command or check | Result |
| --- | --- |
| `dotnet build src/AlKhayal.UI/AlKhayal.UI.csproj -c Release --no-restore -m:1` | Passed, zero warnings and errors. |
| `dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj -c Release --no-restore -m:1` | 327 passed. |
| `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj -c Release --no-restore -m:1` | 494 passed. |
| Focused Release integration filters | 93 passed for A, 52 for B and 110 for C. |
| `node frontend/tests/run-tests.js` | Passed. Includes shared navigation, report response handling and affected-module regressions. |
| `node frontend/tests/remaining-browser.cjs a`, `b`, `c` | Edge passed the targeted defect regressions and route matrix in both themes at 1024×768, 1366×768 and 1600×1000. |
| `node frontend/tests/stage2-browser.cjs` through `stage6-browser.cjs` | All five Edge suites passed. |
| `git diff --check` | Passed. |

Edge uses the actual frontend assets with intercepted HTTP responses. The .NET tests exercise the backend and disposable fixtures separately. The browser route matrix establishes navigation, RTL and document bounds across the requested themes and sizes. A capture can show an initial, empty or unavailable state where the fixture supplies only a generic API response. Populated-browser coverage is limited to the cases listed in each batch QA. No native desktop workflow, Chromium 109 deployment, DPI qualification, physical print or native folder picker was exercised in this pass. See [Stage 6 native closure evidence](purchases-stage6-qa.md#native-windows-and-webview2) for the prior native automation limitation.

## Source and Git review

The audit report, screenshot manifest, and all 194 historical PNGs were available as extracted files in the starting worktree. Historical reference sheets in the three evidence directories show the reviewed selections. The original ZIP was located in the workspace. SHA-256 verification matched all 196 extracted PNG and Markdown files to the archive entries. Current Edge captures were made independently with synthetic records.

This pass changed `frontend/js/modules/returns.js`, `payment-methods.js`, `workforce.js`, `renderers.js` in the installment payment view, `backup-settings.js`, `user-management.js` and `general-settings.js`. It added `frontend/tests/remaining-browser.cjs`, the nine batch reports and this summary. The starting worktree contained Stage 1–6 edits, including changes to shared UI, sales and the native host. These were preserved. The new production changes are confined to frontend rendering, request ordering, selection context, control focus and status feedback. Financial payloads, formulas, posting and persistence, access enforcement, licensing, printing and recovery engines are outside the production patch. No live data, payment provider, printer or restore operation was used.

Starting and ending statuses, diff stats, screenshot evidence and detailed test logs are in [Batch A](../artifacts/remaining-stage-batch-a/), [Batch B](../artifacts/remaining-stage-batch-b/) and [Batch C](../artifacts/remaining-stage-batch-c/). POS, sales and delivery implementation remain reserved for the final stage.
