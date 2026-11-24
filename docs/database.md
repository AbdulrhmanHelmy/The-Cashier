# Database

The [supermarket simulation guide](../tools/TheCashier.Simulation/README.md) covers the development-only operational reset, staged validation, database lease, and recovery procedure.

Migration 100 adds the enabled-by-default cash change calculator flag to the existing `ApplicationSettings` row for both fresh and upgraded databases.

Migration 099 adds immutable `SalesInvoiceCostParts` and `SalesReturnEstimatedCosts` without rewriting historical posted invoices. Each part links an invoice line and its source inventory document line; actual parts also link the original FIFO allocation, while estimates retain the selected purchase-cost provenance or an explicit unknown basis. See [sale cost and profit scope](estimated-sale-profit.md).

Migration 096 adds `UserSalePricePermissions`, an independent per-cashier manual invoice selling-price grant. Missing rows mean disabled. Administrator access is derived from role and is not changed by this table.

Migration 097 adds `UserPosStockPermissions`, an independent POS product-card stock visibility grant. A missing row means disabled; administrator access is derived from role.

Migration `092_return_settlements.sql` adds immutable `CustomerInstallmentReturnAllocations` (per return and installment row), immutable `SalesElectronicRefunds` (pending obligation linked to the posted sales return), and immutable `SalesElectronicRefundConfirmations` (unique external reference and operation key). Existing contract, schedule, payment, invoice, and return rows are not rewritten. All new source links have restrictive foreign keys. The return writer inserts stock, ledger, schedule allocation or electronic liability within one SQLite transaction; external confirmation later inserts its evidence and payment event atomically.

Installment migrations 090 and 091 add a nullable unique contract-to-sales-invoice foreign key and an append-only partial-collection table. Existing contracts and full-payment rows are retained without backfilling invoices. New sale posting, stock/FIFO allocation, customer receivable, schedule, down-payment customer collection, treasury movement, and shift association commit together. Linked invoice collection updates `SalesInvoices.PaidScaled` in the same transaction; the ordinary sales-payment endpoint rejects linked invoices.

Migration 095 adds the immutable `InstallmentReceivableEntries` subledger. It backfills contract principal plus interest as debits, then down payments, full/partial collections, and return schedule allocations as credits. New contracts, down payments, collections, and debt-reducing returns append corresponding entries in their source transaction. The aggregate noncash balance is exposed separately from cash/bank accounts; cash receipts and refund movements remain in their selected treasuries.

## Read-only exports

`SqliteExportDataSource` uses `PRAGMA query_only=ON`, short-lived connections, parameterized filter values, and explicit projections. Dataset keys never become SQL identifiers. Product identifier subqueries return at most one SKU/barcode per product, and sales omit superseded correction revisions. Persisted scaled invoice totals are exported without recomputation. No migration or database write is introduced.

Portable business backups use SQLite Online Backup API for WAL-safe snapshots and atomic restore. Trial records and sessions are cleared from the portable database. Restore preserves the destination trial row, and the SQLite journal handles interrupted commits. This adds no migration. See [offline backup and restore](offline-backup.md).

`087_product_bundles.sql` adds bundle composition records and immutable component-line snapshots for posted bundle sales. A bundle remains a regular stock-type catalog item for pricing and search, but it is excluded from purchasing and physical inventory workflows. Sale posting expands each bundle line into its component variants and writes FIFO inventory movements in the same SQLite transaction as the invoice. Returns restore consumed component quantities and their original FIFO costs; cancellation and invoice correction reverse component movements through the existing inventory paths. Historical invoices are unchanged. Components must be simple, active, untracked stock products with positive whole-number recipe quantities; bundles sell in whole packs and cannot have variants or tracked physical units. POS availability is derived from the lowest component-to-recipe ratio in the selected warehouse.

`091_product_allow_sale_out_of_stock.sql` adds `ProductDetails.AllowSaleWhenOutOfStock`, defaulting to `1` for existing products. Catalog create/update persists this setting. Sales use it per stock product and bundle component inside the existing invoice/inventory transaction; disabling it rejects a sale that exceeds available stock, while enabling it preserves the full invoice quantity and consumes only available inventory. No historical invoice or inventory row changes.

`086_customer_collection_treasury.sql` adds immutable, unique `CustomerCollectionTreasuryLinks` from a customer ledger collection to one treasury movement and account, including the payment method. It adds nullable operation identity columns to the legacy `CustomerPayments` table so new requests can replay safely. No old collection, balance, payment, or treasury row is updated or backfilled. New links and movement/audit/version changes are committed in the collection writer's existing SQLite transaction. The read-only historical diagnostic is [customer-collection-treasury-discrepancy.sql](qa/customer-collection-treasury-discrepancy.sql).

`085_zero_value_sales_returns.sql` rebuilds `SalesReturns` and `SalesReturnLines` with `TotalScaled>=0` and `AmountScaled>=0`, preserving historical row identities, foreign keys, indexes, immutability and product-popularity triggers. `Migrator` temporarily disables foreign keys for the table rebuild, verifies `foreign_key_check` inside the migration transaction, and reenables them afterward. Historical posted returns and treasury balances are unchanged.

