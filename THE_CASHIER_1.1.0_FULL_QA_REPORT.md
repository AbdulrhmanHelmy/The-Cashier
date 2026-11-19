# The Cashier 1.1.0 full QA report

**Release decision — PRODUCTION BLOCKED**

## Executive summary

QA exercised the Windows installer and an installed workflow from trial start to purchase, credit sale, inventory and balance reconciliation, PDF generation, and backup/restore in an offline disposable guest. The final automated runs passed 322 unit tests, 494 integration tests and eight License Manager tests. The historical Trial Dialog timeout was reproduced and fixed in the test harness. Production-signed activation and the complete installed operator acceptance matrix remain unqualified.

## Baseline and environment

- Checkout `E:\The Cashier`, branch `main` tracking `origin/main`, commit `0834db5770fa063f75bd626a925012fe490aa2c5`. The four named Cashier reports were reviewed as historical evidence.
- Incoming changes covered `docs/testing.md`, the Cashier Inno script, SQLite backup code and fixtures, the owner tool README, deployment, launcher, server and packaged test, the owner-tool VBS rename, and the untracked production blockers report. Those changes were preserved.
- Host Windows 11 Pro x64, build 26300, Intel Core i7-6820HQ, approximately 16 GiB RAM. Sandbox guest build 26100, networking disabled. SDK 8.0.428, .NET Framework release 533509, VSTest 17.11.2, xUnit adapter 4.0.1, system Node 26.13.1, owner package Node 24.22.2, Inno Setup 6.7.3.
- Cashier uses a .NET Framework 4.8 WinForms shell with WebView2, a vanilla HTML/CSS/JavaScript frontend, an OWIN/Web API 2 loopback host, and SQLite/Dapper with a native x86 interop DLL and embedded schema migrations. Printing has native PDF and printer-service paths. Backup and restore use SQLite snapshots, archive validation and staged publication. Application and owner licensing use signed product-scoped requests and hardware claims.
- Business fixtures used disposable SQLite files and synthetic accounts and records. Installed checks were restricted to the sandbox. License tests used disposable credentials. No real database, prior installation, production issuer, or production private key was used. No changes were pushed or published.

## Evidence and test counts

| Run | Status | Evidence |
|---|---|---|
| .NET unit, final | PASS, 322/322, 0 failed, 0 skipped | `unit-final.log`, `unit-final.trx` |
| Native dialogs, fresh processes | PASS, 3/3 each in three runs | `trial-repeat.log`, `trial-1.trx` through `trial-3.trx` |
| Full frontend | PASS, exit 0 | `frontend.log`. Runner includes renderer, DOM fixture and static checks. |
| Frontend JavaScript parse checks | PASS, 85 files | `frontend-syntax.log` |
| Packaged License Manager and source tests | PASS, 8/8, 0 failed, 0 skipped | `manager.log`, staged owner package with a disposable profile and restricted outbound Node network calls |
| Backup suite, ordinary and deep Unicode paths | PASS, 17/17 each, 0 failed, 0 skipped | `backup-standard.trx`, `backup-deep.trx`. Deep fixture base was 128 characters, with resulting scheduled archive paths crossing the legacy Windows limit. |
| Forced interruption during offline restore | PASS | `restore-crash-retest.log`. The crash runner killed its own restore process after observing SQLite's rollback journal. Reopening the database verified the original rows and integrity, removed interrupted staging, and retained one emergency archive. |
| Existing .NET integration before the new acceptance test | PASS, 493/493, 0 failed, 0 skipped | `integration.log`, `integration.trx` |
| Complete .NET integration, final | PASS, 494/494, 0 failed, 0 skipped | `integration-final.log`, `integration-final.trx` |
| New first-run host-to-SQLite acceptance | PASS, 1/1, 0 failed, 0 skipped | `first-run-api.log`, `first-run-api.trx` |
| Installed Cashier workflow in offline Windows Sandbox | PASS for the executed scenario | `sandbox-sales-qa/guest.log` and `setup.log`. Native shortcut, activation and trial UI, real installed HTTP and SQLite business operations, PDF, backup, restore and restart. |
| Desktop Release publish and package comparison | PASS | `release.log`, `artifact-verification.log` |
| All nine solution projects, Release | PASS, 0 warnings, 0 errors | `solution-flat.log`. SDK 8 could not parse the checked-in `.slnx`, so the same project list was built via a disposable SDK-compatible `.sln`. |
| Installer script syntax and source resolution | PASS, compiler exit 0 | `installer-script-check.log`, Inno Setup 6.7.3, output disabled |

