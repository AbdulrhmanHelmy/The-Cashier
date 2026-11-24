# Batch B QA

**PARTIAL.** Edge reproduced an out-of-order payroll response with synthetic employee records, then ran the regression at 1024×768, 1366×768 and 1600×1000 in Light and Charcoal Dark. The route matrix checks active navigation, RTL and document-level overflow.

| Module | Current coverage | Acceptance |
| --- | --- | --- |
| Employee directory, payroll and advances | Create dialog, payroll employee switching, history identity, and directory, payroll and advances route smoke. | **PARTIAL.** Employee edit and archive, advance and payroll confirmation and rejection permutations remain. |
| Attendance and shifts | Attendance picker and Escape, attendance history via Settings, shift routes, populated shift-close review and cancellation, and report route smoke. | **PARTIAL.** Shift close submission, attendance correction and restricted access permutations remain. |
| Installments | Contract creation, payment and report route smoke. Populated customer switching and delayed synthetic collection preserve the current form context. | **PARTIAL.** Full contract and invoice detail review, collection validation and error cases remain. |
| Maintenance | Intake, tracking and report route smoke, populated customer filtering and a long Arabic issue description. | **PARTIAL.** Ticket edits, status and settlement permutations remain. |

Run `node frontend/tests/remaining-browser.cjs b` with Edge and Playwright Core. See the [evidence directory](../artifacts/remaining-stage-batch-b/) and [defect register](remaining-stage-batch-b-defect-register.md).


The focused Release integration filter `Workforce|Attendance|Shift|Installment|Maintenance` matched 52 passing tests. Each alternative targets `FullyQualifiedName`. See [focused output](../artifacts/remaining-stage-batch-b/focused.txt). Use `installment-before` as the optional browser argument to reproduce the pre-change customer switch.

