# The Cashier final acceptance report

**Release decision — RELEASE BLOCKED.** Production-key activation and installed Windows coexistence remain unverified. Keep the installer as a QA candidate.

## Environment and tested revision

- Windows 11 Pro build 26300, .NET SDK 8.0.428, .NET Framework release 533509, VSTest 17.11.2, Node 26.13.1, npm 11.20.0, Inno Setup 6.7.3. The .NET Framework SDK build succeeded via project-level commands. The conventional machine-wide .NET Framework reference assemblies directory was absent.
- Tested base commit recorded upstream baseline plus the pre-existing uncommitted rebrand and independence changes and the Cashier-only changes below. The original remote HEAD reported the same commit via read-only `git ls-remote`. The Cashier checkout has no Git remote.
- Reviewed both preceding reports in full. Their findings are historical. Results here come from this run.
- Evidence logs and individual TRX results were retained locally.

## Changes made during acceptance

- Updated the Cashier desktop project to deploy the production frontend and explicitly publish the x86 SQLite native library. The initial publish omitted both. Final staging contains the login page, frontend assets, WebView2 loader and `x86/SQLite.Interop.dll`.
- Corrected settings-preview examples to show port 5138 and the Cashier backup path.
- Added Cashier acceptance assertions for correctly signed unscoped and expired-subscription formats using a disposable test key.
- Reverted all acceptance-run License Manager source and packaging edits and removed the disposable package after the scope correction. The pre-existing dirty Manager files were preserved. No new activation tool remains, and the original installation and source were left untouched.

## Automated and package results

| Test | Expected | Observed | Status | Method and evidence |
|---|---|---|---|---|
| Desktop Release publish | Stage the UI, frontend and x86 runtime files | Final publish exited 0 with required files present | PASS | `dotnet publish src/AlKhayal.UI/AlKhayal.UI.csproj -c Release -m:1 -o artifacts/release-1.1.0/stage`, `publish-final.log` |
| .NET unit | Full suite completes | 322 discovered and passed, 0 failed, 0 skipped, 31 s | PASS | `dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj -c Release -m:1`, `unit-final.trx`, `unit-final.log` |
| Frontend | Full test runner exits 0 | Registry, renderer, DOM and static checks completed, including final preview change | PASS | `node frontend/tests/run-tests.js`, `frontend-final.log` |
| .NET integration, initial run | Full suite completes | 493 discovered, 491 passed, 2 failed, 0 skipped, 10.18 min. No hang was detected. Both failures were scheduled backup cases with DirectoryNotFoundException in the deep disposable temp directory. | FAIL | `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj -c Release -m:1 --blame-hang-timeout 120s --blame-hang-dump-type none`, `integration.trx`, `integration.log` |
| Backup follow-up | All backup cases pass using normal temporary paths | 17 passed, 0 failed, 0 skipped, 1 m 56 s. Reconstructed scheduled archive temp paths were 274 vs. 215 characters. This supports a long-path diagnosis. No production backup code was changed. | PASS | Same project with `--no-build --filter FullyQualifiedName~BusinessBackupTests`, standard per-user temp, `backup-rerun.trx`, `backup-rerun.log` |
| License Manager source | Seven source tests complete | 7 passed, 0 failed or skipped | PASS | `node --test tools/AlKhayal.LicenseManager/tests/manager.test.cjs`, `manager.log` |
| Final-scope packaged License Manager | Validate the selected owner tool package | A disposable intermediate package passed 8/8 including the offline restart test with Node 24.22.2. That package was withdrawn after the scope correction. The prior external package timeout was not reproduced or conclusively diagnosed. | NOT TESTED | `packaged.log` applies only to the withdrawn package. |

## Offline licensing

`TheCashierPOS` is part of the canonical payload covered by RSA-3072 SHA-256 PKCS#1 v1.5. Cashier requires that product during parsing, validates the signature against its trusted key registry and matches hashed hardware claims. The original source parser at the baseline commit expects `AlKhayalPOS`. The approved trusted public resources match the baseline. There was no comparison to an authorized live issuer's key identity. The owner-side and desktop algorithms were exercised together using synthetic hardware and a disposable key.

| Case | Expected | Observed | Status | Method and evidence |
|---|---|---|---|---|
| Valid Cashier license → Cashier | Accept | Accepted | PASS | `LicensingTests`, `unit-final.trx` |
| Valid AlKhayal license → Cashier | Reject | Correctly signed wrong scope rejected | PASS | `Cashier_request_and_signed_product_are_separate_from_AlKhayal`, `unit-final.trx` |
| Cashier license → original installed AlKhayal | Reject | Original source requires its own scope. Installed behavior unverified. | BLOCKED | No original app was launched against customer data |
| Correctly signed unscoped license → Cashier | Reject | Rejected | PASS | `Correctly_signed_unsupported_license_format_cannot_activate`, `unit-final.trx` |
| Invalid signature → Cashier | Reject | Rejected | PASS | Signature case in `Rejects_tampering_and_unsupported_fields`, `unit-final.trx` |
| Modified signed product ID → Cashier | Reject | Rejected | PASS | Product case in `Rejects_tampering_and_unsupported_fields`, `unit-final.trx` |
| Wrong hardware → Cashier | Reject | Significant device change rejected | PASS | `Another_device_or_significant_change_rejected`, `unit-final.trx` |
| Expired subscription-format license → Cashier | Reject | Signed fixture rejected as unsupported. The supported paid format is perpetual and has no expiry field. | PASS | `Correctly_signed_unsupported_license_format_cannot_activate`, `unit-final.trx` |
| Correct product and hardware → Cashier | Accept and persist | Activation and fresh service instance check succeeded | PASS | `NodeLicenseCompatibilityTests`, `unit-final.trx` |
| Full production desktop activation | Generate request, issue with existing production issuer, import, restart offline | Authorized production issuance and a disposable GUI qualification environment were unavailable | BLOCKED | Test-key .NET/Node round trip and WinForms control tests ran. They did not use production credentials. |
| Hardware algorithm validation | Preserve stable claims and reject wrong devices | Synthetic normalization, missing-identifier thresholds and allowed one-component changes verified. Real WMI stability across physical devices remains untested. | PASS | `LicensingTests`, `unit-final.trx` |