Raw run evidence is in `C:\Users\user\Documents\Codex\2026-11-19\files-pasted-by-the-user-the-3\work`. Counts for focused reruns overlap the full suites.

## Functional coverage matrix

PASS applies to the executed layer described in each row. Source inspection alone does not establish a pass.

| Phase | Executed validation | Remaining coverage |
|---|---|---|
| A. Baseline | PASS for Release build, package extraction and hash comparison, disposable databases and offline guest setup. | Production issuance unavailable. |
| B. Installation and startup | PASS for silent install, Start Menu launch, native activation and trial start, first database creation, offline loopback startup, graceful exit and restart after forced termination. | NOT TESTED for startup under a Unicode Windows profile, guest reboot, invalid configuration, and ACL-denied startup. |
| C. Authentication and RBAC | PASS for installed bootstrap/login/logout and at the automated layer for authorization. The application supports administrator and cashier roles, with configurable screen grants. Backend fixtures cover invalid and disabled credentials, restrictions and protected writes. | NOT TESTED for a full installed UI permission matrix and cashier grant configurations representing separate jobs. |
| D. Catalog and inventory | PASS at the automated layer. Catalog, warehouse, purchase and sale suites exercise persisted prices, product/variant searches, inventory movements, FIFO, returns, stock floors and reorder reports. | NOT TESTED for barcode hardware, a production-sized catalog and manual WebView interaction. |
| E. Customers and suppliers | PASS for installed supplier validation, party creation and balance reconciliation. Persistence and loopback suites also cover statements, payment allocation and supplier purchase/payment across a host restart. | NOT TESTED for exhaustive installed UI workflows, Unicode input methods and manual navigation. |
| F. POS and sales | PASS for installed open-shift enforcement, credit checkout, repeat posting and stock/receivable reconciliation. Automated fixtures additionally cover cash, partial payments, installments, returns and rollback. | NOT TESTED for complete operator checkout in the installed UI, rapid live scanning, large carts, and an independent exhaustive tax and rounding matrix. |
| G. Purchasing | PASS for installed credit purchase posting, retry and stock/supplier reconciliation. Automated tests cover treasury, inventory layers, payments and returns. | NOT TESTED for installed forms and a full tax/discount matrix. |
| H. Invoices and printing | PASS for installed invoice PDF generation and automated document/template resolution, long invoice handling and print-job dispatch through a recording backend. | BLOCKED for physical standard and thermal print verification. NOT TESTED for visual PDF/Arabic layout acceptance, the Windows spooler, offline printers and user-cancelled print jobs. |
| I. Reports and analytics | PASS for installed daily sales total and counts. Reporting and export suites exercise persisted totals, dates, financial summaries, inventory, authorization and output contracts. | NOT TESTED for manual coverage of all 18 installed report screens, printed exports or production-size performance. |
| J. Backup and restore | PASS for installed manual backup, archive inspection, rollback of a post-backup product, session invalidation, restored sale and restart. Automated tests additionally cover scheduled tasks, concurrent scheduler, retention, malformed archives and integrity at both path depths. | NOT TESTED for disk full or forced termination during archive writing. Forced interruption during restore was exercised separately. |
| K. Licensing and trial | PASS for disposable-key desktop/Node protocol, wrong product/hardware and tamper rejection, trial transitions, packaged issuer restart, and native contacts harness. The supported paid format is perpetual. The signed subscription-format fixture is rejected as unsupported. | BLOCKED for authorized production signing and installed activation/restart on production trust. Actual clipboard copy and installed request export NOT TESTED. |
| L. Reliability and security | PASS at the automated layer for executed disposable restart, replay, rollback and authorization cases. The packaged owner tool and frontend suites also completed. | NOT TESTED for exhaustive injection/path review, comprehensive secret scanning, real multi-terminal load, long sessions and sustained locking/crash qualification. |
| M. Build and release packaging | PASS for Release publish and matching packaged artifacts. | Installer rebuild was unnecessary because tracked changes were limited to tests and test documentation. |

