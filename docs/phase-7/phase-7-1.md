# Phase 7.1 — Employees, optional attendance and cashier shifts

Implemented directly in the existing checkout on 2026-10-29. Initial branch: `main`; initial working tree: clean. No delegation, external coding worker, OpenCode invocation, commit, push, merge or branch change. Phase 7.2/7.3 implementation is excluded.

## Architecture and employee data

`Application/Workforce` contains contracts, validation, exact money parsing and canonical operation fingerprints. `IWorkforceStore` is the persistence boundary. `Data/Workforce/SqliteWorkforceStore` implements short-lived connections, parameterized queries, immediate writer transactions, concurrency checks and source association. The thin `WorkforceController` uses the existing session, CSRF, role, screen-access and feature services. Composition is in `AlKhayalServer`/`ApiBootstrap`; no package or target-framework change.

Only employee name is required. SQLite AUTOINCREMENT allocates the immutable identity; the displayed unique code is `EMP-` followed by at least six digits. Allocation is safe under concurrent creation; codes are never renumbered or reused. Creation UTC, version 1, active status and attendance opt-out are automatic. No login user or credentials are created. An optional unique `LinkedUserId` references an existing account and grants no permissions; one login cannot belong to two employees.

Optional fields: phone, job title, hire date, address, notes, linked user, salary, salary frequency, expected payday and compensation notes. Empty salary and empty payday remain null. Salary uses the existing `MoneyPolicy`: signed-64-bit scaled storage, nonnegative values and at most two entered decimal places. Frequencies are unspecified/monthly/weekly/other. Payday is an optional calendar day 1–31, with contextual details in compensation notes; it does not promise a payment on short months. The original Phase 7.1 implementation treated salary as informational; migration 076 and the addendum below now implement persistent advances and monthly salary payouts. Insurance and tax calculations remain out of scope.

Employee edits require the current version. Activate/deactivate through the employee editor. Deactivation or attendance opt-out requires closing an existing attendance first. Physical employee deletion is prohibited, including employees without history, to preserve stable codes and references. Employee management, including its read endpoints and compensation data, is administrator-only; cashiers cannot read employee or salary projections.

## Attendance

Attendance is independently optional globally and per employee. An administrator records check-in/check-out using server UTC. A second open attendance or any overlapping interval is rejected under the writer lock; a unique partial index also protects active attendance. Check-out must follow check-in. An administrator may correct start/end (including a closed record), with current version and a required reason. The immutable audit retains actor, time, previous interval and replacement interval; it is available in the UI's سجل التعديلات and a paginated API.

Correction timestamps require `Z` or an explicit numeric offset and normalize to UTC ISO format. An empty corrected end means open attendance; overlap protection still applies. Cross-midnight sessions are ordinary intervals. Worked seconds are calculated per interval, ending at server time for open records. Daily/date-range history selects intervals overlapping the range, using an exclusive end boundary. Durations describe the full matching session, not clipped calendar-day allocations or payroll hours. No lateness/absence is calculated: work schedules are unsupported.

All displays and date-input boundaries use the existing application convention: the workstation/browser's configured local timezone. There is no separate business-timezone setting in this checkout. Configure Windows/browser timezone to the business location. Stored times remain unambiguous UTC; no fixed Cairo offset is embedded. Attendance never opens/closes a cashier shift.

## Shift lifecycle and permissions

`advanced-shifts` enables the optional cashier workflow. Current POS users or users granted `shifts-list`, `shift-add`, `shift-reports` or `shift-details` may operate their own shifts; every cashier list/detail/mutation is constrained to the authenticated user. Administrator can inspect all shifts and open/close/adjust on behalf of another active cashier or administrator. Opening/closing actor and responsible cashier are retained separately.

