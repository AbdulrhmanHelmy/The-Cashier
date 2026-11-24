# Stage 2 quality assurance

Executed on November 19, 2026. Edge rendered the current repository frontend with intercepted same-origin responses from a disposable synthetic fixture. Browser navigation covered the full visible route inventory. The .NET suites exercised the API and persisted workflows using their own disposable fixtures. Native visual verification has a separate status below.

## Commands and results

| Command | Result |
| --- | --- |
| `dotnet build src/AlKhayal.UI/AlKhayal.UI.csproj -c Release --no-restore -m:1 -p:OutputPath=bin/Stage2/` | PASS. Release build with 0 warnings and 0 errors. |
| `dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --no-restore -c Release -m:1 -p:OutputPath=bin/Stage2/ --logger "console;verbosity=minimal"` | PASS. 322 passed. |
| `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --no-restore -c Release -m:1 -p:OutputPath=bin/Stage2Integration/ --logger "console;verbosity=minimal"` | 493 passed and 1 failed. The isolated output depth broke the first-run test's relative path to `frontend/`. |
| `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --no-restore -c Release -m:1 --filter FullyQualifiedName~StandaloneFirstRunApiTests --logger "console;verbosity=minimal"` | PASS. The first-run test passed from its standard output path. |
| `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --no-restore -c Release -m:1 --logger "console;verbosity=minimal"` | PASS. Full standard-output rerun passed all 494 tests in 7 minutes 50 seconds. |
| `node frontend/tests/run-tests.js` | Exit 0. Registry, redirect, sidebar flyout, shortcuts, tabs, alert badge and the other named suites printed results. The runner exited before its aggregate asynchronous completion message, as documented during Stage 1. |
| `node frontend/tests/design-system-tests.js` | PASS. Stage 1 theme tokens, contrast pairs and theme preference behavior. |
| `node frontend/tests/stage2-browser.cjs` | PASS. 76 visible routes and the hidden Quick Add entry reached. Four shell text contrast pairs met 4.5:1 in both themes. No uncaught browser errors. |
| `git diff --check` | PASS. |

The browser command requires Microsoft Edge and `playwright-core`. Set `THECASHIER_QA_PLAYWRIGHT` to the absolute path of an existing `playwright-core` installation if Node cannot resolve it from the test directory. The runner loads the repository JS and CSS, writes screenshots and route results under `artifacts/layout-navigation-stage2/`, and uses a reserved test hostname intercepted entirely by Playwright. The fixtures simulate API response shapes. They do not exercise writes, authentication cookies or licensing.

The solution-level build command stopped because the installed .NET 8 SDK did not recognize the `.slnx` solution format. The desktop project build and separate test project commands above use the supported project entry points.

## Navigation matrix

| # | Scenario | Status | Evidence |
| --- | --- | --- | --- |
| 1 | Expanded sidebar | PASS | Edge measured 272 px. |
| 2 | Collapsed sidebar | PASS | Edge measured 64 px. |
| 3 | Sidebar persistence | PASS | Edge reload checked explicit collapse and a manually opened group. |
| 4 | Active route highlighting | PASS | Edge checked the selected child and active tab. |
| 5 | Nested submenu expansion | PASS | Sales and Payment Methods menu paths. |
| 6 | Nested submenu keyboard interaction | PASS | Rail Enter, arrow and Escape paths in Edge and the flyout suite. |
| 7 | Collapsed flyout navigation | PASS | Flyout VM used rendered authorized links to select a child. |
| 8 | Flyout dismissal and focus restoration | PASS | Escape returned Edge focus to the trigger. |
| 9 | Sidebar scroll | PASS | Edge scrolled the navigation independently. |
| 10 | Fixed sidebar footer | PASS | Footer bounds before and after navigation scroll matched. |
| 11 | Permission-restricted navigation | PASS | Synthetic cashier and registry permission suites. |
| 12 | Feature-gated navigation | PASS | Disabling catalog removed the otherwise granted Quick Add action. |
| 13 | All quick actions | PASS | Edge opened POS, Quick Add, purchase editor, customer form, warehouse directory, returns and treasury. |
| 14 | Quick-action overflow | PASS | Edge opened More and chose overflow destinations. |
| 15 | Notification access | PASS | Bell opened Alerts. Alert unit tests covered unread, mark-read and legacy access. |
| 16 | Branch and shift status | PASS | Edge checked the existing branch label and both synthetic shift states without header clipping. |
| 17 | Global search | PASS | Edge opened results with Arrow Down and Enter. |
| 18 | Search permission filter | PASS | Synthetic cashier could not find the restricted Settings hub. |
| 19 | Open tab | PASS | Edge route runs activated the expected tab. |
| 20 | Switch tab | PASS | Edge switched away from purchase and back. Notes remained in the editor. |
| 21 | Close tab | PASS | Edge exercised Cancel and Confirm on the purchase discard prompt, then closed the tab. |
| 22 | Reopen existing page | PASS | Workspace VM asserted view reuse and no duplicate activation for a selected route. |
| 23 | Duplicate tab prevention | PASS | Edge checked one Sales workspace after repeated navigation. |
| 24 | Tab overflow | PASS | Edge exercised the tab selector and Escape. |
| 25 | Dirty form protection | PASS | Edge displayed the purchase dirty indicator and confirmed the actual discard dialog behavior. Catalog and POS expose their existing editor state to the shell. |
| 26 | Back navigation | PASS | Header Back restored the previously visited workspace in Edge. |
| 27 | POS navigation | PASS | Header action and F3 opened Touch POS. |
| 28 | Theme switching | PASS | Edge rendered Light and Charcoal Dark. |
| 29 | Theme persistence | PASS | Edge reload retained Dark. |
| 30 | RTL rendering | PASS | Edge captured RTL shell, overlays and tabs in both themes. |
| 31 | Keyboard shortcuts | PASS | Shortcut VM suite and Edge Ctrl+K/F3 smoke. |
| 32 | 1024 × 768 | PASS | Edge measured shell geometry and shift text. |
| 33 | 1366 × 768 | PASS | Edge measured shell geometry and shift text. |
| 34 | 1600 × 1000 | PASS | Edge measured shell geometry and shift text. |
| 35 | Native WebView2 | BLOCKED | Native session and Windows DPI verification were unavailable. The desktop build passed. |
| 36 | No horizontal shell overflow | PASS | Edge measured all three sizes plus 820 × 680 and 512 × 384. |
| 37 | Overlay bounds | PASS | Flyout bounds regression, popup screenshots and keyboard dismissal. |
| 38 | Checkout feedback and printing access | PASS | Automated printing regressions and disposable API suites passed the covered workflows. Physical printing and a native end-to-end sale were outside this run. |
| 39 | Compatibility redirects | PASS | Frontend test executed the production resolver for 40 compatibility entries. |
| 40 | Enabled module reachability | PASS | All 76 visible routes activated against the authorized fixture. See [route coverage](layout-navigation-stage2-coverage.md). |

