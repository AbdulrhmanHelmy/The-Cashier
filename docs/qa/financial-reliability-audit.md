# Financial correctness and operational reliability audit

Date: 2026-11-03. This is an **incomplete audit**, not a release sign-off. The repository has persisted financial workflows beyond the older project identity in `AGENTS.md`. No operator database was opened or changed.

## Executive summary

The five confirmed findings now have prospective fixes, including FR-002 customer collection treasury integration with explicit destination selection. Historical payments were not changed or backfilled. The broader financial audit still lacks a process-kill campaign and operator-history review, so overall financial readiness is **not verified**.

## Scope and method

Read `docs/architecture.md`, `docs/database.md`, `docs/testing.md`, `docs/inventory-fifo.md`, `docs/returns-management.md`, `docs/purchase-treasury.md`, and the relevant Application/Data writers. Inspected the existing disposable-SQLite integration tests and launched `dotnet test AlKhayal.slnx --no-restore -m:1 --verbosity quiet`. The audit used code/data-flow tracing and a focused regression test. No live database or historical migration was modified.

Authoritative amounts use signed 64-bit integers at scale 4, with money finalized to piastres (100 internal units). Quantities use scale 6. A posting writer's SQLite transaction is the atomic boundary; successful results are returned after commit. Operation keys on posted documents are the retry boundary.

## Workflow coverage and batch status

| Batch / workflow | Path and authoritative data | Coverage |
|---|---|---|
| 1: cash, credit, wallet/card sales; weighted sale | `SalesService` → `SqliteSalesStore.PostAsync` → invoice, lines, inventory document, customer ledger or sales payment/treasury, shift origin; `SqliteSalesReportStore` reads posted invoices | Code traced; existing tests identified. No new end-to-end reconciliation. Mixed initial payment is explicitly rejected by `SalesService.Validate`; it is not implemented. |
| 2: purchase and stock receipt | `PurchaseService` → `SqlitePurchaseRepository.PostAsync` → purchase, inventory/FIFO, supplier ledger, optional treasury in one transaction | Code traced; purchase-return defect tested and fixed. The new test includes two and four fractional return sequences and ledger/stock checks. Partial receiving is not established by this audit. |
| 2: transfer, adjustment, count | `InventoryService` → `SqliteInventoryStore` → documents, movements, balances, FIFO layers | Transaction and operation-key path inspected; no new failure injection. |
| 3: partial/repeated sale return | `SalesService` → `SqliteSalesStore.ReturnAsync` → return lines, original-cost stock restoration, customer ledger, payment, return treasury in one transaction | Code traced; final sale-return line uses residual allocation. No new repeated-return stress run. |
| 3: correction/cancellation | `SqliteSalesStore.Corrections` reverses and replaces within one transaction; cancellation disallows paid invoices | Code traced; historical-data and instrument rules need scenario review. |
| 4: customer collection, supplier payment, treasury, shift close | Customer and supplier ledger writers, treasury writer, workforce shift writer | Original cash-collection treasury gap reproduced, then fixed prospectively and covered with disposable SQLite reconciliation and failure tests. |
| 5: sales/net-profit/stock reports, migration and crash/retry | Immutable posted records read by report stores; operation-key checks on writers | Selected SQL read. Full report reconciliation, migration rehearsal and process-kill tests not done. |
| Delivery financial settlement | `SalesInvoiceDeliveries.FeeScaled` is saved with the sales draft and included in the posted sale; `SqliteDeliveryStore.SetStatusAsync` updates status only | Code traced. Separate driver payout or delivery settlement is not implemented by this path; no second financial event was found. |

The purchasing service currently exposes draft create/update, full invoice posting and purchase returns. It has no partial receipt or posted-purchase cancellation command. These are unsupported workflows, not tested accounting operations. Mixed initial sale payment is explicitly rejected by `SalesService.Validate` and documented as full single-method attribution in `docs/payment-methods.md`.

## Findings

