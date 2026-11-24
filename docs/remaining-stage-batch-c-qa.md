# Batch C QA

**PARTIAL.** Edge reproduced and rechecked backup feedback with an external path and empty history from synthetic HTTP fixtures. The matrix uses Light and Charcoal Dark at 1024×768, 1366×768 and 1600×1000.

| Module | Current coverage | Acceptance |
| --- | --- | --- |
| Reports Center | Route smoke for the overview and all 17 individual reports. Existing frontend suites check returned figures, filters, pagination and permission cases. | **PARTIAL.** Exhaustive populated report, export and source-document browser acceptance remains. |
| Settings and company profile | Settings hub, general, company, appearance, printer, feature and scale routes. Company and general settings use a delayed synthetic save to verify the in-flight guard and success feedback. A rejected general settings save restores editing and preserves the draft. | **PARTIAL.** Other settings save and validation permutations, native printer availability and picker states remain unqualified. |
| Users, permissions and operations log | Administration route smoke, restricted cashier home, user search visibility, and out-of-order cashier permission dialog responses. | **PARTIAL.** User edits, grant changes and error cases remain. |
| Backup and restore | Destination dialog, returned path, configured-folder history feedback, synthetic restore inspection and Escape cancellation. | **PARTIAL.** Native destination picker and complete progress/failure matrix remain. Browser exercise stops at confirmation. |
| Home, notifications and general UI | Home, global search navigation, active tabs, theme changes, and notification read, filter and hidden-access states. | **PARTIAL.** Notification refresh failure, related document links and long-identity browser acceptance remain. |

Run `node frontend/tests/remaining-browser.cjs c` with Edge and Playwright Core. See the [evidence directory](../artifacts/remaining-stage-batch-c/) and [defect register](remaining-stage-batch-c-defect-register.md).



The focused Release integration filter `Report|Backup|Settings|Permission|Alert` matched 110 passing tests. Each alternative targets `FullyQualifiedName`. See [focused output](../artifacts/remaining-stage-batch-c/focused.txt). Use `permissions-before` as the optional browser argument for the pre-change dialog race.


