# Global Search QA

## Header Global Search

The Edge browser run exercised the existing Header field, Ctrl+K, Arrow Down, Escape, RTL, popup viewport bounds and synthetic cross-category results. The full frontend suite also ran navigation, workspace, barcode, sales, purchasing, returns, delivery, reporting, permissions and Settings regressions.

## Settings Global Search

The same Edge run exercised the existing Settings field and preserved its cards in the same workspace. Clicking a product record opened the exact fixture ID from Settings and produced one Product Directory tab. The frontend mount test checks that the Settings renderer passes its existing field and separate popup to the shared service.

## Shared engine and cross-entry checks

`node frontend/tests/global-search-tests.js` exercises the production service with two mounted controls. It covers Arabic and Western digits with leading zeros, two identical requests sharing the same provider calls, text escaping, bounded rendering, result mapping and typed navigation for all configured providers, independent keyboard selection, loading and retry messaging, out-of-order results, blur and Escape, tab inactivity, and permission invalidation. These are controlled frontend tests. They do not prove that every destination opens a persisted record from both surfaces.

## HTTP permission checks

`GlobalSearchApiTests.cs` uses a disposable SQLite database and the actual loopback HTTP host. It verifies administrator and granted operator access, denial for restricted and anonymous users, access revocation, empty query validation, exact phone and ID, capped results and an identity-only customer projection. Record searches operate on separate test databases. The test does not access business data from an installed copy of The Cashier.

## Large dataset and request counts

The browser run recorded timings in `artifacts/global-search/browser-metrics.json`. A broad Arabic query made 17 fixture requests for an administrator in each surface. Time from input to inserting the first product result was 323–358 ms, including the 300 ms debounce. The separate elapsed field also includes Playwright waiting for the record.

The focused HTTP run measured these point samples on a disposable database while integration regressions were running. Each HTTP response returned 20 records and the popup retained up to five.

| Query | Seed size | Focused HTTP time |
| --- | ---: | ---: |
| Customers by code prefix | 5,000 | 224 ms |
| Products by name | 4,000 | 196 ms |
| Sales invoices by reference prefix | 2,500 | 222 ms |
| Purchase invoices by reference or supplier | 2,500 | 181 ms |

The same invoice reference was present in both document types and the exact lookup picked it independently from each endpoint. The test also checked a phone number with a leading zero and the customer record ID. These measurements are individual samples. They do not establish a throughput target or represent older hardware.

## Screenshots

Twelve browser captures under `artifacts/global-search/` show both entry points in light and charcoal dark themes at 1024×768, 1366×768 and 1600×1000. Screenshots use the repository frontend and synthetic HTTP responses. The permissions and database checks run against the live disposable HTTP host separately. The compact browser case checks dialog focus, retry, empty results and tap selection. Physical barcode scanners and native WebView2 were outside this browser run.

## Commands and results

| Check | Command or runner | Result |
| --- | --- | --- |
| Focused frontend | `node frontend/tests/global-search-tests.js` | Passed |
| Frontend regressions | `node frontend/tests/run-tests.js` | Passed, 132 routes |
| Browser and screenshots | `node frontend/tests/global-search-browser.cjs`, using Edge and `THECASHIER_QA_PLAYWRIGHT` | Passed, twelve captures and navigation to Sales Report and Company Profile |
| Desktop Release | `dotnet build src/AlKhayal.UI/AlKhayal.UI.csproj -c Release --no-restore -m:1 -p:OutputPath=bin/GlobalSearchDesktop/` | Passed, zero warnings or errors |
| .NET unit | `dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj -c Release --no-restore -m:1 -p:OutputPath=bin/GlobalSearchUnit/` | 327 passed |
| .NET integration | `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj -c Release --no-restore -m:1 -p:OutputPath=bin/GlobalSearchIntegration/Release/` | 496 passed |

The first integration run used a custom output path one level too shallow for the standalone test's relative frontend lookup. The standalone test and the full suite passed from the corrected output path without changing or skipping that test.

The shared JavaScript uses APIs available in Chromium 109. The browser run used the installed Microsoft Edge. Windows 7, Chromium 109 WebView2 and native desktop DPI behavior still require runtime acceptance. See [coverage](global-search-coverage.md) for unsupported record categories.