The new findings below are prospective code defects or documented integration gaps. No existing posted row was rewritten.

### FR-001 — P1 — Purchase-return account credit can exceed original line

- **Evidence:** CONFIRMED by the previous formula in `src/AlKhayal.Data/Purchasing/SqlitePurchaseRepository.cs` near the `PostReturnAsync` account-value calculation and the new regression in `tests/AlKhayal.IntegrationTests/PurchasingPersistenceTests.cs`.
- **Root cause:** Each partial return rounded `quantity × purchase unit price` independently, without considering earlier credits or the original posted line total.
- **Scenario and impact:** Buy 3 units at 0.01 each: posted line is 0.03. Return 1.5 twice: each independent half-up calculation gives 0.02, so supplier credits total 0.04 and payable is understated by 0.01. Inventory quantity can reconcile while the supplier ledger does not reconcile to the purchase line.
- **Reproduction:** The regression `Repeated_fractional_purchase_returns_never_credit_more_than_original_line` posts a 3 × 0.01 purchase and two 1.5-unit returns, then a 3 × 0.02 purchase and four 0.75-unit returns on an isolated SQLite file. It checks exact final credits, supplier payable/ledger zero, and stock balance/movement zero. Against the old expression the first case credits 0.02 on each return, a 0.01 excess.
- **Expected / minimal fix:** Allocate nonfinal returns from the original posted line with downward piastre rounding; assign the exact residual to the final return. A request with zero *total* credit is rejected, while a zero-credit line can accompany another positive line. This matches the existing sales-return residual rule and preserves the original posted record.
- **Fix status:** Applied to future postings only. `docs/qa/purchase-return-discrepancy.sql` is a read-only query for historical overcredit, full-return mismatch or overquantity. Its SQL detected a planted 0.01 overcredit in an in-memory SQLite fixture. It has not been run on operator data. Existing discrepancies require a separate decision. **Verification:** expanded two/four-return integration test passed; supplier ledger, payable, stock balance and stock movements reconciled; `git diff --check` passed.

### FR-002 — P1 — Customer collections lacked treasury movements

- **Original evidence:** The earlier isolated test posted a 12.34 credit sale and collected 5.00. Invoice paid, customer debt and shift collection changed, while `TreasuryMovements` remained empty. The original invoice writer in `SqliteSalesStore.Lifecycle.cs` had no destination account.
- **Supported writers inspected:** Credit-invoice payment uses `SalesService` → `SqliteSalesStore.AddPaymentAsync`; manual statement collection and the legacy `/payments` endpoint use `CustomerAccountService` → `SqliteCustomerAccountRepository`; installment down payment and scheduled payment use `InstallmentService` → `SqliteCustomerInstallmentRepository`; cash service settlement at maintenance delivery uses `MaintenanceService` → `SqliteMaintenanceRepository`. Existing sale-correction difference collection already posts its own treasury movement in `SqliteSalesStore.Corrections.cs`. Initial cash sales use `ShiftRevenueTreasury` and are not collections of an existing receivable. Maintenance intake advances prepay an uncharged ticket and are outside this finding. Supplier payments are a separate payable workflow. No other persisted customer collection writer was found.
- **Posting rule:** The API and relevant RTL screens now carry `treasuryAccountId`. New collections require an explicitly selected, existing, undeleted account. Cash must select a cash account; card/transfer/wallet must select a bank account. The legacy customer-payment endpoint additionally requires `operationKey`. No default account is configured or inferred, and `ShiftRevenueTreasury` is never chosen automatically. Missing, inactive and incompatible destinations stop posting.
- **Atomicity and identity:** Migration `086_customer_collection_treasury.sql` adds an immutable one-to-one ledger-to-treasury link with a database match trigger and nullable operation identity for new legacy payments. Each writer posts the customer ledger entry, exactly one deposit, treasury audit/account version, customer balance, invoice/installment/maintenance records, and any open-cashier shift event inside its existing SQLite transaction. The shift event points at the same movement; shift close only records counted cash and adds no deposit. Unique operation keys and link constraints prevent duplicates; payload hashes or stored fields reject same-key conflicts. A failed deposit, late customer-balance update, or other required write rolls back the entire transaction. Collections do not add sales revenue.
- **Historical compatibility:** Migration 086 adds schema only. It does not change posted payments, recalculate balances or backfill treasury. The read-only `docs/qa/customer-collection-treasury-discrepancy.sql` identifies unlinked or mismatched historical collection rows for human review; it was not run on operator data. Old collection requests without a destination cannot create new untracked payments. A historical same-key retry without a treasury link is rejected as a conflict by the manual path; historical maintenance delivery with its original null-account hash can replay read-only. Existing read endpoints remain available.
- **Verification:** Disposable-SQLite tests check invoice/ledger/treasury/shift equality, partial and repeated settlement, selected destinations, rejection cases, same-key replay/conflict, concurrent duplicate submission, injected early and late write failures, shift closing, legacy payment linkage, and pre-existing history. The installment test separately checks that the initial 100.00 down payment and later 330.00 installment each have one equal linked deposit in the selected account; maintenance delivery and net-profit tests cover their respective collection and revenue boundaries. An in-memory preexisting-schema fixture retained one historical payment and collection after migration 086, created zero links, and passed `PRAGMA integrity_check`. **Fix status:** implemented prospectively; final full-suite result is recorded below.

