# Stage 5 supplier UI corrections

Reviewed the audit and screenshot index in `THE_CASHIER_UI_AUDIT_PACKAGE.zip` and the seven requested screenshots. The current supplier directory, forms, accounts, statement, related purchase references, treasury-backed payment dialog, payables report, purchase supplier picker, export entry and permission routing were inspected. The historical form exception did not recur in Edge.

- `frontend/js/modules/renderers.js` distinguishes an empty supplier database from zero matches, identifies the supplier on delete, and sets initial form focus through the existing modal option.
- `frontend/js/components/ui.js` supports a fallback focus target when a caller's original control has been removed. Supplier Edit supplies the replacement row's Edit button, or the directory search when that row is absent.
- The same renderer holds account actions disabled until the selected account loads. If a direct lookup fails, it clears the invalid selection, announces the error and returns to the existing picker.
- `frontend/js/modules/operational-reports.js` labels period supplier payments consistently in the report card and table heading.
- `frontend/tests/stage5-browser.cjs` runs before and after scenarios with the actual frontend in Edge, synthetic API responses and controlled delays. It writes screenshots under `artifacts/suppliers-stage5/`.

Directory actions, supplier form fields and validation, existing account and invoice actions, payment requests, report filters, purchase selection, compatibility routes and screen grants continue through their existing implementations. The corrections use modal focus already supported by the shared component, ordinary DOM attributes and existing request sequencing. No Chromium features newer than the application's Chromium 109 baseline were added.

See the [defect register](suppliers-stage5-defect-register.md) and [QA report](suppliers-stage5-qa.md) for observations and remaining native desktop acceptance.