## Financial, restore and security evidence

- The supplier restart fixture posted two units at 10.13 and collected 5.13. The reopened host returned 15.13 payable and 5.13 paid. SQLite checks verified one posted invoice, two ledger entries, one receipt cost layer, integrity, and foreign keys.
- The cashier checkout fixture posted 12.34 and returned the same treasury movement on retry. The account balance remained 12.34. Separate tests exercised atomic FIFO posting, concurrent inventory issues, customer credit collection, and return reconciliation.
- In the installed guest, 3 × 10.33 posted as 30.99 payable and added three units. A credit sale of 2 × 14.12 posted as 28.24 receivable, left one unit, and appeared once in the daily sales report at 28.24. Retrying both posts reused their inventory document IDs. The installed runtime rejected the first sale post while the shift was closed. It accepted the same draft after opening a shift.
- Backup cases rejected invalid archives, unsafe member paths, incompatible schema and mismatched hashes while preserving the live database. Restore coverage verified persisted posted sales, stock and permissions and preservation of destination trial state.
- Backend test coverage included missing sessions, elapsed sessions, missing CSRF, restricted functions, and cross-account receipt/shift access. The packaged owner tool denied unauthenticated and cross-origin calls. The source uses loopback by default, same-origin WebView navigation checks, parameterized SQL in reviewed data paths, and protected session cookies. The limited source scan for common private-key markers found the owner's encrypted-PEM format validation. A comprehensive secret and SQL-injection audit was not run.

## Installation and startup

The offline guest installed the existing candidate using its real Inno Setup executable, with logging and the optional desktop icon task enabled. The installer used its configured administrative mode and completed without requesting a reboot. The log records successful icon and uninstall registry entry creation and exit 0 from the HTTP URL reservation command. Installed executable metadata reported The Cashier 1.1.0 and the Start Menu shortcut pointed to the expected executable.

The first two automation attempts demonstrated installation and opened the real activation window. The second captured a real request for `TheCashierPOS` and showed that UI Automation classified the trial control as a pane. The later run invoked the confirmed native WinForms button in the guest. The app created its own `thecashier.db`, served the branded login page on port 5138, and passed bootstrap, wrong-password rejection, logout, login and Arabic product creation/search through the installed API. The process exited gracefully, relaunched, was terminated on purpose, and served the login page again after another launch. The follow-up captured the `The Cashier` main window and its WebView2 child process. These runs reached the installed x86 SQLite-backed runtime. The actual visual login and dashboard layouts, installed standard-user behavior, and visual icon inspection remain unverified. See `sandbox-run`, `sandbox-retest`, `sandbox-lifecycle` and `sandbox-business-retest` under the evidence directory.

The final installed scenario completed with guest harness exit 0. It generated a 49,147-byte PDF with a valid PDF header and a 62,637-byte backup archive. Restoring invalidated the session, preserved the posted invoice and removed the product created after backup. After a normal exit and a subsequent forced termination, a fresh process accepted login and returned the same invoice number and 28.24 total. See `sandbox-sales-qa`.

## Native Trial Dialog timeout

The fresh-build baseline failed at the 30-second STA join. A diagnostic run captured the error at the popup callback and closed the modal in a `finally` block. That run exposed the exact mismatch between the legacy expected title and the actual Cashier title. The outer timeout followed a callback assertion, with the timer already stopped and the modal left open. The STA thread, message pump and native form were active.