### FR-003 — P1 — Tax remainder can be assigned to a fully discounted line

- **Evidence:** CONFIRMED in `src/AlKhayal.Data/Sales/SqliteSalesStore.cs`, `FinalizeLinesAsync`: the old loop assigned all remaining tax to the literal final line, even when its taxable amount was zero. `Invoice_tax_residual_is_assigned_to_taxable_line_not_fully_discounted_last_line` passed after the fix.
- **Root cause:** Invoice discount and tax were allocated in one pass with the same final-row residual rule. The last invoice row need not be the last taxable row.
- **Scenario and impact:** Three 0.01 lines, invoice discount 0.01, tax 50% exclusive. The posted invoice total is 0.03 and tax 0.01. Under the old code, the third line was fully discounted yet carried 0.01 tax, so its line total became 0.01. Its return value and product tax reporting could be wrong although the invoice header still reconciled.
- **Expected / minimal fix:** Allocate discounts first, calculate each line's taxable amount, and place the tax rounding remainder on the last line with positive taxable value. Preserve header tax/total and line sum invariants.
- **Fix status:** Applied to future draft creation/update and corrections that recalculate lines; posted historical lines are unchanged. `docs/qa/sales-line-tax-discrepancy.sql` detects one unambiguous historical pattern read-only, without claiming to find all misallocations. It detected a planted bad line in an in-memory SQLite fixture. **Verification:** focused posted-invoice test passed; header/line tax and total reconciled. Historical discrepancy counts are unknown.

### FR-004 — P2 — A zero-value sales line cannot be returned

- **Evidence:** CONFIRMED code path in `src/AlKhayal.Data/Sales/SqliteSalesStore.Returns.cs`: when calculated return amount is zero, it returns `zero_return` before stock restoration. `src/AlKhayal.Data/Migrations/020_sales_lifecycle.sql` requires positive amounts for `SalesReturnLines` and `SalesReturns`. The FR-003 isolated regression asserts this result and passed.
- **Root cause:** The return document model requires every return to carry positive monetary value, even when a valid sold line was fully discounted.
- **Scenario and impact:** The third line in FR-003 has zero final value but consumed one stocked unit. A customer returning that item cannot use the sales-return workflow to restore inventory, even if other paid lines are included in the same return request; an unrelated manual adjustment would lose the original sale-cost linkage.
- **Approved policy and fix:** A legitimately sold zero-value line may return stock and original FIFO cost with zero refund. Migration `085_zero_value_sales_returns.sql` rebuilds the two return tables with nonnegative monetary checks, retaining identities, positive quantity checks, foreign keys, indexes, immutability and popularity triggers. The migrator checks foreign keys before committing. The return writer rejects negative residuals but accepts zero; cash/refund posting remains gated on positive amounts. `ReturnTaxAllocation` accepts zero gross while rejecting negative gross, so profit reporting includes the cost reversal. Existing posted documents and balances are unchanged; no historical backfill occurs. **Verification:** `ZeroValueReturnTests` and `ZeroValueReturnMigrationTests` passed 8/8; related return, profit, treasury and catalog migration tests passed 34/34 on disposable databases. This is a prospective fix; historical affected invoices can now be returned within existing quantity limits.

