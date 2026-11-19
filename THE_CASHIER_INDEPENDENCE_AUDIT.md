# The Cashier independence implementation and audit

## Original findings
The inherited defaults shared AlKhayal's SQLite file, user and machine AppData containers, trial registry and mutex, license scope, and port 5137. The Cashier installer already had a distinct AppId and install/shortcut names, but reserved 5137.

## Implemented
- Default database: `%LOCALAPPDATA%\AlKhayal\alkhayal.db` → `%LOCALAPPDATA%\TheCashier\thecashier.db`. No database is copied or migrated. Startup still applies the existing schema to the selected database. Backups remain based on the selected database path and default to its `Backups` child.
- Local user storage: AlKhayal → TheCashier for WebView2, credentials, PDF previews, logs, and trial state. Machine storage: `%PROGRAMDATA%\AlKhayal` → `%PROGRAMDATA%\TheCashier` for license and trial security state. Registry: `Software\AlKhayal\Licensing` → `Software\TheCashier\Licensing`. Trial mutexes are now Cashier-specific. A central `CashierStoragePaths` resolver defines database, backup, log, cache, temporary, trial and license destinations. Existing data is left in place.
- Offline request and signed license payload product field: `AlKhayalPOS` → `TheCashierPOS` in the application and bundled License Manager. A legacy AlKhayal license is rejected even when correctly signed for the same hardware. The RSA-3072 SHA-256 PKCS#1 signature and trusted public key mechanism are preserved. The hardware claim hashing algorithm intentionally retains its previous domain string so the fingerprint algorithm is stable. Trial and credential protection entropy now use Cashier-specific labels.
- API default and installer URL reservation: `127.0.0.1:5137` → `127.0.0.1:5138`. Desktop connection and host configuration now use `THECASHIER_SERVER_URL`; database, frontend, and WebView2 overrides use `THECASHIER_*` equivalents. The browser API client uses same-origin relative requests.
- Installer: existing Cashier AppId `{12CF3562-C3F5-42B2-B5C4-48D2B9F3A8E1}`, `{autopf32}\The Cashier`, and Cashier shortcuts retained. Its URL reservation now targets 5138.

## Files modified by this implementation
`src/AlKhayal.Data/Database/{Db,CashierStoragePaths}.cs`; `src/AlKhayal.Data/Licensing/{WindowsTrialStore,FileLicenseStorage,WindowsTrialOperationLock}.cs`; `src/AlKhayal.Application/Licensing/{LicenseFormat,TrialSecurityService}.cs`; `src/AlKhayal.Server.Host/HostOptions.cs`; `src/AlKhayal.UI/{MainForm,WebView2UserDataFolderResolver,DesktopCredentialStore,DesktopPdfOpener,StartupFailureReporter}.cs`; `tools/AlKhayal.LicenseManager/{protocol.cjs,tests/manager.test.cjs,tests/packaged.test.cjs}`; `scripts/TheCashier-Production-1.1.0.iss`; and licensing/integration tests. The repository had unrelated pre-existing uncommitted rebrand changes; they were preserved.

## Validation and remaining work
- PASS: frontend Node suite.
- PASS: direct .NET unit project: 320/320 after updating Cashier WebView2 path expectations.
- PASS: seven License Manager source tests, including RSA signing and product-scoped request grammar.
- FAIL: packaged License Manager launcher test timed out against an existing desktop package outside this repository; that package must be rebuilt from this source.
- PASS: direct .NET unit project: 320/320.
- FAIL: `dotnet test TheCashier.slnx` cannot be opened by installed MSBuild (`MSB4068`). The integration project built and began running, but remained without final output and was interrupted; it cannot be claimed passed.
- FAIL: Windows runtime coexistence of both installed applications has not been exercised. Installer compilation and uninstall coexistence were not executed.
- FAIL: a complete disposable end-to-end activation with the production issuer and production key has not been executed. Rebuild and deploy the authorized License Manager with its existing production signing key, then issue a new Cashier-scoped license. Legacy licenses will require reissue. No production signing keys were changed.

## Acceptance criteria
| Criterion | Result |
|---|---|
| Independent default database and AppData | PASS |
| Independent trial and license storage | PASS |
| Authenticated product scope and AlKhayal rejection | PASS |
| Preserved fingerprint and signature security | PASS |
| Dedicated API port in source and Cashier installer | PASS |
| Complete build/test suite | FAIL |
| Actual installed coexistence and installer/uninstaller validation | FAIL |
| Production-key offline issuance validation | FAIL |