## Browser screenshots

The files below are Edge captures of the implemented shell. The earlier [Stage 1 Dashboard](../artifacts/design-system-dark-charcoal/dashboard.png), [Touch POS](../artifacts/design-system-dark-charcoal/pos.png) and [search](../artifacts/design-system-dark-charcoal/search-popover.png) are before references from the prior audit.

| Area | After captures |
| --- | --- |
| Sidebar and Home | [Expanded Dark](../artifacts/layout-navigation-stage2/01-expanded-dark-dashboard-closed-shift.png), [collapsed Dark](../artifacts/layout-navigation-stage2/02-collapsed-dark.png), [expanded Light](../artifacts/layout-navigation-stage2/05-expanded-light.png), [collapsed Light](../artifacts/layout-navigation-stage2/04-collapsed-light.png), [active submenu](../artifacts/layout-navigation-stage2/17-active-submenu.png), [rail flyout](../artifacts/layout-navigation-stage2/03-collapsed-flyout.png), [scrolling and fixed footer](../artifacts/layout-navigation-stage2/06-sidebar-scroll-footer.png) |
| Header | [Full header and closed shift](../artifacts/layout-navigation-stage2/01-expanded-dark-dashboard-closed-shift.png), [narrow overflow](../artifacts/layout-navigation-stage2/20-header-overflow.png), [search](../artifacts/layout-navigation-stage2/07-keyboard-search.png), [Alerts](../artifacts/layout-navigation-stage2/09-notifications.png), [open shift](../artifacts/layout-navigation-stage2/19-open-shift.png), [restricted cashier](../artifacts/layout-navigation-stage2/22-restricted-cashier.png) |
| Tabs | [Single/pinned](../artifacts/layout-navigation-stage2/01-expanded-dark-dashboard-closed-shift.png), [multiple, active and inactive](../artifacts/layout-navigation-stage2/15-pos.png), [all workspaces](../artifacts/layout-navigation-stage2/21-tab-overflow.png), [unsaved purchase indicator](../artifacts/layout-navigation-stage2/23-purchase-dirty-indicator.png) |
| Operational screens | [POS](../artifacts/layout-navigation-stage2/15-pos.png), [Products](../artifacts/layout-navigation-stage2/08-products.png), [Quick Add Product](../artifacts/layout-navigation-stage2/24-quick-product-form.png), [Sales](../artifacts/layout-navigation-stage2/10-sales.png), [Purchasing](../artifacts/layout-navigation-stage2/11-purchasing.png), [Customers](../artifacts/layout-navigation-stage2/12-customers.png), [Add Customer](../artifacts/layout-navigation-stage2/18-add-customer.png), [Inventory](../artifacts/layout-navigation-stage2/13-inventory.png), [Reports](../artifacts/layout-navigation-stage2/14-reports.png), [Settings](../artifacts/layout-navigation-stage2/16-settings.png) |

Edge automation asserted the browser-native discard confirmation and both outcomes. The PNG shows the dirty indicator because Playwright page screenshots exclude browser-owned dialog chrome.

## Open qualification

- **Medium — native desktop evidence.** Execution policy blocked provisioning and starting the isolated QA host for the live visual pass. Native WebView2 screenshots, Windows display scaling at 100%, 125%, 150% and 200%, the legacy Chromium 109 runtime, physical printing and attached barcode hardware remain unverified. The Edge viewport checks verify CSS layout independently.
- **Medium — editor coverage.** In-memory view switching and discard protection passed for the purchase editor. The shell honors existing POS and catalog guards. Editors with no dirty hook and the native window close flow retain their prior behavior.
- **Low — frontend test runner.** The existing runner still exits before the aggregate asynchronous result. This report counts the named output and the independent Edge suite.