### FR-005 — P1 — A valid invoice discount can overflow the final line

- **Evidence:** CONFIRMED by the old `FinalizeLinesAsync` allocation in `src/AlKhayal.Data/Sales/SqliteSalesStore.cs`: all remaining discount was put on the literal last line. The database requires nonnegative line totals. The focused regression asserts exact discount allocation and passed.
- **Root cause:** Piastre-flooring each earlier proportional allocation can leave a residual larger than the final line's value.
- **Scenario and impact:** Three 0.01 lines with a valid 0.02 invoice discount leave 0.02 after the first two proportional floors; the last line is worth only 0.01. The prior draft save attempts a negative line total and rolls back with a constraint error. A larger invoice with many small lines can encounter the same failure.
- **Expected / minimal fix:** Keep the existing proportional floor, cap the last line at its net value, then distribute the remaining discount backward across earlier lines with available value. Header and line totals remain exact and no line is discounted below zero.
- **Fix status:** Applied to new/updated drafts and corrections; posted historical invoices are unchanged. **Verification:** focused regression passed with exact discount and line-total sums; no historical posted data is expected to carry a negative line total because the database check rejects it.

## Reconciliation and reliability results

| Invariant | Result |
|---|---|
| Purchase return credits ≤ original posted purchase line | Previously violated by FR-001; new regression passed for two- and four-part fractional returns. Historical rows have not been audited. |
| Purchase quantities and supplier ledger/balance after multiple returns | New two-return and four-return scenarios check original-line credits, zero final supplier payable/ledger, and zero stock balance/movement. |
| Sales invoice discount and tax allocations | New three-line posted sale checks line tax and total against header; 0.02 discount draft checks discount sum and nonnegative lines. FR-003/FR-005 fixed prospectively. |
| Sales return quantity and residual amount | `SalesPersistenceTests`, `ReturnManagementTests` and `NetProfitReportTests` executed in the full integration run; the audit did not add a new independent reconciliation query for every historical return. |
| Sale/purchase posting replay and atomicity | Operation-key lookup and transaction encompassing document, stock and ledger seen in writers; existing retry/trigger-rollback tests executed. Crash at the OS/process commit boundary not injected. |
| Customer and supplier balances vs ledger | Existing isolated integration tests executed; the FR-001 test adds exact supplier ledger/payable reconciliation. No independent all-customer/all-supplier reconciliation over one comprehensive dataset was added. |
| Stock movements vs balances; transfers conserve quantity; FIFO vs COGS | Existing inventory/FIFO/report suites executed; FR-001 adds direct balance/movement equality. No independent exhaustive valuation reconciliation across every workflow was added. |
| Treasury vs all cash events | FR-002 regression now checks the 5.00 invoice collection has one equal treasury deposit linked to the ledger and shift; manual, installment, maintenance-delivery and legacy paths are covered in disposable databases. Historical unlinked rows remain for read-only review. |
| Shift totals and reports | New invoice and manual collection tests link the shift event to the one treasury movement; shift close adds no second deposit. Net-profit tests keep collections out of additional sales revenue. |

## Durable batch progress

