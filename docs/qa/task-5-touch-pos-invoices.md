# Task 5 — Sales invoice view/edit in Touch POS

Implemented 2026-11-01. No commit/push. All database/HTTP validation used disposable databases and test license gates; the operator database and licensing state were not opened. Existing uncommitted changes were snapshotted under ignored artifacts/task5/baseline before implementation.

## Completed behavior

- History View opens the existing main Touch POS renderer with contextual saleInvoiceId. Cashiers without POS navigation permission use the same renderer/layout inside their authorized sales screen.
- Invoice number, date, status, return badge, original customer/warehouse names, items, quantities, prices, discounts, saved line/tax/total amounts and paid/returned/outstanding summary load from GET /api/sales/{id}. Historical totals remain independent of current tax settings. Inactive selected warehouse/instrument names remain visible.
- Opened invoices start in view mode. The first attempted draft mutation asks for confirmation, shared across overlapping input events. Declining leaves the original unchanged. Quantity, add/remove, line/invoice discount and supported header changes reuse existing POS handlers.
- Save Changes performs the existing draft PUT, with expectedVersion. The transactional guard rejects stale edits before any mutation. Cancel restores a deep-copied original snapshot with no request. In-flight saves cannot be canceled or submitted twice.
- A new-sale cart already in the POS tab is retained in tab memory when a contextual invoice remounts it. متابعة الفاتورة الجديدة السابقة resumes it. Explicit New Invoice retains discard confirmation and normal checkout actions. Existing tabs, standard new-sale workflow, scanner, print/collection/return/cancellation services and route registry remain.
- Opened invoice views do not expose checkout. Asynchronous product/picker responses are invalidated when an edit is discarded or the cart is replaced. Quantity reduction rejects a line discount exceeding its new gross amount rather than breaking rendering.

## Unsupported editing scenarios and existing accounting rules

**Editing is supported for drafts only.** Posted cash/credit/electronic invoices, paid/part-paid invoices, invoices with returns, canceled invoices and unknown states remain view-only with an Arabic explanation. Deleted drafts return not found. Existing Application/store state checks and posted-record immutability remain authoritative for forged/stale PUT requests.

Inspection covered SalesService, the sales store posting/payment/return/cancellation partials, posted immutability migrations, FIFO/stock-floor behavior, customer debt-first refunds, shift treasury receipts and payment instrument policy. There is no supported atomic amendment/reversal-and-replacement use case. Existing cancellation rejects cash payment, linked payments/returns/collections, insufficient customer debt, tracked units and FIFO mismatches. Chaining cancellation and replacement across separate HTTP calls would leave partial reconciliation on failure; this implementation does not do that.

No posted financial records are overwritten. Draft viewing/discard/saving makes no stock/FIFO/payment/refund/customer-ledger/treasury entries. Posting remains the existing atomic operation-key workflow. Tests assert unchanged complete financial/history rows on rejected edits and exact stock/FIFO/customer/payment/treasury reconciliation after supported posting/replay.

Confirmed draft saves retain existing rules: current tax settings recalculate totals; catalog-selected unit prices and active references are validated. A draft with changed prices or inactive product/unit/customer/warehouse/instrument references may require correction or item removal/re-addition before saving. Serialized draft quantities keep their existing physical-unit restrictions. Opened drafts save as drafts; this task adds no automatic posting or posted amendment.

## Files changed by this task

Production:
- frontend/js/modules/sales.js
- frontend/css/modules.css (bounded contextual Touch layout only)
- src/AlKhayal.Application/Sales/SalesContracts.cs (optional ExpectedVersion)
- src/AlKhayal.Api/Controllers/SalesController.cs (request mapping)
- src/AlKhayal.Data/Sales/SqliteSalesStore.cs (transactional version guard)

Tests:
- frontend/tests/touch-pos-invoice-tests.js (new)
- frontend/tests/run-tests.js (register new handler tests)
- frontend/tests/touch-pos-layout-tests.js (retain original 72 cases; add 192 invoice/contextual cases)
- tests/AlKhayal.IntegrationTests/TouchPosInvoiceEditingTests.cs (new)
- tests/AlKhayal.IntegrationTests/TouchPosInvoiceEditingApiTests.cs (new partial TreasuryApiTests)