## Storage, localhost and installer

| Test | Expected | Observed | Status | Method and evidence |
|---|---|---|---|---|
| Effective path resolution | Cashier-specific storage and loopback defaults | Staged resolver returned `%LOCALAPPDATA%\TheCashier\thecashier.db`, Cashier backups, logs, cache, temp, trial file and `%PROGRAMDATA%\TheCashier` license and trial security state. Host options returned `http://127.0.0.1:5138`. | PASS | Executable reflection probe against staged assemblies, `path-probe.log` |
| SQLite record persistence | Exercise synthetic catalog, customer, supplier, sale, purchase, user and settings writes | Disposable SQLite and loopback tests exercised those paths. Two scheduled backup cases required follow-up. | PASS | See integration TRX and backup rerun |
| Physical/default profile coexistence | Fresh and existing Cashier/AlKhayal profiles remain isolated, including WAL, SHM, backups and standard-user access | Staged path probe and disposable integration cases ran. Installed profile matrix and before/after legacy fixture hashes remain unverified. | BLOCKED | Isolated Windows launch script creation was rejected by the command environment |
| Fixed ports and runtime resources | Cashier serves 5138 while a mock or original serves 5137, without cross traffic | Existing loopback tests use ephemeral ports. Source shows Cashier 5138 defaults, relative browser requests and Cashier-specific trial mutex and registry identities. Fixed-port socket and dual-application tests remain unverified. | BLOCKED | `HostOptions.cs`, `WindowsTrialStore.cs`, `WindowsTrialOperationLock.cs`, `integration.trx` |
| Installer identity | Separate AppId, folder, shortcut and URL reservation | Distinct AppId `{12CF3562-C3F5-42B2-B5C4-48D2B9F3A8E1}`, Program Files x86 folder, Cashier shortcuts, icon and 5138 URL reservation verified in script. Windows registry entries and runtime ownership need installed verification. | NOT TESTED | Static comparison of the two production ISS definitions |
| Production installer compile | Produce a complete 1.1.0 candidate | ISCC exit 0. Version 1.1.0                                             . Size 577522920 bytes. Authenticode NotSigned. SHA-256 `AE220A54C4AB0F008D1D98E5C983FC83E1CD524E204DA7D132E94642F28F5631`. | PASS | `ISCC.exe scripts/TheCashier-Production-1.1.0.iss`, `installer-candidate.log`, `E:\The Cashier\artifacts\release-1.1.0\output\TheCashier_Setup_1.1.0.exe` |
| Fresh Windows install and GUI smoke | Launch, initialize storage, use login, inventory, sales, purchases, reports, settings, print and restart | Integration and frontend checks exercised constituent behavior. Installed GUI smoke and physical printing remain unverified. | BLOCKED | No isolated installer execution completed |
| Installed coexistence, upgrade and uninstall | Separate original and Cashier apps, preserved data and licenses, no duplicate identities | No isolated installation run completed. No controlled prior Cashier installer was identified. | BLOCKED | Original installers were located by filename only |

## Release security and next actions

- Microsoft prerequisite binaries were copied from the local release cache after checking their signatures and matching SHA-256 before and after copy. The installer pins Cashier's own directory and URL on 5138. No uninstall script deletes shared data. The installer does not remove its 5138 URL reservation on uninstall, so record and verify ownership and cleanup behavior during uninstall qualification.
- Desktop debug symbols, test frontend files and common key/database file patterns are excluded by the installer. The RSA public-key build guard passed. A marker scan of staging found no PEM private key or inline private RSA XML. These checks do not constitute a complete final binary or dependency security review. Native Windows 7/Chromium 109 qualification and support policy remain open.
- On a disposable Windows VM with no production data, install the candidate under a standard-user profile. Check the database and both products' listener bindings with a synthetic AlKhayal fixture and controlled original installer. Verify startup, login, sample transactions, printing, settings, upgrade from an approved prior Cashier build and uninstall with the original app still functional.
- For production activation, have an authorized issuer operator compare the existing issuer's public key ID with the staged embedded trusted key IDs locally. With the VM offline, generate a request from the release UI, transfer it securely to the existing issuer, and issue a test license scoped to `TheCashierPOS` with its already-authorized production key. Import and restart offline. Verify persistence, then test tamper and wrong-product rejection with disposable copies. Record outcomes without storing production request text, fingerprints or signed license bytes in QA logs.
- Sign the distribution artifacts with the authorized release signing process, then recheck Authenticode and record final SHA-256. Keep distribution blocked until the critical checks pass.