## Manual weight migration

`084_manual_weight_products.sql` adds only `ProductDetails.SellingType TEXT NOT NULL DEFAULT 'piece'` with a `piece`/`weight` check. It does not insert or modify `Units`, `ProductUnits`, prices, movements, FIFO layers, or historical invoice rows. New weighted product quantities use the existing scale-6 integer columns, with one base quantity representing one kilogram. Scale-4 money columns and rounding are unchanged.

Migration 083 is additive and introduces `Drivers`, `SalesInvoiceDeliveries`, and `Customers.SecondaryPhone`; delivery snapshots reference the invoice and retain driver/customer text after later edits or deactivation.

Sizes/colors are retired without a schema migration. Immutable migration 010 and all legacy attribute/value/variant links remain; normal product edits preserve them. See [compatibility strategy and remaining references](retired-product-attributes.md).

Pre-Phase 9 introduces no schema migration or historical rewrite. Product price edits affect current defaults only; catalog purchase price is reference metadata. Configuration round-trips preserve exact persisted variant overrides, and remaining inventory cost comes from remaining FIFO layer values. See [pricing/FIFO invariants](phase-9/pre-phase-9-pricing-fifo.md) for immutable invoice/cost facts and Phase 9 joins.

Migration `031_sales_refund_overdraft.sql` transactionally rebuilds the treasury ledger to allow narrowly scoped signed sales-refund post-balances, retaining all row IDs/history/source FKs. The migrator performs FK integrity checks before commit and restores FK enforcement after rollback or success. See [sales-refund-overdraft.md](sales-refund-overdraft.md).

Migration `032_customer_installments.sql` adds immutable installment contracts and due schedules plus append-only installment payment records. Contract creation and payment each write the customer transaction ledger and update the balance projection in the same transaction; historical customer records are unchanged. Retry keys and unique schedule-payment constraints prevent duplicate contracts or payments. See [installments.md](installments.md).

Invoice return-state flags are read projections over saved return headers/lines. Complete return requires all original line quantities to be returned; purchase drafts do not count. This requires no migration or invoice/history rewrite. See [returns-management.md](returns-management.md).

Migration `030_returns_management.sql` adds immutable named return-cashbox identities and real sales/purchase return-to-treasury source links. Historical return rows are retained without retroactive cash posting. See [returns-management.md](returns-management.md).

Migration `029_sales_treasury.sql` adds immutable shift-revenue account identity and sales receipt source links. New cash sales atomically deposit into the named cashbox; historical posted sales are unchanged. See [sales-shift-revenue.md](sales-shift-revenue.md).

Migration `028_expenses.sql` adds persisted expense types and immutable vouchers with exact scale-4 money, actor/time snapshots, unique operation identity and treasury source links. Voucher and treasury writes commit atomically. See [expenses.md](expenses.md).

## Migration 024 — treasury

Migrations 026–027 add immutable supplier-payment treasury source links and invoice allocations. Supplier ledger, allocations, treasury movement/audit/version and supplier projections commit atomically. Legacy and purchase invoice read projections include account-payment allocations without double-counting direct invoice payments. Old financial rows remain unchanged. See [supplier-payment-treasury.md](supplier-payment-treasury.md).

Migration `025_purchase_treasury.sql` adds nullable `PurchaseInvoices.TreasuryAccountId` and `TreasuryMovementId` foreign keys, a unique movement-source index and an account lookup index. Existing rows remain unchanged. New cash purchases persist their chosen account; posting links exactly one immutable treasury withdrawal in the same transaction as inventory/FIFO/supplier/audit writes. See [purchase-treasury.md](purchase-treasury.md).

`024_treasury.sql` adds `TreasuryAccounts`, `TreasuryMovements` and `TreasuryAudit` without editing earlier scripts or importing mock balances. Codes are normalized/unique; metadata is versioned and unused-account deletion retains a tombstone. Amounts and historical post-balances are checked scale-4 integers restricted to piastres. Unique operation keys plus canonical hashes prevent duplicate posting. Actor, source account and UTC time are retained. Movement/audit rows cannot be updated or deleted; account-indexed ledger totals derive balances. One writer transaction checks funds, posts money, increments account version and appends audit. See [treasury.md](treasury.md).

## Monetary precision policy

Customer Statement requires no migration. Its unfiltered read starts at zero before replaying the ledger once; filtered opening balances include only entries strictly before `from`. `currentBalance` derives from the full exact ledger while period totals/closing honor filters. Existing movement notes are exposed. Manual increases check signed-64-bit overflow before the insert; entry and balance projection remain one transaction. See [customer-statement.md](customer-statement.md).

Supplier Accounts adds no schema migration. New debt invoices now populate the existing immutable ledger `SupplierInvoiceId`, `OperationKey` and `RequestHash` columns and retain actor attribution in the entry description. Invoice, ledger and payable/credit projection writes share a transaction; existing credit offsets the new debt. Invoice read projections subtract posted purchase returns and parse invariant monetary text in C# to avoid mixed SQLite UNION provider types. See [supplier-accounts.md](supplier-accounts.md).