| Batch | Workflows inspected | Executed evidence | Confirmed findings / fixes | Remaining risk | Status |
|---|---|---|---|---|---|
| 1 Sales | Cash, credit, wallet/card, discount/tax, weighted stock, posting, correction, cancellation, retry | Full integration baseline 408/408 and first final 409/409; focused tax/discount test; frontend suite | FR-003/FR-005 fixed; mixed initial payment is an explicit limitation | Historical line-tax errors; no new process-kill run | Partial |
| 2 Purchase/inventory | Purchase posting, return, supplier payable, FIFO, transfer, adjustment/count, tracking | Full baseline; FR-001 sequence; existing disposable dataset and FIFO tests | FR-001 fixed; read-only diagnostic added | Historical overcredits; no partial receipt or posted purchase cancellation workflow | Partial |
| 3 Returns/corrections | Sales/purchase partial return, refund treasury, correction and cancellation writers | Full baseline; FR-004 targeted 8/8 and related 34/34 passed | FR-004 fixed prospectively | Unrun multi-document process-kill scenario | Partial |
| 4 Accounts/treasury/shifts | Customer and supplier ledgers, collection, treasury, shift source/close | FR-002 linked-deposit, replay, failure, shift-close and full-suite tests | FR-002 fixed prospectively | Historical unlinked collections need read-only review; intake advances are separate prepayments | Prospective fix |
| 5 Reports/reliability | Sales, net profit, stock reporting SQL, operation keys, migrations and SQLite constraints | Full baseline and unit/frontend suites; read-only SQL fixtures | No additional report defect confirmed | Independent full-report recalculation and commit-interruption tests outstanding | Partial |

## Tests and remaining work

- FR-002 final sequential run after the down-payment assertion: `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --no-build --no-restore -m:1 --logger 'trx;LogFileName=fr002-integration-verified.trx' --verbosity quiet` passed **422/422**, failed 0, skipped 0 in 5m16s; TRX: `tests/AlKhayal.IntegrationTests/TestResults/fr002-integration-verified.trx`.
- Final unit run: `dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --no-build --no-restore -m:1 --logger 'trx;LogFileName=fr002-unit-verified.trx' --verbosity quiet` passed **248/248**, failed 0, skipped 0; TRX: `tests/AlKhayal.UnitTests/TestResults/fr002-unit-verified.trx`.
- Final frontend run: `node frontend/tests/run-tests.js` passed, including **131 registered screens**. Final `dotnet build AlKhayal.slnx --no-restore -m:1 --verbosity quiet` passed with **0 warnings, 0 errors**.
- Migration 086 was applied to an in-memory preexisting schema with one historical payment and collection. Both remained, no treasury link was inserted, and SQLite integrity was `ok`. This verifies additive behavior on the planted fixture; it is not an operator-database migration rehearsal.
- Previous baseline: `dotnet test AlKhayal.slnx --no-restore -m:1 --verbosity quiet` was interrupted after the integration testhost produced no completion for several minutes; no pass/fail count was obtained. This continuation showed that the full integration suite takes about six minutes and includes a 30,000-sale report stress test; silence under `--verbosity quiet` was not proof of a hang.
- Continuation integration baseline: `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --no-build --no-restore -m:1 --logger 'trx;LogFileName=financial-integration-full.trx' --blame-hang-timeout 120s --verbosity quiet` passed 408/408, failed 0, skipped 0 in 5m59s. It was run before the final tax/discount changes.
- First full integration rerun after FR-003: `financial-integration-final.trx` passed 409/409, failed 0, skipped 0 in 6m09s.
- Second full integration rerun after FR-005: `financial-integration-final-all.trx` passed 409/409, failed 0, skipped 0 in 5m17s. A final narrow purchase-return guard and regression assertion were added afterward; the latest focused rebuild passed 3/3. The sales/purchase/return/pricing/FIFO subset passed 126/126, failed 0, skipped 0 from the latest binaries in 1m24s.
- Focused final regression rebuild: FR-001, FR-002, FR-003/FR-004/FR-005 combined tests passed 3/3, failed 0, skipped 0.
- Continuation full unit suite: passed 247/247, failed 0, skipped 0 (`financial-unit-final.trx`).
- Frontend executable suite: `node frontend/tests/run-tests.js` passed, including 131 registry screens and the finance-related module suites.
- Final solution build: `dotnet build AlKhayal.slnx --no-restore -m:1 --verbosity quiet` passed with 0 warnings and 0 errors.
- Focused run: first attempt to run the new test concurrently failed during build because the baseline `testhost` held `xunit.abstractions.dll` open. This is a test-run scheduling conflict, not a product test result. The sequential rerun passed 1/1 with zero build warnings/errors.
- Related purchase/supplier integration subset: `dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --no-build --filter 'FullyQualifiedName~Purchase|FullyQualifiedName~Supplier' --verbosity quiet` passed 45/45.
- Money, inventory-costing and supplier-allocation unit subset: `dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj --no-restore -m:1 --filter 'FullyQualifiedName~MoneyPolicy|FullyQualifiedName~InventoryCosting|FullyQualifiedName~SupplierPaymentAllocator' --verbosity quiet` passed 15/15.
- `git diff --check`: passed.
- Frontend and full existing integration/unit suites executed. Outstanding work includes independent reconciliation over one comprehensive dataset, operator-history read-only review, process-kill/commit-boundary tests, and explicit reproductions for every one of the 15 requested workflows. Do not treat this document as proof of readiness.