The final change updates the expected Cashier caption and heading, ensures modal closure on failure, propagates the callback assertion with its stack and makes the test thread a background thread. It checks the configured copy-button targets without interacting with the host clipboard. No runtime UI change or timeout increase was needed. See the defect register for the exact reproduction and regression evidence.

## Performance measurements

These are observed suite durations on the listed host, with concurrent QA activity. They are not per-operation benchmarks or release thresholds.

| Workload | Observed |
|---|---:|
| Successful Inno Setup run in the offline guest, log start to close | 7.46 s |
| Fully executed .NET unit suite | 34 s |
| Ordinary backup and restore test group | 2 min 42 s |
| Deep-path backup and restore test group | 3 min 9 s |
| Packaged owner-tool lifecycle test | 25.2 s |
| New first-run API and persistence scenario | 19 s |
| POS cash posting and same-key replay scenario, including fixture setup | 6.05 s |
| Supplier purchase/payment and host restart scenario, including fixture setup | 18.50 s |
| Operational reporting permissions and read-only scenarios, including fixture setup | 22.85 s |
| Complete integration | 10 min 58 s |

The lifecycle guest took 5.56 s from trial invocation to the first branded loopback login response. Relaunch probes took 4.99 s and 5.11 s. In the final installed scenario, the first loopback login response took 4.39 s, valid login 1.34 s, Arabic name search 265 ms, purchase post 406 ms, credit sale draft 704 ms, sale post 353 ms, daily sales report 194 ms, PDF generation 671 ms, backup creation 1.24 s and restore 749 ms. The restore timing covers the restore request following archive inspection. These are single-run sandbox measurements. Dedicated barcode-search latency, large-database load and performance on supported production hardware remain unmeasured.

## Installer and build identity

Original candidate `E:\The Cashier\artifacts\release-1.1.0-qa-final\output\TheCashier_Setup_1.1.0.exe`, 577,522,774 bytes, SHA-256 `FF3C042C009F6CE5B0842218C7DF7530D3113A167B9C5ABDEC6C0FF26F2C6FA5`. The candidate remains unsigned and was preserved.

| Extracted component | SHA-256 |
|---|---|
| `TheCashier.UI.exe` | `A425D839710D29EAE50D9540EEE12029F190C0F172D5CB0B22A3639642BF886C` |
| `frontend/login.html` | `5F85036AF032A776DD150396A169BC26E7AC7000B0CBCC8701B05890A8335C5B` |
| `x86/SQLite.Interop.dll` | `553693B15BDA4BBF7788BEE09B8BD325FFE4DB151686F432194C58E15B9B4F1E` |
| `AlKhayal.Data.dll`, includes backup/restore | `6E2D54014884FC715C468BADDE2FB475F1A7AF458BF611035D2578E2292612D8` |
| `AlKhayal.Application.dll`, includes application licensing logic | `981E19E4516ED21E6FD844523744698140066AE01C678EEE5C91072232557D26` |
| `WebView2Loader.dll` | `CD2F07E47E98E52472BD744912EDC9E9E645B544F5E612E2F38F89B7ACB1E064` |

These six extracted hashes matched both the original stage and a fresh Release publish. The mark, wordmark and reference badge in the installer also matched the tracked assets byte for byte. See `logo-verification.log`. Archive extraction and packaging checks are separate from runtime compatibility.

The compiler's output-disabled mode still removes an executable in its configured output directory. The QA installer was restored from the pre-check Sandbox copy and its original SHA-256 was verified. The older historical path `artifacts/release-1.1.0/output/TheCashier_Setup_1.1.0.exe` was absent after a syntax check against the default release root. Its presence immediately before QA was unverified and neither of the other installers in the repository matched the historical hash. The compiler also reported `Non-commercial use only` in its banner. The commercial build entitlement for this candidate was unverified.

## Fixes, unverified functionality and decision

This run changed the Trial Dialog regression harness and added a standalone first-run API acceptance test. Existing source changes were retained. Confirmed problems and verification evidence are in the companion defect register. Open gates include authorized production-signed activation and installed acceptance of release-critical business, print, and recovery paths. No approval is issued from automated suite results alone.
