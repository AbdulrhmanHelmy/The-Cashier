# Settings and Printing Settings regression QA

21 November 2026 · The Cashier 1.1.0

## Build and automated suites

| Check | Command | Result |
| --- | --- | --- |
| Desktop Release | `dotnet build src/AlKhayal.UI/AlKhayal.UI.csproj --no-restore -c Release -m:1` | PASS, 0 errors and 0 warnings |
| .NET unit | `dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --no-restore -c Release -m:1` | PASS, 327 of 327 |
| .NET integration | `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --no-restore -c Release -m:1` | PASS, 495 of 495, 9m 8s |
| Printer Settings | `node frontend/tests/printing-tests.js` | PASS |
| Full frontend | `node frontend/tests/run-tests.js` | PASS, including 132 registry screens, barcode settings, saved product/unit preview, receipt transport, assignments, backup access and user management |
| Printing Settings Edge | `node frontend/tests/settings-printing-browser.cjs` | PASS, Edge 158.0.4370.33, no page errors |
| Stage 2 Edge | `node frontend/tests/stage2-browser.cjs` | PASS, route access, tabs, focus, responsive shell and dirty purchase cancellation |
| Stage 3 Edge | `node frontend/tests/stage3-browser.cjs` | PASS, product and barcode flows in both themes at three sizes |
| Stage C Edge | `node frontend/tests/remaining-browser.cjs c after` | PASS, external backup result, cancellation, rapid permission switching and General/Company save and cancel |
| Whitespace | `git diff --check` | PASS. Git reported existing line-ending conversion notices. |

The .NET integration suite used the default Release output path. That layout is relevant to the first-run static-assets test, which resolves `frontend` relative to the assembly path. The earlier isolated-output failure was not reproduced. Prior-stage Edge scripts wrote to a separate evidence directory to preserve existing audits. The Settings browser test resolves Playwright Core from `CASHIER_PLAYWRIGHT` or the usual Node module path. For meaningful screenshots, set `CASHIER_RECEIPT_FIXTURE` to a synthetic receipt PNG and `CASHIER_LABEL_FIXTURE` to a synthetic label PNG. The recorded run used the audit's renderer-generated receipt and the Stage 3 synthetic 35×25 label.

## Focused regression matrix

| Area | Result | Checks |
| --- | --- | --- |
| Initial loading | PASS | Printing settings ahead of discovery, discovery ahead of printing settings, branding last, separate barcode settings and delayed discovery, including 3-second hold. |
| Read errors | PASS | Missing printing settings disables managed saves until retry. Company failure leaves all assignment controls available and offers retry. Failed discovery and empty success produce distinct feedback. |
| Discovery counts | PASS | Rapid repeated clicks, initial in-flight refresh, retry after failure, refresh after success, advanced editor reuse, switching away and return, and close/reopen during scan. |
| Retained/disposed views | PASS | Active and inactive completion, inactive failure, multiple switches, saved controls on return, and late settlement ignored by a disposed view. Old/new discovery overlap is prevented by single-flight. |
| Save integrity | PASS | Recording tests inspect the complete printing PUT while discovery is pending, all nine assigned records, registered printer identity, paper configuration and document-template updates. Rapid save is deduplicated. A rejected save retains the dialog draft and Cancel reopens from saved defaults. |
| Receipt drafts | PASS | Device, name, custom width and footer survive failed, empty and successful refresh. Existing tests exercise uploaded/removed logo, visibility, all preset paths, margins, scale, continuous paper and preview sequencing. |
| Barcode drafts | PASS | Custom width and the same designer DOM survive refresh. Frontend and Stage 3 cover barcode dimensions, design preview and product-context preview. |
| Receipt preview | PASS | Sample endpoint, saved-document search and selected ID, delayed preview, stale/error response handling, close cleanup and 125% viewing zoom. Saved-document output was a fixture PNG. |
| Profile modal | PASS | Light and Charcoal Dark at 1024×768, 1366×768 and 1600×1000. RTL HTML, long Arabic company and Latin printer names, visible Save, initial focus, required-paper focus, scroll-to-preview and Escape focus restoration. |
| Appearance | PASS | Native button keyboard activation and a single pressed state across Light, Dark and System. |
| Scale configuration | PASS | Browser checks all six text, numerical and select controls for associated native labels. |
| Native WebView2 and physical printer | NOT TESTED | QA exercised the shared Web frontend against isolated fixtures. The Release binary built and all .NET tests passed. Native viewport behavior, real installed-printer UI, physical jobs, real saved invoice fidelity and the Windows 7 runtime were outside this run. |

## Screenshot and timing evidence

[Post-edit screenshot index and timing trials](../artifacts/settings-printing-implementation/after.json) contains 38 captured browser states and the exact Edge version. File names identify viewport, theme and simulated condition.

- [Immediately after navigation](../artifacts/settings-printing-implementation/1366x768-light-settings-printers-simulated-initial-navigation.png) and [saved controls during discovery](../artifacts/settings-printing-implementation/1366x768-light-settings-printers-simulated-delay-saved-settings.png)
- [Registered and unavailable printers](../artifacts/settings-printing-implementation/1366x768-light-settings-printers-simulated-success-registered-and-unavailable.png), [nine assignments](../artifacts/settings-printing-implementation/1366x768-light-settings-printers-simulated-nine-document-assignments.png), [failed discovery](../artifacts/settings-printing-implementation/1366x768-light-settings-printers-simulated-error-after-refresh.png), and [empty discovery](../artifacts/settings-printing-implementation/1366x768-light-settings-printers-simulated-empty-after-refresh.png)
- [Return after background completion](../artifacts/settings-printing-implementation/1366x768-light-settings-printers-simulated-background-completion.png)
- [Compact Dark Add Printer](../artifacts/settings-printing-implementation/1024x768-dark-settings-printers-simulated-add-dialog.png), [paper validation](../artifacts/settings-printing-implementation/1024x768-dark-settings-printers-simulated-required-paper.png), and [receipt preview](../artifacts/settings-printing-implementation/1024x768-dark-settings-printers-simulated-sample-receipt.png)
- [Light barcode settings and preview](../artifacts/settings-printing-implementation/1600x1000-light-settings-printers-simulated-barcode-settings.png)

The [before evidence](../artifacts/settings-printing-implementation/before/before.json) is separate. The full working-tree diff includes changes that predate this task. The implementation report lists this task's source scope.

The final Edge rerun also verified Tab order from profile name to Windows device, keyboard focus on the preview region, and a paper dimension edited while the scan was pending. [Final rerun evidence](../artifacts/settings-printing-implementation/final-verification/after.json)

A fresh read-only Windows backend probe was attempted. Windows PowerShell's script execution policy rejected it, so the performance report uses the earlier Windows probe only. The policy was left in effect.
