# Final release blockers

**Release qualification remains PARTIAL.**

| Priority | Evidence and release impact |
| --- | --- |
| P0 | No confirmed stage-introduced P0 finding in the executed coverage. |
| P1 | The owner-reported delivery-create/edit symptom remains open for production correlation. The delayed invoice/tab race was fixed. The disposable API lifecycle and live Edge checkout-to-correction loop each passed 20 trials. The affected deployment and native workflow still require verification. If logs show a business-rule rejection requiring changes to existing eligibility, mark that case **BLOCKED — REQUIRES SEPARATE BUSINESS LOGIC AUTHORIZATION**. |
| P2 | Wide and compact Edge route bounds passed. Full populated usability, rapid touch interaction and specific report/receipt output combinations have incomplete evidence. These are coverage gaps. |
| P3 | No stage-introduced P3 backlog item was confirmed. |

## Missing release evidence

- Native WinForms/WebView2 authentication, POS, sales and delivery workflows, focus, dialogs, keyboard, RTL and DPI scaling. Window binding blocked interactive automation on the tested Windows 11 system.
- Windows 7 SP1 startup with the supported legacy runtime, scanner hardware, thermal and standard paper printing, and connected payment provider failure/recovery. No physical printer job or real payment provider was exercised in this stage.
- Exhaustive whole-application populated/empty/error workflows, cashier grants and full visual sign-off. Prior stage reports remain the source for their earlier defects and verification gaps.
- Cash invoice cancellation currently returns `cash_refund_policy`. Any release requirement to support cash reversal is **BLOCKED — REQUIRES SEPARATE BUSINESS LOGIC AUTHORIZATION**. Unpaid credit cancellation passed in the live fixture.

See the [defect register](final-stage-pos-sales-delivery-defect-register.md), [Delivery QA](final-stage-pos-sales-delivery-qa.md) and [native checklist](final-manual-native-test-checklist.md).