The supported deployment in this phase is one standalone register, identified server-side as `standalone`. A browser cannot submit a fabricated device/register identity. Partial unique indexes prevent overlapping open shifts for a cashier or register. This is not multi-terminal LAN identity/support. Each shift stores opening cash/time/notes; closing requires nonnegative counted cash and stores expected cash, counted cash, closer, timestamp and optional notes. Negative difference is shortage; positive difference is excess. Closed shifts, opening identity and associated events are immutable. Events cannot be added after close.

In the original Phase 7.1 UI, employee and attendance screens were in Settings and the operational sidebar contained cashier shifts. The workforce addendum below moves employee records, payroll, advances and attendance into their requested operational sidebar section; Settings no longer duplicates these routes. Module switches remain in the existing feature settings. `employee-management` and `employee-attendance` now default enabled for new/unconfigured installations; explicit saved feature choices are retained. `advanced-shifts` remains independently optional/off by default. Disabling attendance/employee management with open attendance or shifts with an open shift is rejected; close the workflow first. These checks serialize with new opening/check-in under the SQLite writer lock. Disabling never deletes data.

## Cash calculation and source policy

Expected drawer cash = opening cash + linked cash sales + linked invoice cash collections + explicit drawer cash-in − explicit drawer cash-out.

Source association runs only when a **new** sale is posted or a **new** invoice payment is accepted, inside its existing transaction. It uses authenticated posting/collecting actor, active shift identity, source IDs and current UTC event time. Invoice/document business dates may be earlier; they never assign historical documents by date. An operation replay returns before association and cannot duplicate receipts or links. Closing, source posting and adjustments serialize on the same SQLite writer lock: an event is either included before close, or the later document is left unassigned. Failures roll back sale, inventory/FIFO, payment, treasury, source link and shift event together.

Policy is deliberately optional: **an active shift is not required to post a sale**. Both standard sales and Touch POS use this same Application/Data boundary. Only the posting actor's current shift receives new activity. Existing invoices, other cashiers' activity, read operations and documents posted without an active shift remain untouched/unassigned. A replay after opening another shift never reassigns a historical invoice.

| Activity | Drawer treatment |
| --- | --- |
| New posted cash sale | Exact final collected invoice amount; existing `إيراد الوردية` treasury deposit remains unchanged. |
| New posted credit sale | Linked/countable gross credit sale, **zero cash**. |
| Invoice collection with `Method=cash` | Exact accepted payment, collector's active shift, unique payment/source link. |
| Invoice card/transfer payment | No drawer cash/event. |
| Customer statement/legacy general collections | Excluded: that boundary does not establish an authenticated drawer/payment-method source. |
| Sales cash refunds | Excluded: existing implementation pays from independent `مرتجعات البيع`, not this drawer. No refund is invented. |
| Supplier payments, purchases, expenses and arbitrary treasury transfers | Excluded: account activity alone is not proof of physical movement in this drawer. |
| Explicit drawer cash-in/out | Required positive amount and reason; immutable event/actor/time, operation key and atomic audit. |
| Opening/counting/closing | Physical declarations/reconciliation only; no new treasury deposit/withdrawal. |

Drawer adjustments do **not** post a second treasury movement or customer/supplier entry. They represent an explicitly documented physical drawer movement; operators must not use them to represent an unsupported salary, expense, refund or automatic bank transfer. Outflows cannot exceed expected drawer cash. They do not rewrite existing treasury balances. Credit totals and sale counts are gross posted source activity, not net revenue after later cancellations/returns; they never affect expected cash. The UI labels the credit summary accordingly.

Closing expected cash is snapshotted in `ClosingExpectedScaled` in the close transaction. Immutable event sums reproduce it. Counts and all amounts are computed without floating point in the server. JavaScript displays server money strings and submits strings, avoiding numeric precision loss.

## Database and migration coordination

