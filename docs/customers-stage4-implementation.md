# Stage 4 customer UI implementation

November 21, 2026. Five defects reproduced on the current frontend and received customer-scoped fixes.

- **Create and Edit.** The existing shared dialog applies its final focus after `onOpen`. The customer form now uses the dialog's focus selector to land on the first input. The generated-code hint and editable Code field in Edit follow the current form contract.
- **Directory.** The empty state identifies an empty customer list or a search with no matches. The existing delete confirmation names the customer and includes their code. Search keys, pagination, row targeting and the delete request retain their established behavior.
- **Statement.** The treasury lookup may return after a customer switch, statement reload, route change or newer dialog request. Its continuation now verifies the current context before displaying the collection dialog or a lookup error. The existing posting callback and request payload were preserved.
- **Report to statement.** A link for an unavailable customer now shows an explicit message and disables account actions until the operator selects an available customer. Opening the statement directly still selects the first available customer.
- **Historical Create/Edit error.** Repeated use in Edge produced neither an uncaught error nor the historical null `innerHTML` toast. The evidence supports **NOT REPRODUCED** for both dialogs.

The original audit ZIP and its unpacked audit, index and six requested screenshots were reviewed. In the current registry, `customers-list` and `customer-statement` are the sidebar entries. The directory opens Create and Edit contextually. `customer-add` and `customer-edit` remain hidden form entries. `customer-credit-payment` still resolves to the statement. Export uses the directory's existing format chooser. `customers-export` and `customer-subscriptions` remain hidden placeholders.

| File | Stage 4 change |
| --- | --- |
| `frontend/js/modules/renderers.js` | Customer form focus, directory messages and confirmation, and statement dialog context. This is the sole production file changed for Stage 4. |
| `frontend/tests/customer-statement-tests.js` | Deferred treasury response regression and verification that an unavailable route ID does not request a different customer's statement. |
| `frontend/tests/stage4-browser.cjs` | Synthetic Edge reproduction, assertions and screenshot capture. |
| `docs/customers-stage4-defect-register.md`, `docs/customers-stage4-implementation.md`, `docs/customers-stage4-qa.md` | Findings, scope, evidence, commands and limits. |
| `artifacts/customers-stage4/` | Local before/after screenshot evidence. The repository ignores the artifact directory. |

Only existing modal, confirmation, field validation and request-generation patterns are reused. The shared UI component, styles, permission contract, navigation registry and report renderer have no Stage 4 changes. The [QA report](customers-stage4-qa.md) records the remaining qualification limits.
