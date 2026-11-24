# The Cashier 1.1.0 Stage 1 QA

**Status: PARTIAL.** The design system is in use across the current frontend. Browser, desktop build, and regression evidence are available. Native Windows visual qualification and parts of the interaction matrix remain open.

## Build and automated checks

| Command | Result |
| --- | --- |
| `node frontend/tests/design-system-tests.js` | Passed. Checks approved action colors, scoped contrast pairs, prepaint theme initialization and Light/Dark/System preference behavior. |
| `node frontend/tests/run-tests.js` | Exit 0 and named regression results printed. The runner stopped before its aggregate asynchronous completion message, and the untouched baseline exited at the same point. Async scenarios without a reported result remain unverified. |
| `dotnet build src/AlKhayal.UI/AlKhayal.UI.csproj -c Release --no-restore` | Passed with 0 warnings and 0 errors, including the OWIN host and its dependencies. |
| `dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj -c Release` | 322 passed. |
| `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj -c Release` | 494 passed. |
| `dotnet build TheCashier.slnx -c Release` | Toolchain-blocked. The installed .NET SDK 8.0.428 failed to recognize SLNX with MSB4068. The desktop project and both test projects compiled directly. |
| `git diff --check` | Passed. |

## Rendered verification

Microsoft Edge on Windows rendered the real OWIN frontend and API against a disposable SQLite dataset. The fixtures used synthetic credentials, catalog records and sales. The available screenshots are indexed in the [route and screenshot coverage](design-system-stage1-coverage.md). The coverage counts represent screens rendered, with workflow results recorded separately.

| QA scope | Status | Evidence |
| --- | --- | --- |
| QA-001–009 identity, colors, font, table style | Partial | Theme and contrast tests, 68 route pairs, offline same-origin WOFF2 request with `font/woff2`, and four loaded font faces. A comprehensive computed-style inventory for each individual feature control was not performed. |
| QA-010–015 contrast, focus, statuses | Partial | Rendered color samples below. Visible focus and associated errors checked on sign-in and invoice cancellation. Focus clipping on every sticky region was not tested. |
| QA-016–021 keyboard, validation, sizing and motion | Partial | Enter checkout, Escape modal close, opener focus restoration, required sign-in feedback, reduced-motion browser captures and forced-colors report focus checked. Full keyboard traversal, 200% scaling and all target sizes were not tested. |
| QA-022–033 shared states | Partial | Shared defaults, disabled Complete Sale, enabled gold checkout, field validation and modal focus exercised. Automated cases cover multiple request guards and filters. Every button state, wide table and notification overlap case was not manually exercised. |
| QA-034–040 RTL and desktop | Partial | Mixed Arabic/Latin product text, invoice ID isolation and preserved synthetic total checked. Dashboard, POS, sales list, catalog and a sales report captured at 1024×768 and 1366×768 in both themes. Native display scaling and multi-monitor DPI were not tested. |
| QA-041 checkout | Passed for the synthetic cash sale | Server-confirmed invoice number, persisted total of 25.50 EGP, payment label, receipt entry point, invoice view and New Sale affordance. The result remained visible beyond 4.5 seconds. Physical receipt output was not tested. |
| QA-042 cancellation | Passed for validation and safe exit | Danger treatment, reason requirement, invalid field focus, Escape and intact posted invoice. A real cancellation was outside this visual scenario. |
| QA-043–049 purchase, backups, printing, profit, permissions and recovery | Partial | Existing fixture-level tests cover these workflows. Browser sampled related route presentation. Physical printing, native backup picker, live restore, and restricted-user browser matrix were not exercised. |
| QA-050 keyboard-only operations | Partial | Enter checkout and Escape cancellation verified in Edge. Close shift and restore were not exercised keyboard-only. |
| QA-051 screenshots | Partial | 148 before and 179 after PNGs, including all 68 comparable Light/Dark routes and selected workflow states. Native WinForms comparison, full DPI and touch matrix remain open. |
| QA-052 reports | Passed for rendering | All 18 Reports Center views captured in both themes. The Advanced Shifts setting was enabled on the isolated database for the feature-gated shift report. |

### Contrast

Measured WCAG contrast from browser-resolved colors. The action ink and fill were also checked on the rendered, enabled checkout button in Dark Mode. Focus colors are measured against the named surface, which does not establish that every outline is unclipped.

| Pair | Light | Dark |
| --- | ---: | ---: |
| Primary text / app | 13.15:1 | 16.37:1 |
| Secondary text / surface | 5.73:1 | 8.99:1 |
| Control border / surface | 3.18:1 | 4.83:1 |
| Primary button text / fill | 7.58:1 | 7.12:1 |
| Gold button text / fill | 8.44:1 | 10.09:1 |
| Outer focus ring / elevated surface | 14.47:1 | 8.20:1 |
| Sidebar text / sidebar | 7.58:1 | 8.76:1 |
| Success ink / status surface | 4.64:1 | 6.94:1 |
| Error ink / status surface | 5.66:1 | 4.85:1 |

Browser visual coverage does not qualify Windows 7, Chromium 109, native WebView2 rendering, printer hardware, barcode devices, touch hardware, or all WCAG 2.2 AA criteria.

## Dark charcoal refinement

The five neutral roles are documented in [Design system v1](design-system-v1.md). The shared web theme uses the same charcoal for subtle and elevated surfaces. WinForms uses the matching app and standard surface colors when Windows requests Dark Mode.

| Dark Mode contrast pair | Ratio |
| --- | ---: |
| Primary text / app | 16.61:1 |
| Secondary text / standard surface | 9.48:1 |
| Secondary text / elevated surface | 8.33:1 |
| Control border / standard surface | 5.09:1 |
| Control border / elevated surface | 4.47:1 |
| Focus ring / elevated surface | 9.29:1 |
| Selected text / selected fill | 9.53:1 |
| Teal selection marker / selected fill | 4.14:1 |

The decorative divider measures 1.34:1 against the elevated surface. Essential control boundaries use the stronger control-border token. Dark selection retains its existing teal tint and marker.

Screenshots from the real OWIN frontend in Edge with the disposable SQLite fixture include [sign-in](../artifacts/design-system-dark-charcoal/login.png), [dashboard](../artifacts/design-system-dark-charcoal/dashboard.png), [products](../artifacts/design-system-dark-charcoal/products.png), [product dialog](../artifacts/design-system-dark-charcoal/product-dialog.png), [POS](../artifacts/design-system-dark-charcoal/pos.png), [reports](../artifacts/design-system-dark-charcoal/reports.png), and [search popover](../artifacts/design-system-dark-charcoal/search-popover.png). Native WinForms colors compiled. Native WebView2 rendering and DPI were outside this capture pass.