New embedded additive migration: `071_phase7_1_workforce.sql`. Existing migrations end at 031 in this checkout. Numbers 032–069 are left available for parallel Phase 6 work; no unseen Phase 6 schema is assumed. Full resource filenames are the existing migrator identity. The phase-qualified filename and independent tables avoid reusing an observed number/name. An unseen branch can still choose the same allocation; during merge, coordinate filenames and table names before release, and never renumber a migration already deployed. Phase 7.1 has no dependency on a future Phase 6 migration and does not modify existing migration files.

Tables: `Employees`, `EmployeeAttendance`, `CashierShifts`, `CashierShiftEvents`, `WorkforceOperations`, `WorkforceAudit`. Foreign keys restrict historical references. Indexes cover status, dates, employee history, cashier dates, open-state uniqueness, source uniqueness, shift event order, covering shift totals and audit entities. Immutable triggers protect source events, operations, audit, closed shifts and opening declarations. No existing financial/history rows are backfilled, reassigned, reset or renumbered.

The normal operator database `%LOCALAPPDATA%\AlKhayal\alkhayal.db` and repository seeded database were not opened for writing, migrated, reset or reseeded during this work. Tests construct disposable `%TEMP%` database paths and explicitly pass them to the real host. New workforce tests reuse the isolated `TreasuryPersistenceTests.Fixture`, whose root is `%TEMP%\alkhayal-treasury-<GUID>\test.db`. Production applies 071 only on the operator's next normal updated-host startup; back up according to the existing deployment policy.

## HTTP contract

All paths below start with `/api/workforce`; all use the existing `{ success, data, error }` envelope, same-origin cookie and `X-CSRF-Token` for POST.

| Method/path | Contract |
| --- | --- |
| GET `employees` / `employees/{id}` | Admin only; paginated list/detail including optional compensation. |
| POST `employees/save` | `id=0` for create, otherwise `id` + current `version`; required name and operation key. |
| GET `attendance` | Admin only; paginated overlapping interval history. |
| POST `attendance/in` | Employee ID + operation key; enabled active employee. |
| POST `attendance/out` | Attendance record ID + operation key. |
| POST `attendance/correct` | Record ID, version, explicit-offset start/end, required reason, operation key. |
| GET `attendance/{id}/audit?page=1` | Admin-only audit, 50 rows per page. |
| GET `shifts` | Owner-scoped cashier history; administrator all. |
| GET `shifts/current` | Current user's open shift, or empty data. Administrators receive the open standalone-register shift across cashiers so the shared header reflects the active device shift; cashiers remain scoped to their own shift. |
| GET `shifts/{id}` | Owner-scoped detail/summary; administrator all. |
| GET `shifts/{id}/events?page=1` | Owner-scoped immutable source/movement rows; 50 per page. |
| POST `shifts/open` | Amount string, optional notes; admin may select cashier ID. |
| POST `shifts/close` | Shift ID, counted amount string, optional notes, operation key. |
| POST `shifts/adjust` | Shift ID, positive amount, direction `in`/`out`, required reason, operation key. |

List query: `search`, `status`, `employeeId`, `cashierId`, `fromUtc`, `toUtc`, `page`, `pageSize`, `discrepanciesOnly`. Page size defaults to 25 and clamps to 1–100. Date ranges are UTC instants, exclusive end. Employee status uses active/inactive; attendance and shift status use open/closed. Workforce operations share globally unique keys and actor-inclusive canonical hashes. Matching retries return the original entity ID; changed payload/actor conflicts. No hashes/credentials are exposed. Busy/SQLite failures return Arabic retry guidance; clients retain an uncertain command/key and disable editing until confirmed.

## Routes and UX

`employees-list` → persisted employee directory; `employee-add` → hidden contextual create editor; `attendance` and `attendance-history` → filtered persistent records and manual audit; `shifts-list` → history; `shift-add` → current/open workflow; `shift-reports` → paginated summaries and discrepancy filter; `shift-details` → contextual summary and event history. `shift-details` inherits `shifts-list` permission. Direct details without a selected ID offers history. `shift-approval` and `analytics-shifts` are hidden compatibility redirects; no unsupported approval operation is presented.

