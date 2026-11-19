# The Cashier 1.1.0 defect register

## TC-QA-001 — Native trial contacts test timed out

| Field | Evidence |
|---|---|
| Severity | Medium |
| Affected module | WinForms trial and activation regression harness |
| Reproduction | On Windows, build `tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj` in Release into a clean output directory. Filter by `TrialDialogTests.Original_activation_starts_trial_once_and_expired_actions_use_existing_form_and_contacts`. |
| Expected | The native trial test completes and surfaces the original assertion when the dialog regresses. |
| Actual | Fresh build failed at the 30-second STA join. The first incremental run passed, so the clean build was used for diagnosis. |
| Root cause | The contact popup title assertion still expected the previous product name. It threw inside the WinForms timer callback after the timer stopped and before the popup closed. The STA blocked inside the modal message loop. An instrumented callback that captured the error and closed in `finally` exposed the exact caption mismatch in five seconds. |
| Files changed | `tests/AlKhayal.UnitTests/TrialDialogTests.cs`, `docs/testing.md` |
| Fix status | Fixed in the test harness. Corrected the title assertion, added a heading assertion, preserved callback exception details, and closed the popup in `finally`. The STA remains bounded by the same 30-second join and is a background thread. The test checks the two clipboard button targets without writing the host clipboard. |
| Regression | `TrialDialogTests.Cashier_activation_starts_trial_once_and_expired_actions_use_existing_form_and_contacts` |
| Verification | `fresh-before.trx` reproduced the timeout. `diagnostic.trx` recorded the caption mismatch. `unit-final.trx` passed 322/322. In `trial-1.trx` through `trial-3.trx`, all three native tests passed in each fresh process. Actual clipboard transfer remains a desktop acceptance case. |

The prior backup long-path and owner-tool startup defects were already remediated in the incoming working tree. Their current outcomes are covered in the QA report. Production licensing and other outstanding acceptance gates are tracked as qualification gaps unless an actual product failure was observed.

The first Sandbox harness did not detect the trial button because its UI Automation provider reported the control as a pane. The next harness invoked the native WinForms button and passed trial start. A supplier request without a phone and a sale post without an open shift also returned the expected validation errors. The final installed run satisfied those prerequisites and completed purchase, credit sale, PDF, backup, restore and forced-restart verification.
