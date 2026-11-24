# Batch A QA

**PARTIAL.** Edge ran the return picker cancellation and independent payment pagination scenarios against synthetic HTTP fixtures. The route matrix covers Light and Charcoal Dark at 1024×768, 1366×768 and 1600×1000. It checks active workspace navigation, RTL and document-level overflow. Synthetic response fixtures prevent contact with live gateways or financial data.

| Module | Current coverage | Acceptance |
| --- | --- | --- |
| Returns | Unified overview, eligible invoice picker, delayed selection, dismissal and focus return, purchase return editor, posted sale and purchase return detail, and the report route. | **PARTIAL.** Actual refund and settlement actions and the full permission and error matrix still need browser acceptance. |
| Cash accounts and treasury | Account directory, deposit and withdrawal route smoke, selected account's API balance and rejected zero-amount entry. | **PARTIAL.** Full post, interruption and cancellation scenarios remain. |
| Expenses | Category directory, voucher and report route smoke, category/account selection, API balance and rejected zero-amount entry. | **PARTIAL.** Category management and voucher posting permutations remain. |
| Payment methods, wallets and terminals | Overview, wallet and terminal directories, history, payment report, paged populated directory, empty history, edit target and Escape cancellation. | **PARTIAL.** Instrument writes, rejection cases and complete restricted-user workflow coverage remain. |

Run `node frontend/tests/remaining-browser.cjs a` with Edge and Playwright Core. Optional `before` and `payment-before` arguments exercise the implementation from the starting Git commit. See the [evidence directory](../artifacts/remaining-stage-batch-a/) and [defect register](remaining-stage-batch-a-defect-register.md).


The focused Release integration filter `ReturnManagement|Treasury|Expense|PaymentInstrument` matched 93 passing tests. Each alternative targets `FullyQualifiedName`. See [focused output](../artifacts/remaining-stage-batch-a/focused.txt).