Touch POS and standard sales mount a compact current-shift action only when enabled. It opens/reconciles the current cashier's shift and permits documented drawer adjustments. Current shift, history, detail, summary and discrepancy report are presentations of the same persisted source. No second viewport/scroll owner, new font, framework, CDN or browser API newer than Chromium 109 was introduced. Existing compact modal/focus-trap and navy/gold Light/Dark tokens are reused. Search fields stay mounted; sequence/generation checks reject stale responses. Mutations disable repeated submissions, retain inputs after errors and preserve the operation key after uncertain results.

## Validation and remaining manual acceptance

Commands:

```powershell
dotnet test AlKhayal.slnx --no-restore -m:1 --logger 'console;verbosity=minimal'
dotnet build AlKhayal.slnx --no-restore -m:1 -p:OutDir=D:/projects/AlKhayal/.tmp/phase-7-1-validation/
node frontend/tests/run-tests.js
Get-ChildItem frontend/js -Recurse -Filter *.js | ForEach-Object { node --check $_.FullName }
```

Workforce persistence coverage: name-only/optional compensation, linking without credentials, edit/deactivate/version conflicts, concurrent codes/retry, opt-out, duplicate/overlapping attendance, cross-midnight correction/duration/UTC persistence/audit, exact/short/excess close, overlapping shift open, immutable close/events, restart, ownership, payment-method separation, source replay, cash adjustments/reconciliation and injected late rollback of sale/payment/inventory/treasury/close. Additional races cover concurrent open/adjustment retry and sale versus close. HTTP coverage uses real OWIN cookies/CSRF, optional modules, open-workflow disabling, Admin/Cashier ownership and compensation privacy. Unit tests cover money boundaries, canonical replay, optional fields and ambiguous/reversed/future corrections. Frontend tests execute employee/shift forms, retry loading/failure, mounted search, stale-response rejection and registry integration.

Initial full run found legacy hardcoded migration counts and historic fixtures applying newly discovered future scripts; expectations/setup now correctly distinguish pre-migration fixtures from fully migrated 32-script databases. An existing customer statement test depended on the current UTC day being before 2026-10-29; filter boundaries now derive from its actual fixture transaction dates. No customer accounting behavior changed. A subsequent full run passed 210 integration/74 unit tests before the final two concurrency/payment rollback regressions were added. Final full run: **212 integration tests and 74 unit tests passed** (0 failed/skipped); the frontend suite passed across **104 registered screens**, and every frontend JavaScript file passed `node --check`.

Full solution build (including WinForms/Printing) passed with **0 warnings and 0 errors** using the isolated output directory above. The initial ordinary-output build failed with MSB3027/MSB3021 because the already-running `AlKhayal.UI` process locked copied assemblies. It was left running and was not restarted against the operator database. Close the desktop and rebuild/relaunch normally when ready to activate the new binaries and additive migration. `git diff --check` passed.

Remaining manual acceptance: actual WinForms/WebView2 interaction, RTL Light/Dark visual layout at 1366×768 and ~900×600, keyboard/Arabic IME, DPI transitions, legacy Chromium 109/Windows 7 runtime and printer hardware. These were not claimed as executed. No separate configured business timezone, employee work schedules, overtime, tax or insurance calculations, multiple registers, shift-required-sales policy, general ledger collection assignment or drawer-funded refund is implemented. The workforce payroll addendum provides fixed monthly salary/advance settlement and Treasury cash movements, not full accrual accounting. Native/direct printing is intentionally absent.

## Phase 7.2 handoff

Use authenticated paginated shift/detail/event contracts to assemble a report snapshot. Keep compensation out of cashier reports. Invoke native C# printing through the existing printing abstraction (`IReceiptPrinter` or a deliberately extended output port); controllers remain thin and native printer code stays outside Application/UI business rules. Desktop and future Browser Mode must share the server-side native print boundary.

