# Stage 6 purchasing implementation

The audit archive was found at the workspace root. The audit text, screenshot index and all 12 required purchasing images were reviewed. Those images cover the initial editor and history (137, 138), posted detail (316, 504), supplier choice (317), three-line invoice and saved draft (318, 319), posted editor (320), populated history (434), the report (109, 419), and saved receipt preview (535).

## Route and workflow review

- `purchase-management` owns composition and draft reopening. The editor review covered supplier selection and contact context, warehouse context, reference/date/mode/notes presentation, product search and tracking fields, the line table, draft writes, dirty-state navigation and posting feedback, including a repeat click during supplier pagination.
- `purchases-list` opens invoice details, editable drafts, and returns when the corresponding screen is available. `purchase-details` keeps the requested invoice ID and the saved-line presentation, and mounts the existing posted-invoice print control.
- `report-purchases` uses the transaction-report renderer. The report's posting-date language, filters, empty result and invoice link were reviewed. The payment mode wording now matches the editor.
- `purchase-supplier`, `purchase-expiry`, and `purchase-confirm` redirect to the editor. `purchase-reports` redirects to purchase history. `purchase-returns` redirects to `returns-overview`. The actual return handoff uses its existing invoice-type and ID state.
- Supplier management/accounts, warehouse and catalog routes were covered by relevant regressions. Printing Settings uses an existing actual-document picker; the purchase-detail print button uses the existing print-job endpoint.

## Changed files

| File | Change |
| --- | --- |
| `frontend/js/modules/purchasing.js` | Supplier selection presentation and keyboard entry, pending product and mutation guards, edit/focus feedback, confirmed line display, working draft context, post outcome feedback, saved warehouse/unit visibility, and permission-aware navigation affordances. |
| `frontend/js/app.js` | Added a view identity predicate to the existing renderer context. Purchasing and its report can now distinguish a hidden retained tab from a replaced one. |
| `frontend/js/modules/transaction-reports.js` | Matched purchase mode wording and allowed retained purchase-report views to complete their pending read. |
| `frontend/js/modules/printing.js` | Added separators between existing saved-document summary fields in the receipt designer. |
| `frontend/tests/stage6-browser.cjs` | Repeatable Edge checks with synthetic API data, deterministic pending/failure states and captures. |
| `frontend/tests/purchasing-tests.js` | Reproduces duplicate Show More clicks while the next supplier page is pending. |
| `docs/purchases-stage6-defect-register.md`, `docs/purchases-stage6-implementation.md`, `docs/purchases-stage6-qa.md` | Defect evidence, implementation and validation. |
| `artifacts/purchases-stage6/` | Before and after captures and live isolated smoke evidence. The repository ignores the artifact directory. |

The pre-save line-total gap is documented under P6-02. The frontend uses saved values where the API has supplied them. Native desktop and receipt-preview limitations are listed in the QA report.
