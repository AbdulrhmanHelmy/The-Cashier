# The Cashier 1.1.0 production blockers report

**Release decision — PRODUCTION BLOCKED.** The scheduled-backup defect and the Cashier owner-package collision were remediated. Production-key activation and the installed Windows lifecycle remain unqualified. The full unit run also has one native dialog timeout. Keep the rebuilt installer in QA.

## Baseline and environment

- Repository `E:\The Cashier` on `main`, initially clean and tracking `origin/main`. Starting and final HEAD `0834db5770fa063f75bd626a925012fe490aa2c5`. The remediation and this report are local working-tree changes.
- Read `THE_CASHIER_INDEPENDENCE_AUDIT.md` and `THE_CASHIER_FINAL_ACCEPTANCE_REPORT.md`. Their test results are historical and are distinguished from this run.
- Windows 26H2 build 26300.9457, .NET Framework 4.8 release 533509, .NET SDK 8.0.428, VSTest 17.11.2, xUnit adapter 4.0.1, Node 26.13.1, bundled owner runtime Node 24.22.2, Inno Setup 6.7.3. The test shell used a non-elevated account. Host `LongPathsEnabled=0`.
- Windows Sandbox feature state was installed and the Hyper-V/container services were running. Two combined automation commands were rejected with `blocked by policy`. A separate launch of the offline, folder-mapped configuration started the inbox launcher but produced no Sandbox window or guest identity marker. The `.wsb` file association and the newer `wsb` CLI were unavailable. The launcher was stopped and the installer trigger was never created. No installer or uninstaller ran on the host. [Microsoft Windows Sandbox configuration](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file)
- Synthetic databases and logs were created in disposable test directories. Raw logs and TRX files are under the Codex workspace `work` directory. The original AlKhayal installation, data, license state and repository were treated as read-only.

| Baseline artifact | SHA-256 |
|---|---|
| `artifacts/release-1.1.0/output/TheCashier_Setup_1.1.0.exe` | `AE220A54C4AB0F008D1D98E5C983FC83E1CD524E204DA7D132E94642F28F5631` |
| `artifacts/release-1.1.0/stage/TheCashier.UI.exe` | `112CF353E5990439F0F0D79F4A6A5D6EF0750486F87B2CFDE65BBD5927FD50C7` |

## Production licensing

**BLOCKED.** An authorized production issuer operator, a production signing session approved for this product, and an isolated Windows desktop were unavailable. No production private key was accessed or generated. The actual desktop activation request, production issuance, import, physically offline restart and customer-facing workflow remain unverified.

Implementation review traced the WinForms startup gate, request writer, hardware claims, issuer parser, RSA signer, desktop parser, trust registry, hardware comparison and durable file storage. The issuer signs the canonical UTF-8 payload beginning `ALKLIC|1|TheCashierPOS|` with RSA-3072/SHA-256 PKCS#1 v1.5. The desktop parser requires the product in the signed data, then the service verifies those payload bytes against the trusted key ID before checking hardware claims and persisting the license. Source defaults put Cashier data under `%LOCALAPPDATA%\TheCashier` and machine license and trial state under `%PROGRAMDATA%\TheCashier`. Source and automated results do not qualify production signing or hardware behavior.

| Real acceptance case | Expected | Status and evidence |
|---|---|---|
| Genuine Cashier license in Cashier | Accept | BLOCKED. Authorized production issuance and isolated target unavailable. |
| Genuine AlKhayal license in Cashier | Reject | BLOCKED. Disposable genuine legacy sample and isolated installed Cashier unavailable. Source-level tests exercise correctly signed wrong-product data with a test key. |
| Genuine Cashier license in AlKhayal | Reject | BLOCKED. Authorized Cashier issuance and disposable genuine AlKhayal runtime unavailable. |
| Legacy unscoped license in Cashier | Reject | BLOCKED. Genuine fixture and isolated installed target unavailable. A signed test-key fixture exercised the parser. |
| Wrong-hardware Cashier license in Cashier | Reject | BLOCKED. Authorized issuance and controlled hardware pair unavailable. Synthetic claim tests ran. |
| Tampered Cashier license in Cashier | Reject | BLOCKED. Production QA base license and isolated installed target unavailable. Test-key tamper cases ran. |
| Expired Cashier license in Cashier | Reject | BLOCKED. No genuine expiring production format/fixture and isolated target. Current accepted paid format is perpetual. A signed disposable subscription-format fixture is rejected as unsupported. |
| Valid Cashier license after offline restart | Accept | BLOCKED. No production issue/import/restart cycle in an offline guest. Owner-tool restart evidence covers its separate registry only. |

### Packaged owner tool