## Readiness answers

- **Double posting / safe retry:** Core sale, purchase, return and inventory writers use operation keys, but exhaustive replay and crash testing remains unverified.
- **Partial failure:** Inspected posting paths group related writes in SQLite transactions; uninspected paths and process-stop injection remain unverified.
- **Incorrect returns/corrections:** Purchase-return rounding was confirmed and fixed prospectively. Sales correction/return edge cases remain to be stress-tested.
- **Debt divergence:** FR-001 proves purchase-return credits could exceed the source purchase line. Customer/supplier full-ledger reconciliation remains unverified.
- **Valuation/profit:** Immutable FIFO and report sources exist, but a complete valuation/profit comparison is outstanding.
- **Contradictory/incomplete behavior:** Initial mixed-payment sale remains unsupported by validation. New customer collections now have explicit treasury destinations; historical unlinked collections remain for review.

## Issue summary and historical exposure

| ID | Severity | Issue | Root cause | Fix status | Verification |
|---|---|---|---|---|---|
| FR-001 | P1 | Purchase-return overcredit | Independent partial rounding | Fixed prospectively | Expanded isolated regression passed; history query provided |
| FR-002 | P1 | Cash collection absent from treasury | No destination account in contract; independent boundary | Fixed prospectively with migration 086 | Independent ledger/deposit/shift reconciliation and full suites recorded below |
| FR-003 | P1 | Tax remainder on fully discounted line | Residual assigned to literal last row | Fixed prospectively | Posted-invoice tax/total regression passed; history query provided |
| FR-004 | P2 | Zero-value line could not be returned | Positive-only return schema and validation | Prospective migration and posting/report fix applied | Targeted 8/8, related 34/34, full integration 418/418 passed |
| FR-005 | P1 | Invoice discount can exceed last line | Residual discount lacks per-line cap | Fixed prospectively | Focused exact-sum regression passed |

Total findings: **5** (4 P1, 1 P2). Fixed prospectively: **5**. Unresolved confirmed findings: **0**. Historical-data exposure: FR-001 and FR-003 can affect already posted transactions; the diagnostic SQL is read-only and was validated against planted in-memory rows, not run against customer/operator data. FR-002 historical collections may lack treasury links. FR-004 does not rewrite older posted rows; eligible free goods can now receive a return document. FR-005 was blocked by a database constraint before a bad document could post.

### Readiness and exact next work

The collection integration is implemented and tested prospectively on disposable databases. The broader audit still lacks process termination at each commit boundary and a read-only examination of historical operator data. Do not silently backfill historical balances or posted documents.

Overall assessment: **not ready for a financial correctness sign-off on this audit evidence**.
