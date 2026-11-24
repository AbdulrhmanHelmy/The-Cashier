# Stage 4 customer defect register

Initial findings from the current frontend in Edge with synthetic API responses. The original ZIP, audit notes, and all six requested historical screenshots were reviewed before source edits.

| ID | Screen | Historical evidence | Current result | Classification | Root cause |
| --- | --- | --- | --- | --- | --- |
| C4-01 | Create and Edit | 310 and 311 | Both dialogs open with focus on Close. | REPRODUCED | The shared modal focuses Close after the form's `onOpen` callback. |
| C4-02 | Directory delete | 131 | Confirmation omits the name and code. Escape cancels without a delete request. | NEW DEFECT | The confirmation uses a generic entity message. |
| C4-03 | Directory empty state | 131 | Empty database and unmatched search show the same message. | NEW DEFECT | Both outcomes use the generic empty table renderer. |
| C4-04 | Statement collection | 132 | With the treasury accounts response delayed, switching customers opens the old customer's collection dialog over the newly selected statement. | NEW DEFECT | The async continuation opens a dialog without checking whether its statement context is still current. |
| C4-05 | Create and Edit | 310 and 311 | Both dialogs opened and accepted input without the historical null `innerHTML` exception. | NOT REPRODUCED | Historical cause unconfirmed. |
| C4-06 | Report to statement | 132 and 420 | Following a report link for a customer missing from the current customer list silently selected the first available customer and enabled Add Debt. | NEW DEFECT | Statement initialization treated an unavailable explicit ID like an initial visit with no selection. |

| ID | Correction | Changed files | Tests and before/after evidence | Status |
| --- | --- | --- | --- | --- |
| C4-01 | Use the modal's existing focus option for customer forms. | `renderers.js`, `stage4-browser.cjs` | Initial focus on Name for Create and Code for Edit. Blank save focuses Name, keeps the entered secondary phone and displays field-level feedback. Tab wraps within the dialog and Escape restores focus to the Create button. See `create`, `edit` and `validation` screenshots. | Fixed |
| C4-02 | Name the target customer and show their existing code in the confirmation. | `renderers.js`, `stage4-browser.cjs` | Verify the code and dismiss with Escape. No delete request is sent. See `delete-confirmation` screenshots. | Fixed |
| C4-03 | Distinguish an empty directory from a search with zero matches. | `renderers.js`, `stage4-browser.cjs` | Reload with an empty fixture, search a populated fixture, and clear the search. See `empty-directory` and `no-matches` screenshots. | Fixed |
| C4-04 | Check the statement renderer, selected customer, statement request and latest dialog request before opening. | `renderers.js`, `customer-statement-tests.js`, `stage4-browser.cjs` | Hold the treasury response until after switching customers. Verify it cannot open an old dialog or replace a newer debt dialog. See `stale-collection` screenshots. | Fixed |
| C4-05 | No production change for this historical exception. | `stage4-browser.cjs` | Monitor browser errors and toast messages during repeated Create and Edit use, validation and tab navigation. See `create` and `edit` screenshots. | Closed as not reproduced |
| C4-06 | Keep the selection empty and account actions disabled when an explicitly requested customer is unavailable. | `renderers.js`, `customer-statement-tests.js`, `stage4-browser.cjs` | Follow an outdated report link. Verify no fallback statement request is sent and the operator can explicitly select a different customer. See `missing-customer` screenshots. | Fixed |

Evidence filenames and qualification limits are in the [QA report](customers-stage4-qa.md).