**PASS for disposable-key package verification.** The external AlKhayal package contains only `AlKhayalPOS` in its protocol. Its different storage resolver fails when a test data override conflicts with the pinned deployment path. Its catch handler starts a blocking VBScript error dialog even in headless mode. This explains how a startup error can surface as a readiness timeout. The historical exit was unavailable and the original package was inspected read-only. The repo deployment script also targeted the AlKhayal folder, shortcut and default owner data. The new Cashier package has separate folder, shortcut, data override, session identity and startup log. In headless mode it reports a startup failure to stderr and exits. The staging option exercises the deployment allowlist without installing a shortcut or opening original state.

The package version is 1.0.0. It was staged in the Codex workspace at `work\cashier-manager-final`. The real packaged Node executable ran `launch.cjs` under a disposable profile. The package rejected an `AlKhayalPOS` request, issued a verifiable Cashier-scoped license, reused one process, retained its registry across restart, and denied private-key HTTP access. A synthetic startup misconfiguration produced the expected stderr and exit 1. The offline guard restricts the Node HTTP/fetch paths to loopback. The interactive VBS shortcut/browser launch is NOT TESTED. The production-key configuration is BLOCKED.

| Owner package component | SHA-256 |
|---|---|
| `runtime/node.exe` | `1C331340FA6CAC5E4294725959070A143FA0507195A78AD77EED7943F719AAC9` |
| `launch.cjs` | `C388882A16B9EB96239F39DDB613DE62BE8D2EEF45E793282B76D12149C8CC31` |
| `The Cashier License Manager.vbs` | `E802FBAD940AE597FA267EF348B582D5829A00C667F188D8B3E937E03EFD7767` |

## Windows installation and coexistence

The reconstructed installer script records AppId `{12CF3562-C3F5-42B2-B5C4-48D2B9F3A8E1}`, the Program Files x86 subdirectory `The Cashier`, Cashier Start Menu and optional desktop shortcuts, and the loopback URL reservation on port 5138. The AlKhayal installer source uses its own AppId `{C6042F26-9F4C-42D4-B1BE-377779271777}` and reserves port 5137. The browser calls same-origin relative paths. Cashier's installer has no explicit uninstall cleanup for its URL reservation. Its intended retention leaves the separately stored Cashier database, backups and licensing state after uninstall. Retention and residual URL ownership require installed verification.