For deployed databases containing the recorded older `019_treasury.sql`, pending 024 runs a transactional compatibility adapter: it retains the original account table as `LegacyTreasuryAccounts019`, preserves the old transaction/audit tables and FK links, imports exact balances/history into 024, and adds immutable `TreasuryLegacyImports` source links. Unknown or incompatible historical data stops the migration with original schema/rows retained. See [treasury.md](treasury.md#retained-legacy-migration-019).

User-entered EGP amounts are limited to two fractional digits. The Application boundary converts them exactly to the existing signed 64-bit scale-4 representation, so a piastre is always a multiple of 100 internal units. Finalized invoice lines and supplier-account return amounts round to the nearest piastre with `MidpointRounding.AwayFromZero`; half-piastre ties therefore round away from zero. Quantity remains scale 6.

Financial-audit correction (2026-11-03): purchase-return supplier credit now allocates nonfinal partial quantities by flooring their share of the original posted line to a piastre and gives the final quantity the exact remaining original-line value. Sales draft invoice-discount allocation caps each line at its net value and distributes any excess residual backward; tax residual goes to the last line with positive taxable value. These changes affect future postings only. Historical discrepancy queries and limitations are in [the financial reliability audit](qa/financial-reliability-audit.md).

Financial display precision is fixed at two digits even for an older database whose legacy `ApplicationSettings.DecimalPrecision` value differs. New settings writes accept only `2`; generic quantity formatting remains independent and supports up to six fractional digits.

FIFO layers, movement values, partial allocations, unit conversions, and historical values remain scale 4. They are not rewritten merely to obtain a two-decimal display. Allocation consumes the final residual value with the final residual quantity, preserving each layer's original total exactly. User-facing formatting rounds scale-4 values to two decimals while the scaled integer remains available for reconciliation and tests.

The customer and supplier legacy preflights read `CAST(value AS TEXT)` and parse invariant decimal text rather than converting SQLite REAL values through binary floating-point comparisons. Compatible scale-4 values upgrade unchanged. A genuine negative, overflow, malformed, or sub-scale historical value aborts the migration transaction with the affected table/row or financial relationship identified; the original row is retained for operator-led reconciliation. No startup path rounds or resets a legacy database.

## Zero-user bootstrap

First-run administrator creation uses the existing `Users` table and PBKDF2 password hasher; no schema migration is required. The SQLite store begins a serializable writer transaction, checks the count of all users (including inactive users), and inserts exactly one active `administrator`. The role is not supplied by the client. Once any user exists, status remains closed across restart and the insert path returns without writing.

The Phase 2.3 tracking foundation reuses persisted product capability assignments. Physical batch and identifier tables are created by the atomic inventory ledger migration `011_inventory_ledger.sql` described below; see [phase-2-task-3-tracking.md](phase-2-task-3-tracking.md) for the Task 2.3 scope.

## Migration 009 — product reference purchase cost

`009_product_purchase_price.sql` additively adds nullable `ProductDetails.PurchasePriceScaled` with a non-negative check. Non-null values use the existing signed-64-bit scale-4 money policy. Existing rows remain `NULL`, explicitly meaning unknown; the migration does not invent a zero cost, calculate average inventory cost, or create stock movements. Catalog create/update transactions write this reference value alongside the existing product details.

## Migration 008 — product catalog foundation

`008_product_catalog_foundation.sql` adds `ProductCategories`, `Units`, `ProductDetails`, the internal default subset of `ProductVariants`, `ProductMigrationIssues`, and `CatalogOperations`. Root and child category names have separate normalized unique indexes because SQLite treats `NULL` values as distinct. Unit codes are normalized and unique; unit ID 1 (`PCS`, قطعة) is the protected system default. Product details reference active categories/units through Application/Data validation, store price as a non-negative signed 64-bit integer at scale 4, and carry a monotonic `Version` for optimistic concurrency.

The migrator performs the legacy price backfill in C# inside the same transaction as migration 008. It parses only invariant integer/real/text decimal representations, multiplies with checked `decimal`, rounds midpoint away from zero once, and range-checks `long`. Invalid names/types/text/negative/overflow values retain their original `Products` row and price, receive an inactive zero placeholder detail with `review_required`, and create a review issue. Startup reconciliation applies the same quarantine policy to a legacy-only row created after migration. The original `Products.Price` is recovery evidence; it is not read by the catalog API after migration.

No developer database is recreated or deleted. Tests use unique temporary files, clear SQLite pools, and delete only those files.

## Migration 007 — general settings

`007_general_settings.sql` adds singleton `CompanyProfile` and `ApplicationSettings` rows plus `SettingsAuditLog`. Defaults are Arabic, `dd/MM/yyyy`, 24-hour time, `EGP`, `ج.م`, two decimal places, and tax disabled. Updates and their non-sensitive audit entry commit in one SQLite transaction. The migration is additive, repeat-safe through `SchemaVersion`, preserves all earlier tables, and uses the configured `ALKHAYAL_DATABASE_PATH` rather than a hardcoded location.

SQLite is created under `%LOCALAPPDATA%\AlKhayal\alkhayal.db` by default or at `ALKHAYAL_DATABASE_PATH` when explicitly configured. It must remain on local storage. `SchemaVersion` records applied embedded scripts; the migrator orders scripts lexically and applies each pending migration transactionally without recreating the database.

- `001_create_products.sql` provides the original product foundation.
- `002_auth.sql` adds users, server-side sessions, indexes, and foreign keys.
- `003_auth_profile_and_csrf.sql` adds display name, role, timestamps, and per-session CSRF tokens.
- `004_business_activities.sql` adds the selected activity set, its single primary marker, and capability recommendation/override metadata. `UX_BusinessActivities_Primary` enforces at most one primary selection. Foreign keys retain the administrator responsible for each change.
- `005_feature_management.sql` adds optional-module state and an append-only feature audit log. Existing `CapabilityConfiguration` remains the only product-capability state source.
- `006_user_management.sql` adds `Users.LastLoginUtc`, preserves the legacy `administrator` role while mapping every other legacy role to the non-privileged `cashier` role, and adds `UserScreenPermissions` plus `UserAuditLog`. Existing IDs, usernames, activation state, timestamps, and password hashes are preserved.
- `004_customers.sql` adds persisted customer records, including address, balances, credit limit, subscription, and status.
- `005_customer_payments.sql` adds persisted customer payment records.
- `012_suppliers.sql` adds persisted suppliers (`Suppliers.Payable` is the amount currently owed), supplier purchase-on-credit invoices (`SupplierInvoices`, with `PaidAmount <= Total`), and supplier payments (`SupplierPayments`, optionally tied to one invoice). Foreign keys use `ON DELETE RESTRICT`, so a supplier with invoices or payments cannot be deleted.

User mutations that can remove administrator access count active administrators and apply the change in the same SQLite transaction. Role/status/password changes invalidate that user's sessions. Permission replacement and its non-sensitive audit record share a transaction; a failure rolls back the old permission set.

Migration 077 adds append-only OperationAuditLog. It stores actor ID and username/display-name snapshots, authenticated HTTP method, path without query string, status/outcome and UTC timestamp. It intentionally stores no request payloads, credentials, or query values. Logs can outlive a removed user because the actor snapshot is retained without a cascading user foreign key; update/delete triggers reject modification. This unified activity index complements domain audit rows committed atomically with financial operations.

`BusinessActivities` stores stable registry keys, `IsPrimary`, UTC selection time, and `ChangedByUserId`. Saving runs in one transaction and replaces only rows in this table. `CapabilityConfiguration` is the Task 2 integration seam: `IsEnabled` is the effective suggested state and `OverrideState` is either `recommended` or `administrator`. Applying activity recommendations inserts/enables recommended rows but its conflict condition excludes administrator-owned rows. Removed activities never delete or disable capability rows, so the comparison service can report recommendations that no longer apply and the administrator can decide later.

`ModuleConfiguration` stores explicit or recommended optional-module state, source, UTC timestamp, and acting user. Absence means the registry default. `FeatureAuditLog` records module/capability changes and recommendation application with previous/new states. State and audit writes share a transaction. Deactivation changes configuration only and never deletes business rows.

Development provisioning runs only through an explicit `--provision-user [username]` command. Password reset for an existing account is a separate explicit `--reset-password [username]` command. Both read the password from the console without echoing it and never store it in an environment variable or command-line argument. Any non-empty password is accepted. Passwords use PBKDF2-SHA256 with random salts and are never stored or returned in plaintext. Existing usernames are not silently overwritten during provisioning; `--provision-demo` remains an interactive compatibility alias.
Products, categories, units, and optional reference purchase costs are persisted by migrations 008–010, and warehouses, stock movements, balances, batches, physical identifiers, and inventory audit are persisted by migration 011 as described below. Customer records and customer payments are also persisted by their dedicated migrations, `004_customers.sql` and `005_customer_payments.sql`. Suppliers and legacy supplier invoices/payments are preserved by `012_suppliers.sql`; integrated purchase invoices, supplier ledger, and purchase returns are persisted by migrations 017–018. Other business records remain session-only demonstrations unless explicitly covered by a migration and API use case. Business activity profile tables contain configuration metadata only.

# Phase 2 / Task 2.4 inventory migration

## Migration 015 — customer transaction ledger

Migration 015 is additive: it leaves `Customers`, `CustomerPayments`, balances, and all historical rows untouched. It creates immutable customer transaction entries and imports a reconciled opening entry plus the recorded historical collections, so the new statement derives the same current balance rather than rewriting it. The migration preflight rejects a legacy balance or payment that cannot be represented exactly at scale 4; it never rounds a historical monetary value. New monetary entries are limited to two fractional digits, stored at scale 4, and use a unique operation key for retry safety.

Migration `011_inventory_ledger.sql` is additive to the Task 2.3 catalog schema. It creates the default warehouse, inventory documents/lines, immutable stock movements, batch and physical-unit identities, movement links, balance projection, and inventory audit. Partial unique balance indexes distinguish null batch from a real batch. Projection triggers compare each inserted/updated row with its ledger aggregate; posted documents, lines, movements, and audit rows are protected against edits. See [inventory-ledger.md](inventory-ledger.md) for the complete table list, scale policy, restore/rebuild process, and tracking limits.

Warehouse rows are created and edited transactionally by `SqliteInventoryStore.SaveWarehouseAsync`. `Warehouses.NormalizedCode` is the trimmed, Unicode-normalized, upper-cased code and carries a unique constraint, so codes that differ only by case or spacing are rejected as duplicates on both create and edit. The active default and a warehouse that still holds stock cannot be deactivated, only an empty nondefault warehouse can, and reactivating it is an ordinary edit of the same row. Creating a second warehouse still requires the effective `multiple-warehouses` module; editing, deactivating, or reactivating an existing warehouse does not. See [warehouse-management.md](warehouse-management.md) for the directory workflow.

## Migration 016 — FIFO cost layers

Adds persistent receipt/transfer layers, immutable movement allocations and documented opening-cost reconciliations, plus nullable selected-unit acquisition/retail-price snapshots on document lines. Existing stock becomes quantity-preserving unresolved layers; historical movements and valuations are not rewritten. Cost-sensitive withdrawals are blocked until an evidenced opening basis is established. The conservative reconciliation cannot alter retained historical value; a mismatch stays blocked for a dedicated valuation correction. See [inventory-fifo.md](inventory-fifo.md).

## Migrations 017–018 — integrated purchasing and supplier ledger

`017_integrated_purchasing.sql` creates append-only `SupplierLedgerEntries`, purchase invoices/lines/physical units, draft audit, and return records. It imports each legacy invoice/payment once, preserving source tables and IDs. Per supplier, it adds a labeled opening entry equal to current `Payable - legacy invoice totals + legacy payment totals`, preventing old documents from being counted twice. Preflight runs before 017 SQL inside the migration transaction and checks scale-4 representability, invoice/payment ownership, paid-total reconciliation, and a nonnegative opening. Any mismatch throws before migration writes and leaves legacy rows unchanged.

`018_purchasing_provenance_and_credit.sql` links purchase lines to their actual inventory document lines, adds supplier-payment operation keys/hashes, links imported legacy payments back to historical invoices, and adds the supplier-credit projection. It adds immutable return audit and posted-return protections. Monetary ledger values are signed integers at scale 4. Positive entries increase the payable; payments/returns reduce it and may yield supplier credit. Cash purchases record equal payable/payment entries and, after migration 025, an atomic withdrawal from their selected treasury account. Refund and supplier-credit returns use distinct types, but neither creates a treasury movement or balance.

Before applying pending 018, the migrator checks the recorded-017 ledger schema. Current 017 already contains `LegacyPaymentId`; no change is made in that case. A representative older ledger schema without the column makes 018's `UPDATE SupplierLedgerEntries ... p.Id=SupplierLedgerEntries.LegacyPaymentId` fail with `no such column`. For that schema only, the migrator adds the nullable column and unique index inside 018's transaction and links historical payments by an exact, unique supplier/reference/date/scale-4-amount match. Missing or ambiguous matches abort the transaction rather than guessing or changing balances. The historical 017 and 018 scripts are unchanged; repeat startup sees the applied SchemaVersion and does not duplicate rows. Startup SQL exceptions are written to `%LOCALAPPDATA%\AlKhayal\Logs\startup.log` and are not shown verbatim in the Arabic desktop error state.

Invoice posting uses the existing inventory posting implementation with the same SQLite connection and transaction. The invoice status, inventory document/movements/cost layers, supplier ledger, immediate payment, and audit are all committed or rolled back together. Purchase returns retain source invoice-line and origin movement links; transfer-created FIFO fragments preserve the origin. Return lines store original-price supplier adjustment, actual FIFO inventory value, and the explicit signed difference.

## Migration 019 — sales foundation

`019_sales_foundation.sql` adds versioned sales drafts, immutable posted invoices/lines, append-only audit events, unique generated `SAL-<UTC date>-<random suffix>` numbers, inventory references, idempotency keys/hashes, and scale-4 financial snapshots. Tax settings are snapshotted when a draft is saved. Lines also retain the selected batch/expiry and physical serial/IMEI identifiers used at posting.

`020_sales_lifecycle.sql` adds append-only invoice payment/refund events, linked sales returns and lines, exact FIFO cost-layer restoration allocations, and returned physical-unit links. Customer transactions gain invoice/return references. Invoice `PaidScaled` is a bounded projection updated transactionally with customer collections and refunds. Posting, collections, refunds, customer balance changes, return stock receipts, and cost restoration share short SQLite transactions; financial event rows and posted return rows cannot be edited or deleted. Since migration 086, new customer collections also create one source-linked treasury deposit in that same transaction; historical events are unchanged.
# Migration 021: sales cancellation

`021_sales_cancellation.sql` adds append-only `SalesDraftDeletions` and `SalesCancellations` records. Posted `SalesInvoices` and their original stock, payment, and customer entries remain immutable. A cancellation points to a separate posted inventory receipt with `ReversesDocumentId` set to the original sale stock document. Its movements and FIFO layers restore the exact quantity and historical allocated value. The customer reduction and balance update commit in the same transaction. Draft deletion is a tombstone that excludes the draft from normal queries; it never touches stock. Existing database files receive this ordered migration at normal startup; tests apply it only to disposable databases.

# Migration 022: Touch POS browse indexes

`022_pos_browse_indexes.sql` adds lookup indexes on `SalesInvoiceLines(SalesInvoiceId,Id)` and `SalesReturnLines(SalesInvoiceLineId)`. The Touch POS page query aggregates only invoices from the previous 30 UTC days, subtracts returns at the original line's base-unit conversion, and orders the filtered product set by net quantity. The indexes change no financial rows or stock balances. Historical migration fixtures that intentionally omit sales tables skip this dependent index script before running the normal migrator.

`023_directory_query_indexes.sql` adds balance and active product-identifier lookup indexes for paged warehouse searches. It changes no inventory or financial rows. Customer code allocation runs within the customer insert transaction, using the highest existing numeric `CUS-` suffix and the existing case-insensitive unique constraint; historical and explicitly supplied codes are retained.

## Phase 7.1 workforce storage

Additive embedded `071_phase7_1_workforce.sql` adds Employees, EmployeeAttendance, CashierShifts, CashierShiftEvents, WorkforceOperations and WorkforceAudit. Allocation leaves the intervening range for parallel Phase 6; it changes no prior migration or existing history. Unique active/source indexes, exact scale-4 amounts, UTC intervals and immutable audit/close guards preserve history. See [Phase 7.1](phase-7/phase-7-1.md#database-and-migration-coordination) for migration coordination and drawer accounting boundaries.

Additive `076_workforce_payroll.sql` stores employee national IDs, cumulative attendance break minutes, employee advances, monthly payroll payments and immutable advance-to-payroll allocations. Advances and net salary payments reference the Treasury withdrawal movement created in the same transaction; employee/month uniqueness and operation keys prevent duplicate payouts, and no applied migration or prior workforce history is rewritten.

## Native printing persistence — Phase 7.2

Printer management adds no schema migration. The existing `PrintingConfiguration.Json` contains optional `printers` records with stable IDs and optional `printerId` references on per-document profiles. Older JSON deserializes with an empty printer list and keeps its direct Windows printer/template profile values. Saving uses the existing version check and audit transaction. Removal must clear referencing profile IDs before save; no financial tables are changed.

Additive `072_phase7_2_printing.sql` creates versioned PrintingConfiguration JSON, PrintingAudit and PrintJobs with durable request-key uniqueness, an active-document index and explicit interrupted-job review metadata. It changes no financial table or existing migration. Configuration and its audit commit atomically; 33 migrations now apply in the current checkout. See [Phase 7.2 storage and diagnostics](phase-7/phase-7-2.md).

## Barcode and label storage — Phase 7.3

Additive `073_phase7_3_labels.sql` adds LabelConfiguration/LabelConfigurationAudit, BarcodeOperations/BarcodeSymbologies and nullable PrintJobs RequestHash/BatchSummaryJson. Symbology is separate from existing barcode provenance. Replacement retires the old ProductBarcodes row and increments the existing product version/audit atomically; no historical line or financial identity is rewritten. Current migration count is **34**. See [persistence and identity policy](phase-7/phase-7-3.md).

Migration `092_scale_barcode_profiles.sql` adds administrator-managed `ScaleBarcodeProfiles`. Migration `093_scale_barcode_single_profile.sql` limits the installation to one row. The profile positions describe 12 EAN body digits (prefix + PLU + value, including an optional value check digit); the thirteenth EAN check digit is validated independently. Profile edits use optimistic versions.
Barcode deletion uses the same lifecycle: it sets `RetiredUtc`/`RetiredByUserId` on only the selected ProductBarcodes row, records the operation and audit event transactionally, and leaves dependent product, stock, and historical document rows intact. Retired rows remain reserved for uniqueness and are excluded from label preview/printing and active scanner use.

## Maintenance ticket storage

033_maintenance_tickets.sql adds customer-linked tickets, append-only status history, and receipt records for intake advances and cash delivery. Monetary values use scale 4, operation keys protect intake/status/delivery retries, and ticket/customer-statement writes share short SQLite transactions. Cash delivery does not create a treasury/cashbox movement. See [maintenance workflow](maintenance.md).


## Sales payment instruments

Configured wallets and card terminals, atomic full-invoice attribution, administrator-only management/history, legacy contract mapping, electronic refund restrictions and physical cash shift exclusion are documented in [payment-methods.md](payment-methods.md). Migration 074 is additive; original financial records and credit collection workflows remain intact.


## Maintenance advance posting correction

No schema migration is required. Maintenance intake retains the advance solely in `MaintenanceTicketReceipts`; delivery writes the service charge and applies that advance to `CustomerTransactions` in the same transaction as the final customer balance and delivery status. Existing matching legacy advance collections are preserved and reused once. `Customers.Balance >= 0`, immutable receipt/ledger triggers, operation keys, and the lack of maintenance-to-treasury posting remain unchanged. See [maintenance-advance-hotfix.md](maintenance-advance-hotfix.md).

## Phase 9.1 reporting reads

No schema migration was added. Reports read SalesInvoices/SalesPayments/SalesReturns/SalesCancellations, PurchaseInvoices/posted PurchaseReturns, SalePaymentSelections and PaymentInstruments with current party/user labels. Count/sum/page share short read transactions. Event headers and independently aggregated payments prevent multiplication through lines or FIFO allocations; no history is rewritten. Saved scale-4 integers are formatted as exact decimal strings at the report boundary. Existing indexes were retained after an isolated 30k-sales/payments, 10k-purchases/returns check. See [query semantics, dates and performance evidence](phase-9/phase-9-1.md).

The expense voucher report reads immutable `ExpenseVouchers` by `CreatedUtc`; `ExpenseDate` remains a distinct business date. It adds no migration or financial writes. See [Phase 9.2](phase-9/phase-9-2.md#expense-voucher-report--recovery-implementation).

## Net Profit eligibility metadata — migration 075

`075_expense_profit_classification.sql` appends `ExpenseProfitClassifications`: voucher FK, unique voucher/revision, explicit included/excluded/unclassified status, reason, actor FK, UTC time and unique operation key. Immutable audit triggers prohibit update/delete. Missing metadata means unclassified; voucher/category names never imply eligibility. Financial vouchers, treasury links and existing applied migrations are preserved. Posting-event and original-return-line indexes serve the report's actual predicates. Net Profit GET uses one read snapshot, independently aggregates original issue costs and streams relevant prior returns; no temporary tables, posting or historical rewrites occur. All migration/tests use disposable files. [Recognition, original tax and current-classification restatement policy](phase-9/phase-9-2.md).

## Phase 9.2 product and inventory report reads

No additional migration. Product reads reuse Net Profit original line/invoice evidence, `InventoryCostAllocations`, `SalesReturnCostAllocations` and the same original-tax allocator. Valuation reuses shared exact remaining-layer evidence and decimal selling-value calculation from the existing inventory endpoint, including unresolved/null acquisition-cost checks before batch grouping. Availability uses real existing warehouse/variant balance assignments only; its warehouse-specific availability view is separate from the product reorder threshold introduced by Task 6. Movements read saved signed quantities/values and unique business source document links. SQL handles filtered grouping/count/sorting/LIMIT/OFFSET; short read snapshots preserve consistent summary and page. Return history and valuation batches stream with bounded memory. See [Phase 9.2 source/date/completeness policies](phase-9/phase-9-2.md#final-five-report-implementation--2026-10-30).

## Phase 9.3 operational read models

No migration or financial writer changes. `SqliteOperationalReportStore` aggregates authoritative customer/supplier ledgers, reuses the existing workforce shift source, and reads attendance, maintenance receipts/delivery history and installment schedules/allocated payments. All filtering/counts/summaries/pagination execute in SQLite; separate event aggregations avoid multiplicative joins. Current balances remain distinct from period registrations. Existing indexes and short read transactions are retained. [Exact sources and limitations](phase-9/phase-9-3.md).


## Touch POS popularity projection (078)

`078_pos_product_popularity.sql` adds `ProductPopularity(ProductId, NetQuantityBaseScaled)` keyed by the existing product ID. Its one-time backfill reads posted sales, original inventory-line conversion snapshots and financial returns, excluding `SalesCancellations` tombstones. Quantities are integer scale-6 base units, including unfulfilled zero-stock sales. This derived read projection does not change financial documents or stock. Three triggers maintain it atomically on draft-to-posted transition, return-line insertion and cancellation insertion; lifecycle operation keys prevent duplicate events. Rollbacks also roll back projection updates. No business authorization or sale/return policy moves into these triggers.

POS product pages join the projection by its primary key, reuse variant/unit/category indexes, and never scan the sales ledger for ranking. Category relevance uses the same eligibility predicate as product browsing and deliberately has no stock threshold. Catalog counts, sorting, substring searches and deep offsets still scale with catalog size; the current left-join/name-tie ordering requires a temporary sort; no unused secondary rank index is maintained. See [Task 4 verification](qa/task-4-touch-pos.md). Migration 078 was exercised only in disposable test files, never an operator database.

## Task 5: draft invoice editing guard

No schema or migration change. The existing SalesInvoices.Version is now optionally supplied as expectedVersion to draft saves and compared inside the existing transaction before mutation. Stale submissions fail without changing lines, audit or accounting projections. Existing posted-invoice immutability triggers and restricted cancellation/return rules are unchanged. Viewing/discarding is read-only; confirmed draft saves do not create stock movements, FIFO allocations, payments, refunds or customer ledger entries. No financial amendment/replacement capability is claimed. [Acceptance](qa/task-5-touch-pos-invoices.md).
## Sales invoice correction history

Migration `079_sales_invoice_corrections.sql` adds an immutable revision chain without changing historical posted rows. `SalesInvoiceCorrections` links the root invoice, prior effective revision, replacement revision, exact inventory reversal document, settlement choice, optional treasury movement, actor, request hash, and operation key. Replacement rows use an internal revision suffix while reads expose the root customer-facing invoice number. `SalesInvoiceShiftOrigins` records the authoritative originating cashier shift independently of cash drawer events, including electronic sales.

Corrections restore the prior revision's actual FIFO allocations as new child cost layers and return the exact tracked physical units before posting the replacement through the normal sale inventory path. Original issue movements, allocations, payments, and audit rows remain immutable. A single SQLite transaction contains eligibility revalidation, reversal, replacement posting, customer ledger adjustments, treasury movement, shift event, and correction history; any failure rolls back the whole correction.

## Task 6: product reorder point (080)

Migration `080_product_reorder_point.sql` adds nonnegative, nonnull `ProductDetails.ReorderPointScaled` with default zero (quantity scale 6). It adds a partial configured-product candidate index and a covering `InventoryBalances(VariantId, QuantityBaseScaled)` index for read-only totals across all warehouse/batch rows. No stored alert counter or stock rewrite is introduced. The migration is supplied for normal deployment; task verification runs it only in disposable test databases. Existing legacy migration fixtures exclude 080 until their skipped prerequisites are applied by `Migrator`.

## Task 7: drawer relationships (081)

Migration `081_shift_cash_links.sql` adds nullable `CashierShiftEvents.TreasuryMovementId`, a unique partial movement index, a shift-to-invoice-origin index and an operation-resource index. It performs no ownership backfill. New manual movements in the existing shift-revenue account are linked to the actor's actual open standalone shift once, within the treasury transaction. Unrelated bank/cash accounts are not assumed to be the physical drawer.

## Unified alert read state (082)

Migration `082_alert_read_states.sql` adds only `AlertReadStates(UserId, AlertId, ReadUtc)` and its user/read-time index. The composite primary key makes each marker per-user and idempotent, and the user foreign key cascades only when that user is deliberately deleted. It does not scan or rewrite products, inventory, batches, installments, or financial rows. `Migrator` applies it transactionally and records it in `SchemaVersion`; upgrade tests run against disposable populated databases and verify preserved rows, foreign keys, and SQLite integrity.

Cash returns create an `out` event with their actual refund payment ID in the same return transaction. Cash collections, opening counts and explicit drawer adjustments reuse existing semantics. Card/wallet sales and transfer correction settlements do not add physical drawer cash. Closing stores its expected/count snapshot atomically; immutable event/closed-shift safeguards remain intact.

## Unassigned delivery snapshots

Migration 083 already permits `SalesInvoiceDeliveries.DriverId` to be NULL. An unassigned order stores NULL there and empty driver snapshot strings in the non-null text columns. No migration is needed.

## Product unit sale prices and cashier overrides
Migration 088 adds nullable ProductUnits.SellPriceScaled, in the catalog fixed scale of 10,000 internal units per currency unit. Null preserves legacy derived pricing; existing product rows are unchanged. Migration 089 adds SalesInvoiceLines.ManualPriceOverride with default 0. Invoice line UnitPriceScaled remains the historical displayed piastre-rounded rate. For an automatic quantity tier, LineTotalScaled snapshots the authoritative total calculated from the tier price and exact conversion ratio, rounded once; it can differ by a piastre from QuantityScaled multiplied by UnitPriceScaled. Inventory posting saves entered quantity and conversion numerator/denominator on the linked inventory document line, so returns do not read current conversion settings.

## Expiry-only stock entry (migration 098)

Expiry-tracked products require an expiry date on positive receipts, but no operator-entered batch number. The inventory store generates an opaque internal lot identity for each entry and retains historical `ProductBatches` and `BatchId` references. An entry keeps its expiry through transfers, adjustments, counts, sales, and reversals. No existing batch records or posted financial rows are rewritten.

`SalesInvoiceStockEntries` records each invoice-line to inventory-document-line split and its base quantity. Sales use original FIFO stock-layer order, excluding expired and non-sellable layers. `SalesReturnCostAllocations.SourceMovementId` identifies the original cost movement when several invoice allocations use one layer. Linked returns consume the invoice's remaining allocations by latest expiry first, then descending document-line ID, and restore original layer cost. Unallocated sales remain explicitly unallocated, with no invented expiry or cost. `InventoryCostLayers.IsSellable` separates damaged or expired returned stock from normal sale availability while retaining inventory value. Historical invoices without the new mapping continue through the legacy return path.

Invoice history computes the matching count and page in one SQLite read snapshot. Sales uses the invoice rowid order and existing primary and unique keys for draft deletion and correction visibility. Migration `101_purchase_invoice_paging.sql` adds `IX_PurchaseInvoices_InvoiceDate_Id` for the cross-supplier purchase history order. The migration adds an index without rewriting invoice rows.
