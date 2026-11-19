# Feature management

The Phase 2.1 catalog uses the existing core `product-catalog` module. Product catalog routes declare that `featureKey` in `screens.json`; no catalog-specific feature store was added. The administrator-facing feature page exposes only optional controls backed by current workflows while the complete registry remains the compatibility contract for historical configuration.

The visible product-property controls are `product-variants`, `serial-numbers`, `imei-tracking`, and `expiry-dates`. Their saved assignments continue to feed catalog, receipt, inventory, and tracking behavior; IMEI retains its dependency on serial numbers. Internal or disconnected legacy capability keys are not presented as administrator choices, but their definitions and saved product assignments remain readable and are preserved during edits. A product assignment configures supported metadata; physical tracking values belong to receipt/inventory workflows.

## Resolution and overrides

The Application service resolves registry defaults, stored state, and recommendations into one `FeatureSnapshot`. Core state always wins. Missing optional rows use the registry default. Confirmed recommendations write source `recommended`; direct changes write `administrator`. Recommendation conflicts never overwrite administrator rows, and removed activities never disable a feature.

Activity application classifies Task 1 keys through the feature registry. It treats `maintenance-management` as a module, adds `barcode-label-printing` when `barcode-labels` is recommended, and includes dependency closure before one transactional apply.

## Persistence and audit

Migration `005_feature_management.sql` adds `ModuleConfiguration` and `FeatureAuditLog`; capabilities continue using migration 004's `CapabilityConfiguration`. State writes use parameterized values and short transactions. Each change records old/new state, source, user, and UTC time. Deactivation changes configuration only and never deletes business data.

Global capability state never auto-assigns a capability to a new product and never clears an existing product assignment or its variants, identifiers, or conversions. Product-level dependencies are validated again in Application; IMEI requires serial tracking. The SQLite catalog assignment store retains historical tracking configuration keys without creating stock records.

## UI and navigation

`الإعدادات ← الوحدات والميزات` reads real API state and is read-only for non-administrators. It omits the non-actionable core-module section and filters unfinished legacy options at the presentation boundary. A switch confirms, waits for persistence, then publishes the returned snapshot; errors retain the previous state.

Optional screens retain their definitions and carry `featureKey` in `screens.json`. The central service filters sidebar items, compact navigation, quick access, global search, and direct navigation. Disabling a feature closes its open tabs and activates the dashboard. Re-enabling restores navigation dynamically.

The effective-state client keeps at most one `/api/features/effective` request in flight. Screen-owned refreshes update the shared snapshot without broadcasting a shell navigation event; only a real enabled-state change broadcasts one. The shell reconciles navigation without recreating an unaffected active renderer, and renderer generations ignore stale asynchronous completions. This lifecycle prevents the feature screen from recursively loading and rendering itself.

## Boundaries

Feature configuration changes availability only; it does not delete business rows or rewrite historical states. Implemented optional workflows keep their existing Application/API/Data boundaries. Legacy registry definitions remain internal for compatibility and cannot be mistaken for completed workflows in the administrator UI.

# Inventory feature behavior in Task 2.4

The existing `basic-inventory` module keeps the Warehouse Directory available. The existing effective `multiple-warehouses` module gates creation of additional warehouses and transfer posting. Product-level batch, expiry, serial, and IMEI assignments remain binding for existing stock even after global availability is disabled. A product with posted movements cannot drop one of these tracking assignments. No second feature registry was introduced.

## Task 7 compatibility

`advanced-shifts` remains stored and continues to gate advanced operational reports. It no longer disables the essential shift lifecycle or makes sales posting/correction bypass shift checks. Existing administrator/cashier grants remain authoritative; users with sales access can operate their own shift from the header even without management-screen grants. Historical financial records are not assigned to a shift.
