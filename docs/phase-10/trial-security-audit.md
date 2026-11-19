# Offline trial security audit (2026-11-11)

## Implementation update

The audit below records the pre-change three-store design. The implementation now adds `%ProgramData%\AlKhayal\Security\TrialState.bin` as a fourth DPAPI LocalMachine witness and `TrialState.bin.events` as a bounded protected event chain. The new witness is written before the legacy stores; revision-one divergence after an interrupted write is reconciled. An existing installation with valid three-store state and no fourth file migrates using its original trial ID, start, and expiry. Deleting the business database and per-user stores leaves the machine-wide witness for recovery. A surviving event log denies a fresh trial if all four records disappear. A valid permanent license still takes precedence.

The directory permits authenticated users to modify state so ordinary POS processes and Windows accounts can operate without a privileged service. Consequently, LocalMachine DPAPI and these ACLs do not stop malicious local software, an administrator, complete artifact deletion, complete machine snapshot rollback, or a patched executable. The event chain detects partial damage but can be replaced or deleted with the other artifacts. Real Windows 7–11 ACL, crash, and multi-account verification remains necessary before deployment. The original findings and validation figures below are historical; use the implementation report for current test results.

## Actual path and policy

`UI/Program.cs` creates permanent licensing and `TrialSecurityService` before starting WebView2 or the local host. A first eligible launch shows native `ActivationForm`; the operator explicitly starts a trial. `TrialSecurityService.Start` writes a UTC start and `start + 120 hours`, inclusive at start and expired at the exact expiry instant. Reopening never resets start. `MainForm` checks on focus and every minute. `AlKhayalServer` installs the same gate in Web API (`LicenseAccessHandler`) and static-file middleware; every HTTP request rechecks it. A request admitted just before expiry may finish after it. A permanent signature validated for the current hardware takes precedence over all trial state.

The approved current policy permits an optional five-day trial. `docs/design.md` still says no trial at its historical activation paragraph; `docs/phase-10/offline-trial.md` and current code supersede that sentence. The operational documentation should be reconciled before release.

## Persistence and trust

`WindowsTrialStore` holds a protected envelope in SQLite `TrialState`, `%LOCALAPPDATA%\AlKhayal\Licensing\trial.dat`, and `HKCU\Software\AlKhayal\Licensing\TrialState`. It uses DPAPI **LocalMachine**, with a fixed application entropy string, and checks database clear fields against the envelope. This detects casual byte edits and copying to a different Windows installation. It does **not** authenticate the POS executable to DPAPI: software running on the same machine can call Protect with the public entropy, so an adversarial local user with write access to all trial stores can create valid-looking records. The file and HKCU values are user-writable. The business database path is configurable. Three-store redundancy is recovery, not an independent tamper-resistant authority.

The fingerprint hashes the canonical hardware request derived from WMI UUID, board and BIOS serials. A different request fingerprint invalidates an existing trial. Normal restarts should preserve it if WMI identity is stable; substantial hardware repair or unavailable WMI may invalidate it. The permanent license has a separate RSA-3072 SHA-256 signature verified with embedded public keys and requires two matching hardware components. The customer assembly contains public key XML, not the owner private key. No private key was read during this audit.

## Findings