Phase 7.2 must implement direct Windows printer output with printer-brand support, flexible paper sizes, configurable templates and selection of printer/template/paper in settings. No browser preview, `window.print()`, or PDF printing path is to be added for these reports. Printer errors/retries must never reopen, modify, duplicate or roll back a committed shift/sale. Report-print authorization must respect owner scope and admin access; printing must read a committed snapshot and report hardware failures separately. No Phase 7.2 or barcode-label implementation is included here.

## Workforce payroll and breaks

Migration `076_workforce_payroll.sql` is additive. It adds a unique nullable NationalId to existing Employees, cumulative BreakMinutes to attendance rows, EmployeeAdvances, EmployeePayrollPayments and EmployeePayrollAdvanceAllocations. Existing employee identities and attendance records are preserved. National IDs are unique when present. Payroll is unique per employee/month, and every saved advance can be allocated to at most one payroll payment. Immutable triggers protect advances, payout history and allocations. Employee “delete” is soft archive/deactivation so employee and payment foreign keys remain valid.

Application validation requires operation keys, positive advance amounts and a valid `YYYY-MM` payroll period. Salary is read from the saved Employee row. The client cannot submit gross/net figures. At payout, the server sums advances without an allocation, rejects advances exceeding the salary, computes nonnegative net, writes a payroll payment and allocates those advances exactly once. The payout period can be paid once. Idempotent retry returns the original payroll ID; changed payload under the same key conflicts.

Advances and payroll cash payments require an active Treasury account. Both call the existing Treasury writer inside the same SQLite transaction used for workforce persistence. Advance posting debits its full amount at issue; payroll debits only gross salary less previously issued advances. If the selected account has insufficient funds, Treasury validation fails and rolls back the workforce row, allocation, operation record, audit and any account version/movement change. A zero net payroll has no cash movement but still records the period and settles the allocated advances. Treasury movement IDs are stored on advance/payroll records for reconciliation.

Check-in/out timestamps and transaction IDs are server generated. `attendance/break` adds a positive duration of 1–1440 minutes to an open attendance row; cumulative break time is limited to 1440. Worked seconds are elapsed interval seconds less saved break minutes, floored at zero. Repeated breaks are separate idempotent operations and appear in attendance audit. The UI shows generated `EMP-`, `ADV-`, `PAY-`, `ATT-` and Treasury movement identifiers. Arabic-Indic and Persian digits in national-ID input are normalized to Latin digits before validation/persistence.

Routes are now grouped in the operational sidebar under **إدارة الموظفين**: employee directory/add, salary payout, advances, and attendance. Cashier shift workflows remain separate. The Settings hub does not duplicate these routes. Payroll and advances are administrator-only and use the existing `employee-management` feature flag; attendance retains its separate `employee-attendance` gate. Both feature defaults are enabled for new/unconfigured installations; an explicit saved feature choice is preserved. The payout UI selects the source Treasury account and displays the persisted payroll and advance history.

| Method/path | Purpose |
| --- | --- |
| POST `/api/workforce/employees/archive` | Version-checked soft archive; history stays linked. |
| GET/POST `/api/workforce/advances` | Read saved advances / post an advance with Treasury withdrawal. |
| GET `/api/workforce/payroll` | Read saved payroll history. |
| POST `/api/workforce/payroll/pay` | Post one employee/month payout from the selected Treasury account. |
| POST `/api/workforce/attendance/break` | Add break minutes to an open attendance record. |

All routes require an authenticated administrator and POSTs require CSRF. Money is stored as integer scaled values; client numeric values are used only for display previews, while API/store calculations are authoritative. No tax, insurance, overtime, prorating, or automatic monthly recurrence is implemented. A salary payout is a Treasury withdrawal and workforce record, not a full general-ledger accounting module.

