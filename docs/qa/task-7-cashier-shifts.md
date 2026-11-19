# Task 7 — cashier shifts

Already present: persisted shifts/events, standalone/user uniqueness, immutable closed history, owner/admin scope, CSRF, opening counts, reconciliation, drawer adjustments, indexed paginated filters, print summaries, invoice origins/corrections, workforce audit and HTTP operation log.

Completed: unconditional transactional sales guard; feature-independent essential lifecycle; original-shift correction checks regardless of feature state; header/Touch POS controls and checkout state; counted/expected/difference confirmation and stale-close protection; cash-refund association, cash-only corrections, explicit manual drawer-account movement links; duration/user counts; bounded linked invoice/treasury/audit detail pages. Existing payroll reads also now return their persisted treasury reference, discovered by the workforce regression. The stale attendance test now expects a closed record to reject a new break, matching existing behavior.

Validation uses unique temporary SQLite databases, random test passwords, localhost HTTP and the existing injected test license gate. No operator database, owner key or license file was opened or changed. No delegation, commit or push.

Commands:

- `dotnet build AlKhayal.slnx --no-restore -p:OutputPath=bin/Task7/ -v:q`
- `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --no-restore -p:OutputPath=bin/Task7/ --filter "FullyQualifiedName~RequiredShiftTests|FullyQualifiedName~Required_shift|FullyQualifiedName~WorkforcePersistenceTests|FullyQualifiedName~Workforce_http|FullyQualifiedName~TouchPosInvoiceEditingTests|FullyQualifiedName~Touch_pos_invoice_http"`
- `dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --no-restore -p:OutputPath=bin/Task7/ --filter "FullyQualifiedName~WorkforceServiceTests"`
- `node frontend/tests/run-tests.js`
- `node --check frontend/js/modules/workforce.js`; `git diff --check`

Executed results: 30 focused integration cases and 8 unit cases passed; frontend behavior/registry passed (129 screens); solution build passed with zero warnings/errors. The final two HTTP/reconciliation cases also passed after adding bounded nonoverlapping activity-page checks; their verification is recorded in `artifacts/task7/results/task7-final-details.trx`. Earlier failed runs remain recorded separately; final passing results supersede them.

Coverage: real authenticated cashier rejection without shift/with another owner's shift; own-shift posting/origin; OWIN dispose/restart/current restoration; unauthorized closure; stale closure; closure followed by rejected new sales; open/closed correction rules; FIFO reversal/reapplication, customer credit, serial reuse, concurrency and rollback; cash/card/wallet separation, cash refund, collections, drawer inputs/outputs and treasury retry identity; linked invoices/audit/treasury and bounded nonoverlapping activity pages; frontend status/elapsed time and checkout state.

Limits: only the existing server-owned `standalone` register is supported. No new LAN terminal identity is claimed. Native WebView2 RTL/DPI/Windows 7 and physical cash/printer acceptance were not run. The entire legacy backend suite was intentionally not run; older sales fixtures outside this focused set still assume optional shifts and will require explicit shift provisioning before those scenarios can pass under the new mandatory policy. No historical cash events are reconstructed, and historical invoices lacking trustworthy origins remain read-only for corrections.

Changed areas: `Application/Workforce` contracts/service; `Api/Controllers/WorkforceController`; `Data/Workforce`, sale posting/corrections/returns, treasury manual posting and migration 081; frontend header/shell, sales and workforce module, shift registry metadata and executable workforce tests; required-shift/API/correction/workforce test fixtures and additive-migration exclusions; architecture/API/database/design/feature/testing documentation. Existing uncommitted changes in these files were preserved.