Documentation: docs/api-contracts.md, docs/database.md, docs/design.md, docs/testing.md and this report. No schema, migration, package, runtime target, route or permission changes.

## Validation and actual results

Logs, baseline and TRX: artifacts/task5/. Builds use isolated outputs; API tests start only disposable loopback fixture hosts with TestLicenseGate. Each integration invocation sets ALKHAYAL_DATABASE_PATH to a fresh temporary fallback. Licensing test classes were excluded.

Commands:

    dotnet restore AlKhayal.slnx --artifacts-path artifacts/task5 -v:q
    dotnet build AlKhayal.slnx --no-restore --artifacts-path artifacts/task5 -v:q
    node --check frontend/js/modules/sales.js
    node frontend/tests/run-tests.js
    node frontend/tests/touch-pos-layout-tests.js
    git diff --check

Relevant integration command:

    dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj --no-restore --artifacts-path artifacts/task5 --filter 'FullyQualifiedName~TouchPosInvoiceEditingTests|FullyQualifiedName~Touch_pos_invoice_http|FullyQualifiedName~SalesPersistenceTests|FullyQualifiedName~SalesStockFloorTests|FullyQualifiedName~SalesTreasuryTests|FullyQualifiedName~SalesCancellationValuationTests|FullyQualifiedName~SalesReturnConflict|FullyQualifiedName~PricingFifoInvariantTests|FullyQualifiedName~ReturnManagementTests|FullyQualifiedName~PaymentInstrument' --logger 'trx;LogFileName=task5-final.trx' --results-directory artifacts/task5/results -v:q

Results:
- Solution build: passed, 0 warnings/errors (build-final.log).
- Relevant integration suite: 87 passed, 0 failed/skipped (results/task5-final.trx).
- After strengthening assertions to include complete invoice/line/audit/selection history, the seven new persistence tests were rerun: 7 passed, 0 failed/skipped (results/task5-strengthened.trx).
- Frontend executable suite: passed, 129 registered routes (frontend-final.log).
- Headless current Edge layout: 264 cases passed across six viewports, both themes, original new-sale states and invoice view/edit/posted/canceled states, including sales-only contextual sizing (layout-final.log).
- JavaScript syntax and whitespace diff checks passed.
- Initial frontend checks exposed a quantity/discount rendering edge case and a missing validity shim in the standalone test DOM; both were corrected. No failing tests were hidden or weakened.

The existing .NET Framework 4.8/.NET Standard 2.0 stack and Chromium 109-compatible APIs remain. Native Windows 7/Chromium 109 WebView2/DPI qualification was not run. This is a relevant test suite, not a claim that every solution test or every supported runtime was exercised.

## Workflow and preservation

Applied .codex/skills/alkhayal-opencode/SKILL.md. Verified CLI help and listed models. Selected verified opencode-go/kimi-k2.7-code as the multi-file candidate; relative prices are not documented. The single batch-1 call returned Go usage limit exceeded before edits. Codex disclosed this and implemented/reviewed both scoped batches directly; no provider/billing/credential/privacy setting changed. No model repair/escalation calls. Token/quota/Plus usage measurements are unavailable; no savings claim. The ignored .codex/opencode-log.md records the failed call.

Baseline copies, original working-tree patch, status and focused review diffs are under artifacts/task5/. All pre-existing files outside the eleven overlapping task paths remain byte-identical to baseline. The pre-existing changes in overlapping paths remain, with the scoped additions documented above. No source deletion, database reset, commit or push occurred.
## Posted correction extension

- Posted invoices can be corrected only in the persisted original open shift, or on the same server-local business date when shifts are disabled.
- Save creates an immutable replacement revision and an exact inventory/FIFO reversal inside one transaction. The public invoice number remains the root number.
- Lower totals require an explicit treasury refund or customer credit. Higher totals require an explicit collection or customer receivable.
- Existing returns, cancellations, superseded revisions, missing historical shift origins while shifts are enabled, header identity changes, and unsafe tracked/FIFO states remain view-only or are rejected with a specific message.
