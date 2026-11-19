# Settings and feature registry audit

The administrator-facing cleanup keeps the persisted feature catalog as a compatibility contract while presenting only controls backed by reachable workflows. No migration or saved `ModuleConfiguration`, `CapabilityConfiguration`, or audit row is changed.

## Optional modules

| Classification | Keys | Evidence and UI decision |
| --- | --- | --- |
| Implemented | `maintenance-management`, `installments` | Authenticated Application/API services and SQLite repositories back the maintenance and installment screens. Both remain configurable. |
| Implemented | `multiple-warehouses` | Warehouse creation, stock posting and transfers are persisted and feature-gated. It remains configurable; the obsolete warehouse-settings card does not. |
| Implemented | `employee-management`, `employee-attendance` | Workforce API, Application service and SQLite store back employee, payroll, advance and attendance workflows. Both remain configurable. |
| Implemented | `advanced-shifts` | Persisted shift operations, reports and shift printing use this feature boundary. It remains configurable; required basic sales-shift enforcement remains independent. |
| Implemented | `barcode-label-printing` | Persisted label templates, native preview/printing and Product Directory entry points use this feature boundary. It remains configurable. |
| Unused or obsolete | `manufacturing`, `multiple-branches`, `cash-services`, `advanced-reports` | The registered destinations are unfinished, demo-only, or disconnected from a persisted operational workflow. They remain internal legacy definitions so historical values and old configuration remain readable, but are not offered to administrators. |

Core module definitions remain in `FeatureRegistry` and continue to resolve as enabled. They are intentionally absent from Modules & Features because they have no administrator control.

## Product properties

The administrator page retains `product-variants`, `serial-numbers`, `imei-tracking`, and `expiry-dates`. These keys have persisted product assignments and catalog, purchasing, inventory, or tracking behavior; the IMEI dependency on serial numbers is unchanged. Internal or disconnected legacy choices such as `batch-tracking` and `wholesale-pricing` are not offered, but their definitions and saved product values remain readable and are not rewritten.

## Settings navigation

`product-categories` and `unit-add` remain implemented routes opened contextually from Product Directory, without duplicate Settings metadata. `warehouse-settings` is retained only as a hidden compatibility redirect to `warehouses-list`. Settings search, global navigation search, and the registry-driven hub therefore do not advertise the obsolete warehouse page.

Activity recommendation preview counts only administrator-visible modules and product properties. Applying recommendations still uses the existing server transaction and preserves administrator overrides and historical compatibility state.