| Severity | Finding and reproduction | Expected / impact | Correction |
| --- | --- | --- | --- |
| P1 | Delete all three artifacts in a disposable installation, then call `Start`: a new trial ID and 120 hours are issued. `TrialSecurityService` treats three missing slots as `NotStarted`. Deleting only the business database is repaired from the other stores. A full reinstall or profile cleanup can make all layers absent. | One trial per device; a user able to remove all local state can reset it. | A durable authority outside user-controlled storage is needed for stronger one-device enforcement. This cannot be guaranteed fully offline against OS administrators or image rollback. Do not change existing state format without a migration and recovery plan. |
| P1 | Restore the same authenticated snapshot to all three stores. The service has no external revision or time authority. A disposable unit test shows a recent replay is accepted. | Old state should not extend access; a full snapshot rollback can. | A separate trusted monotonic authority or signed time is required for strong resistance; either changes the offline trust model. |
| P1 | LocalMachine DPAPI can be invoked by local software; its entropy is in customer code. The same user can write the profile file and HKCU value. | Integrity against malicious local users is weaker than the documentation implies. | Treat DPAPI as corruption detection here. Stronger protection requires a privileged state writer or external authority and a compatibility migration. |
| P2 | The API gate checks at request admission, before controller work. A request admitted just before expiration can commit afterward. | Strict expiry at transaction commit is not established. | Check licensing inside future critical transaction boundaries if the policy requires commit-time expiry; account for in-flight cancellation and idempotency. |
| P3 | `docs/design.md` still describes permanent-only activation. | Documentation contradicts current policy and UI. | Update that historical paragraph when the active design document is reconciled with other ongoing edits. |

No safe code change was made to the licensing format, existing activation data, or transaction semantics. Security tests were extended to make the accepted limits and API enforcement explicit.

## Scenario status

| Scenario | Status | Evidence / limit |
| --- | --- | --- |
| First launch; immediate, 24-hour, 3-day, just-before, exact, after expiry; repeated restarts | PASS | Controlled UTC clock unit tests, including new service instances. Exact boundary is 120 hours. |
| Windows restart and timezone change | PARTIAL | New service/clock instances and UTC-only calculation tested; no real Windows reboot or timezone change was performed. |
| Left open past expiry | PASS | Controlled-clock live loopback HTTP test denies at 120 hours; desktop timer path inspected. |
| Backward/forward/forward-then-back clock; timestamp replay | PARTIAL | Rollback over two minutes invalidates; forward past expiry sticks. Complete recent snapshot replay remains possible. Powered-off clock manipulation cannot be fully detected. |
| One missing layer, database deletion, corrupted file, interrupted write | PASS | Disposable SQLite/file/HKCU integration and unit tests. One missing layer is repaired; corrupt envelope invalidates. |
| All-layer deletion, reinstall, old backup restore, config replacement | FAIL / PARTIAL | All-layer deletion resets trial. Complete snapshot replay possible. Reinstall/config effects inferred from storage locations; no installer VM exercised. |
| Different device, another Windows account, hardware repair | PARTIAL | Fingerprint mismatch unit test invalidates; separate account and physical hardware cases were not exercised. |
| Permanent activation, wrong-device/corrupt signatures, issuer separation | PASS / PARTIAL | Existing signing/installation tests and source inspection; no production key or real device was used. |
| Direct API reads and representative business mutations after expiry, existing session | PASS | Loopback integration denies GETs and POSTs before authentication/controllers; middleware uses same gate per request. |
| Concurrent startup/requests, permission denial, power loss, expiry mid-save | PARTIAL | Mutex and revision recovery inspected; strict commit-time expiry is absent. No real power loss or multi-process stress run. |

All simulations use fake clocks, disposable SQLite files and HKCU test keys. No operational database, license, trial state, system clock, or owner signing key was touched. Offline state cannot provide strong protection against a local administrator, full image rollback, or a patched customer binary. Windows 7 and legacy WebView2 compatibility still require real deployment testing.

## Validation run

- Focused `TrialSecurityTests`: 10 passed; focused trial/license integration selection: 4 passed.
- Full Release solution tests: 285 unit tests passed; integration tests 472 passed, 1 failed. The failure is unrelated to licensing: `InventoryPersistenceTests.Stock_reports_purchase_receipt_and_return_show_original_sources_and_remaining_cost` expects route `purchase-management` but receives `purchase-details`. The affected stock report files were already modified in the working tree before this audit and were not changed here.
- `node frontend/tests/run-tests.js`: passed, 132 registered screens.
- `dotnet build AlKhayal.slnx -c Release --no-restore`: passed, zero warnings/errors after the test process ended.
- `git diff --check`: passed (line-ending notices only).
