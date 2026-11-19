# Retired product sizes and colors

Sizes and colors are no longer supported product features. The catalog form, list badges, saved-combination table, activity recommendations and barcode-label editor no longer offer these fields. Cashier, purchasing, inventory and reports continue to identify products by their existing product/variant IDs, SKU and barcodes. Generic custom attributes and variants remain supported by the catalog API.

The feature registry no longer contains `sizes` or `colors`. Existing global overrides remain stored but are ignored by effective-feature and activity-comparison responses. Explicit requests to assign retired capabilities are rejected. Product lists no longer return `sizeValues` or `colorValues`; attribute value contracts no longer contain `colorHex`, and aggregate attribute commands no longer contain free-text `values`. Attribute list/read/write endpoints expose only custom attributes. Product detail omits retired axes and their variant value IDs.

Expiry dates, batches, serial numbers, IMEI, unit conversions, barcode history, pricing and generic variants remain intact. Expiry still requires batch tracking; IMEI still requires serial tracking. The staff product form retains its expiry switch and preserves other saved tracking assignments.

## Database compatibility strategy

No migration is added or edited, and no customer data is dropped, deactivated or rewritten at startup. Migration `010_product_variants_and_identifiers.sql` remains immutable, including its `size`/`color` kind constraint, built-in attribute rows 1/2 and nullable `ProductAttributeValues.ColorHex` column. Consequently even a fresh database contains those dormant definitions.

Deployed databases may retain size/color values in `ProductAttributeValues`, `ProductAttributeAssignments`, `ProductAttributeValueAssignments`, `ProductVariantValues`, product capability assignments, global `CapabilityConfiguration`, and immutable catalog/feature audit history. Legacy names and saved invoice/stock/report/print snapshots may contain previously entered descriptive text. Historical text is not scrubbed or reinterpreted.

When editing a product with retired axes, the store retains its variant mode, attribute/value links, combination keys, variant IDs, exact price overrides and legacy capability rows. Normal product metadata and identifier edits remain available without sending the retired axes back. Requests that rebuild, replace, archive or change the state of those historical combinations are rejected transactionally with `legacy_configuration_locked`. Custom axes on these products also remain fixed until a separately designed migration can preserve their mixed combinations. Products without retired axes keep normal custom-attribute behavior. Products are never merged and quantities/cost layers are never transferred between variants.

Old saved label-template JSON can contain `size`/`color` elements. LabelService projects only those two elements out when reading, previewing or printing; it leaves stored JSON and its version unchanged. An explicit subsequent settings save persists the supported template fields through the usual versioned/audited settings operation. New templates and product-label field dictionaries omit the retired fields.

Any physical cleanup of these dormant rows/columns requires a separate approved migration strategy covering backups, restore verification, historical foreign keys, financial snapshots, and rollback. This task deliberately does not perform that cleanup.

## Verification

`CatalogRetiredAttributeTests` uses only disposable temporary SQLite databases to verify retired APIs are unavailable, serialized contract properties are removed, normal legacy edits preserve complete rows/stock/cost evidence, rejected combination changes roll back, tracking flags remain, foreign keys are intact, and old label JSON is read without rewriting it. Existing generic variant/pricing/label tests use custom quality attributes instead of retired features. Obsolete free-text size/color generation and reconciliation tests are removed.

Run the focused catalog, feature, business-activity, label and pricing/FIFO .NET tests plus `node frontend/tests/run-tests.js`. Build with `dotnet build AlKhayal.slnx --no-restore`; if a running desktop locks its normal output files, use an isolated `-p:OutDir=...` output directory. This change keeps .NET Framework 4.8/.NET Standard 2.0 and Chromium 109-compatible JavaScript. Native Windows 7/WebView2 and physical-printer verification remain separate deployment checks.

Validation on 2026-11-01:

- `dotnet build AlKhayal.slnx --no-restore -p:OutDir=D:/projects/AlKhayal/.codex/task2-build/`: passed, zero warnings/errors. Normal output was locked by the running desktop; that process was left running.
- Focused unit filter `FullyQualifiedName~Catalog|FullyQualifiedName~FeatureService|FullyQualifiedName~BusinessActivity|FullyQualifiedName~Label`: passed (26 tests).
- Focused catalog/feature/label/pricing integration run: 35 passed; the additional legacy compatibility run passed all 3 tests. All of these were included again in the broader run below.
- Broader integration filter `FullyQualifiedName~Catalog|FullyQualifiedName~FeaturePersistence|FullyQualifiedName~Label|FullyQualifiedName~PricingFifoInvariant|FullyQualifiedName~Inventory|FullyQualifiedName~Purchase|FullyQualifiedName~SalesPersistence|FullyQualifiedName~SalesStockFloor|FullyQualifiedName~Report`: 189 passed, 2 failed. `Five_stock_reports_HTTP_independent_permissions_invalid_filters_and_read_only_GETs` and `Operational_reports_HTTP_independent_permissions_module_gates_overview_nonleakage_and_read_only` fail their whole-SQLite-file byte comparison after GET. Both reproduced in isolation and on a clean archive of unmodified `HEAD`, using disposable databases; no unrelated report fix was included.
- `node frontend/tests/run-tests.js`: passed, including the 129-screen registry and existing frontend suites.
- `git diff --check`: passed. Existing migrations are unchanged. No real database or operator licensing file was used; no commit or push was made.