Validation on 2026-10-31: `node frontend/tests/run-tests.js` passed across 128 registered screens, including employee, advance, payroll and break request handlers. All 37 frontend JavaScript files passed `node --check`; `screens.json` parsed and `git diff --check` passed. The .NET build and C# integration tests could not be validated here: the environment has no .NET Framework 4.8 targeting pack and lacks restored NuGet assets (`MSB3644` / `NETSDK1004`). The C# persistence and HTTP regressions were added but remain unexecuted until those prerequisites are available.

## Changed areas

New Application/Workforce contracts/service; Data/Workforce store and migration 071; Workforce API controller, host composition and resolver; feature registry plus lifecycle disable guard; two small hooks in existing sales posting/invoice collection; workforce frontend module/CSS, shell load/implemented routes, current-shift mount and canonical registry; workforce unit/integration/HTTP/frontend tests, existing migration fixture/count updates and the date-independent customer test. Shared architecture, database, API, design and testing docs link to this report.

Exact changed files (repository-relative):

```text
docs/api-contracts.md
docs/architecture.md
docs/database.md
docs/design.md
docs/phase-7/phase-7-1.md
docs/sales-shift-revenue.md
docs/testing.md
frontend/css/modules.css
frontend/data/screens.json
frontend/index.html
frontend/js/app.js
frontend/js/modules/sales.js
frontend/js/modules/workforce.js
frontend/tests/run-tests.js
frontend/tests/workforce-tests.js
src/AlKhayal.Api/ApiBootstrap.cs
src/AlKhayal.Api/Controllers/WorkforceController.cs
src/AlKhayal.Application/Features/FeatureContracts.cs
src/AlKhayal.Application/Features/FeatureRegistry.cs
src/AlKhayal.Application/Features/FeatureService.cs
src/AlKhayal.Application/Workforce/WorkforceContracts.cs
src/AlKhayal.Application/Workforce/WorkforceService.cs
src/AlKhayal.Data/Features/SqliteFeatureStore.cs
src/AlKhayal.Data/Migrations/071_phase7_1_workforce.sql
src/AlKhayal.Data/Sales/SqliteSalesStore.Lifecycle.cs
src/AlKhayal.Data/Sales/SqliteSalesStore.cs
src/AlKhayal.Data/Workforce/SqliteWorkforceStore.cs
src/AlKhayal.Server.Host/AlKhayalServer.cs
tests/AlKhayal.IntegrationTests/BusinessActivityPersistenceTests.cs
tests/AlKhayal.IntegrationTests/CatalogMigrationTests.cs
tests/AlKhayal.IntegrationTests/CustomerLedgerMigrationPreflightTests.cs
tests/AlKhayal.IntegrationTests/CustomerStatementTests.cs
tests/AlKhayal.IntegrationTests/FeaturePersistenceTests.cs
tests/AlKhayal.IntegrationTests/InventoryFifoTests.cs
tests/AlKhayal.IntegrationTests/InventoryPersistenceTests.cs
tests/AlKhayal.IntegrationTests/MigrationTests.cs
tests/AlKhayal.IntegrationTests/PurchasingPersistenceTests.cs
tests/AlKhayal.IntegrationTests/RefundOverdraftMigrationTests.cs
tests/AlKhayal.IntegrationTests/SalesPersistenceTests.cs
tests/AlKhayal.IntegrationTests/SettingsPersistenceTests.cs
tests/AlKhayal.IntegrationTests/SupplierLedgerMigrationTests.cs
tests/AlKhayal.IntegrationTests/SupplierRestartApiTests.cs
tests/AlKhayal.IntegrationTests/TreasuryLegacyMigrationTests.cs
tests/AlKhayal.IntegrationTests/WorkforceApiTests.cs
tests/AlKhayal.IntegrationTests/WorkforcePersistenceTests.cs
tests/AlKhayal.UnitTests/WorkforceServiceTests.cs
```
