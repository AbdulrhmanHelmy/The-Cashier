# Egyptian supermarket simulation

This Windows development tool resets and generates the operational SQLite data for النخبة ماركت in Cairo. It runs outside the installed application and does not participate in normal startup.

## Run the generator

Use PowerShell 7 and the .NET 8 SDK with the .NET Framework 4.8 developer pack. Close the desktop application and any standalone host against the selected database. Check the host's `THECASHIER_DATABASE_PATH` setting if the installation uses a custom database location.

```powershell
$database = Join-Path $env:LOCALAPPDATA 'TheCashier/thecashier.db'
$run = Join-Path $env:LOCALAPPDATA 'TheCashier/simulation-2026-11-23-complete'
./tools/TheCashier.Simulation/Invoke-Simulation.ps1 -Database $database -EndDate 2026-11-23 -Mode replace -RunDirectory $run
```

Use a new run directory for each generation. The default mode, `verify`, stages and validates a database copy. Pass `-Days 4` for a smaller smoke run. Publication requires 365 days. To recheck a staged copy, run `-Mode validate-stage` with its original run directory and day count. To revalidate and publish a completed annual stage, use `-Mode publish-stage` with its original run directory.

If the application was used after the initial snapshot, inspect the changes and close it again. For an authorized reset that includes those newer operational records, `-Mode publish-refreshed-stage` saves `before-reset-final.db` under the database lease and rechecks preserved settings against that fresh copy before publication.

The tool takes the application's database lease when it copies and replaces the database. It rejects live SQLite sidecars, keeps `before-reset.db` in the run directory, and verifies the original file fingerprint immediately before replacement. It checks for unclassified tables before resetting, and resets triggers and sequences only on the disposable staging copy. Publication verifies the schema, migration history, trial record, and printer, label, and scale settings against the original. Preserved printer and label settings point at the new administrator as their required actor.

The assortment, dates, order mix, prices, and operation keys use a fixed seed. Seeded document identifiers and historical clocks apply only to a development build of the Data repositories. Passwords and salts use the production cryptographic random generator. All major transaction records use the existing posting code, including FIFO allocation, stock projections, treasury receipts, customer collections, supplier allocation, shifts, payroll, expenses, and returns. The staging database uses temporary indexes, an in-memory rollback journal, and a last-posted treasury balance lookup. Validation independently recomputes every treasury running balance. Temporary indexes are removed before publication.

The date range uses Cairo business dates. Generation stops at the current local time on the last day, so a run before opening may end sales on the previous day. Existing general tax settings determine how the application posts taxes.

## Sign in and verify

The administrator username is `nakheel-admin`. Read the password on the same Windows account that ran the generator and copy it directly to the clipboard.

```powershell
Add-Type -AssemblyName System.Security
$encrypted = [IO.File]::ReadAllBytes((Join-Path $run 'admin-password.dpapi'))
$clear = [Security.Cryptography.ProtectedData]::Unprotect($encrypted, $null, 'CurrentUser')
[Text.Encoding]::UTF8.GetString($clear) | Set-Clipboard
[Array]::Clear($clear, 0, $clear.Length)
```

Paste the password into The Cashier and clear the clipboard with `Set-Clipboard -Value ''`. The database contains salted PBKDF2 hashes. Cashier accounts have individually generated initial passwords. An administrator can set their usable passwords from User Management.

With the installed app running on its default loopback port, use `Test-PopulatedInstallation.ps1 -RunDirectory $run` for login, report, catalog, and delivery smoke timings.

Use `-Mode check-reports` for read-only service timings without starting the desktop. The net-profit coverage output includes any incomplete revenue or cost rows. The existing report checks compare merchandise lines with the full invoice total, which causes delivery-fee invoices to be flagged as incomplete revenue.

To restore the recovery copy, close all hosts using the database and use an offline copy of `before-reset.db` as the database file. Keep the recovery copy until the replacement has been verified. Treat it as confidential because it contains the original operational records and password hashes.

## Persistence map

| Feature | Persistence and generation |
| --- | --- |
| Company, startup settings, activities | `CompanyProfile`, `ApplicationSettings`, `BusinessActivities`, capability and module configuration. Creates a grocery profile and keeps existing tax and general preferences. |
| Authentication and permissions | `Users`, `Sessions`, screen grants and access overrides. Replaces users and grants. Authentication and session checks use the application service. |
| Catalog and barcode | `Products`, details, categories, units, variants, SKU and barcode identities, capabilities, batches. Generates packaged and weighed goods, configurable cartons, expiry rules, and audit events. |
| Sales and Touch POS | `SalesInvoices`, lines, payments, stock entries and cost parts, shift origins, invoice audit, popularity. Uses the same posting service as POS. |
| Customers and suppliers | Directory rows, customer transactions, supplier ledger, payment allocations and treasury links. Credits and payments are posted in dated order. |
| Purchasing | `PurchaseInvoices`, invoice lines, inventory documents, supplier ledger and cash settlement. Initial purchases and replenishment use actual receiving and cost posting. |
| Sales and purchase returns | Original invoice and line IDs, return cost and inventory links, credit adjustments and cash refund movements. |
| Inventory | Documents, movements, balances, FIFO layers and allocations. Stock is derived through posting. Includes a stock count, an interwarehouse transfer and a spoilage adjustment. Expired lots remain in the ledger and the sale service enforces sellability. |
| Treasury and operating expenses | Accounts, movements and audit, expense categories, vouchers and classifications. Generates opening funding, balanced transfers, and payments. |
| Workforce and delivery | Employees, cashier shifts, drawer events, attendance, payroll, advances, delivery drivers and order snapshots. |
| Payment instruments, installments and maintenance | Adds representative card and wallet sales, financed sales with linked schedules and down payments, and maintenance tickets with a cash settlement. |
| Reports and dashboard | Derived reads across persisted business data. No report or dashboard summary rows are fabricated. |
| Hardware, printer, activation and backup | Preserves printer, label and scale settings, schema and trial state. Machine licensing material remains outside generation. Existing print and business-operation history is reset. |
| Features with no generated scenarios | Product bundles, serial/IMEI, installment collections, electronic refunds, and posted invoice corrections have existing paths. Branches, cash services and unfinished analytics lack a compatible posting flow in this app version. |