| Acceptance item | Status and evidence |
|---|---|
| Independently inspect actual installer archive and staged payload | PASS. Innounp 2.67.11 listed the original and rebuilt archives and extracted their payloads and installer metadata. The rebuilt executable, backup assembly, login HTML, x86 SQLite interop library and x86 WebView2 loader all matched the stage hashes. The extracted backup assembly also matched the integration test binary. See `installer-latest-archive.log` and `artifact-verification.log`. [Innounp source and supported versions](https://github.com/jrathlev/InnoUnpacker-Windows-GUI) |
| Fresh install, installed UI launch, WebView2 assets, SQLite load, AppData and request creation | BLOCKED. No independently verified guest initialized. No install log or installed-process evidence was generated. |
| Installed shortcut, registry, uninstall identity and 5138 listener | BLOCKED. Guest not established. |
| AlKhayal and Cashier running together, ports, mutex, IPC, AppData, databases and synthetic record isolation | BLOCKED. No isolated environment with a disposable genuine AlKhayal installation. Actual AlKhayal port use and file access were not measured. |
| Upgrade, data counts, migrations, settings and activation persistence | BLOCKED. No approved genuine prior Cashier build or persistent isolated baseline was established. Upgrade from a build using the shared AlKhayal data or license paths is unsupported for this candidate's release gate. It has no automatic cross-product migration. |
| Uninstall with both applications present and original file/license/shortcut/registry checks | BLOCKED. No isolated dual-install environment. Runtime and Cashier retention behavior remain unverified. |

## Backup diagnosis and changes

Baseline source at HEAD, instrumented only in a disposable source copy, failed both original scheduled tests at 272- and 276-character pending archive paths. Both traces show `System.IO.DirectoryNotFoundException` opening the `.alkbackup.tmp` archive in `FileStream.Init → FileStream..ctor → SqliteBusinessBackups.Publish (line 401) → RecordBackup → RunScheduledAsync`. This is a production file-I/O defect under legacy Windows path handling. Moving the whole test runner's TEMP to a still deeper path separately prevented xUnit assembly discovery. That runner failure is distinct.

The ordinary temporary root measured 32 characters without its trailing separator. The corrected deep suite targets a 128-character synthetic temporary directory with spaces and Arabic characters. The fixture roots are 178 characters and the scheduled archive temporary paths are 307 and 311 characters. At that depth the old SQLite workspace snapshot plus `-journal` reached 261 characters. With the archive fix only, `System.Data.SQLite.SQLiteException: unable to open database file` occurred in `SQLite3.StepBackup → SQLiteConnection.BackupDatabase → Copy → Publish`. Test-only reads and recursive cleanup also hit `DirectoryNotFoundException` on published archives. These findings involve production archive I/O, SQLite's native path budget and test harness behavior.

The service uses extended Windows paths for validated local archive operations, including enumeration, recovery, download, atomic publication, removal and retention. Configuration and returned archive paths keep their usual absolute format. The private SQLite files use short names in the existing GUID workspace, with cleanup for both old and new names. The service rejects a workspace that cannot fit the SQLite rollback journal with a message to choose a shorter live database location. Archive format, staging on the selected backup volume, SQLite online backup and integrity checks, restore contract and per-installation retention criteria are preserved. The backup fixture override leaves the runner TEMP unchanged, and fixture reads and cleanup can handle long archive names.

| Backup acceptance item | Status and evidence |
|---|---|
| Both original scheduled tests, deep path including spaces and Unicode | PASS. `Schedule_restarts_retries_retains_seven_and_preserves_manual_backup` and `Schedule_recovers_a_published_archive_when_local_status_write_was_interrupted` both passed in `deep-schedules-final.log` (2/2, exit 0), and in the full deep backup rerun. |
| All backup tests at ordinary and deep paths, including native database integrity and synthetic restore | PASS. 17/17 at each depth, exit 0 in both `backup-latest-standard.log` and `backup-latest-deep.log`. Tested archive contents, downloads, database integrity, restore of synthetic products and users, protected trial state, and session stripping. |
| Non-elevated account, missing destination, scheduled retry, concurrent schedule, interruption of status persistence, retention and pending-file cleanup | PASS. The backup suite creates the missing destination, exercises a configured directory replaced with a file, asserts one successful concurrent run, recovers a published archive after reverting scheduler metadata, keeps seven owned automatic archives and the manual archive, and checks for zero pending files. |
| ACL denial and unsupported live database workspace | PASS. `qualification-final.log` verifies a non-elevated ACL write denial, recorded failure, empty destination, unchanged source SHA-256 and successful retry after permission restoration. A 220-character live database path exceeding the workspace budget produced the explicit path error and left the source SHA-256 and destination intact. |
| Retention leaves another installation's archived backup untouched | PASS for a second independent disposable installation ID in the final schedule test. Actual AlKhayal backup access remains BLOCKED with coexistence. |
| Actual long Windows username, disk-full fault, and interruption during an active file write | NOT TESTED. Deep path pressure and post-publication interruption were verified separately. |

## Regression and release candidate

| Command or evidence | Status |
|---|---|
| `node frontend/tests/run-tests.js`, `frontend.log` | PASS. Exit 0, all runner sections reached their successful assertions. |
| `node --test tools/AlKhayal.LicenseManager/tests/*.test.cjs`, `packaged-qualification.log` | PASS. Exit 0, 8/8 including 7 source tests and 1 packaged lifecycle test. |
| `dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj -c Release`, `unit-latest.log` and TRX | FAIL. Final serialized run exited 1, with 321/322 passed. `TrialDialogTests.Original_activation_starts_trial_once_and_expired_actions_use_existing_form_and_contacts` exceeded its 30-second join. The same test timed out before changes. All 39 tests in `LicensingTests` and `NodeLicenseCompatibilityTests` passed. |
| `dotnet vstest AlKhayal.IntegrationTests.dll`, all integration tests | PASS. Final build exited 0, 493/493 passed, 0 failed, 0 skipped. Duration 11 min 13 sec. See `integration-latest.log` and its TRX. The tested backup assembly matches the packaged SHA-256. |
| `dotnet publish src/AlKhayal.UI/AlKhayal.UI.csproj -c Release -m:1` and production ISCC script | PASS. Release publish and ISCC both completed with exit 0. ISCC used the `ReleaseRoot` override to keep the original candidate in its original location. `release-stage-latest.log` and `package-latest.log`. |
| Local Git working tree | PASS. `git diff --check` exited 0. The branch retains the baseline HEAD and contains only the local backup, owner package, test, installer build option and documentation changes. See `git-final.log` for exact paths and status. |

### Remediated artifact

- Installer `E:\The Cashier\artifacts\release-1.1.0-qa-final\output\TheCashier_Setup_1.1.0.exe`
- Installer SHA-256 `FF3C042C009F6CE5B0842218C7DF7530D3113A167B9C5ABDEC6C0FF26F2C6FA5`
- Product version 1.1.0. Size 577,522,774 bytes. Authenticode status `NotSigned`.
- Packaged `TheCashier.UI.exe` SHA-256 `A425D839710D29EAE50D9540EEE12029F190C0F172D5CB0B22A3639642BF886C`
- Packaged and tested `AlKhayal.Data.dll` SHA-256 `6E2D54014884FC715C468BADDE2FB475F1A7AF458BF611035D2578E2292612D8`

## Remaining release gates

Production requires authorized production-key issuance and the real installed offline activation matrix, followed by isolated installation, coexistence and uninstall with a disposable original build and an upgrade from an approved prior Cashier build if one exists. Include any outstanding automated failures in the release decision. The rebuilt candidate was kept local and unsigned, and nothing was pushed or published.

