# النخبة ماركت simulation report

The local The Cashier database was reset and replaced on November 23, 2026. The generated supermarket is in Cairo and uses EGP. It traded on 364 consecutive dates from November 24, 2025 through November 22, 2026. The run ended before the store’s next opening on November 23.

## Database and recovery

| Item | Verified result |
| --- | --- |
| Live database | `C:\Users\user\AppData\Local\TheCashier\thecashier.db` |
| File size at publication | 238,485,504 bytes (227.4 MiB) |
| First and last treasury transactions, Cairo time | November 24, 2025 at 05:00 and November 22, 2026 at 23:00:02 |
| SQLite integrity and foreign keys | `integrity_check = ok`, zero foreign-key violations |
| Latest recovery copy | `C:\Users\user\AppData\Local\TheCashier\simulation-2026-11-23-complete\before-reset-final.db` |
| Preservation checks | Schema, indexes, triggers, migration history, trial state, general and tax settings, printer configuration, label configuration, and scale profiles matched the checked baseline. License and machine trial security files matched their pre-run hashes. |

The desktop posted another sale while data generation was in progress. The original publication attempt detected the changed source file. The application was closed before a fresh recovery copy was taken and the validated data was published.

## Business totals

| Entity or measure | Count | EGP |
| --- | ---: | ---: |
| Products | 5,184 | |
| Product categories | 48 | |
| Customers | 1,250 | |
| Suppliers | 48 | |
| Employees | 16 | |
| Users | 10 | |
| Sales invoices | 39,888 | 27,735,883.38 |
| Sales invoice lines | 259,571 | |
| Purchase invoices | 895 | 32,102,153.45 |
| Purchase invoice lines | 33,726 | |
| Sales returns | 1,613 | 330,976.35 |
| Purchase returns | 181 | 8,798.45 |
| Operating expense vouchers | 764 | 500,772.00 |
| Inventory movements | 296,598 | |
| FIFO inventory valuation | | 11,753,203.61 |
| Customer receivables | | 1,482,795.48 |
| Supplier payables | | 15,593,509.83 |
| Customer collections | 2,703 | |
| Supplier settlements | 635 | |
| Treasury movements | 42,622 | |
| Delivery orders | 2,738 | |

Sales and purchase amounts are gross posted invoice totals. Returns are shown separately. Expense vouchers and 192 payroll payments have separate application ledgers. The original tax setting was disabled and was preserved.

## Feature coverage

| Module | Generated and checked |
| --- | --- |
| Catalog, barcode, units | 5,184 SKUs and EAN-13 barcodes with valid checksums. The catalog includes weighed items and carton conversions. Arabic name, barcode, and category searches retrieved records. |
| Authentication and permissions | Administrator and nine cashiers, with 33 screen grants across two cashier access profiles. The production password hasher and session service verified administrator sign-in. |
| Sales, POS, and returns | Invoices linked to 729 shifts. Cash, credit, card, and wallet postings. Posted invoice lines have inventory links. Returns reconcile against quantities, amounts, and original invoice and line IDs. |
| Purchasing, customer accounts, and supplier accounts | Opening procurement, replenishment, credit, cash settlement, collections, and linked purchase returns. Recomputed receivables and payables match the directories. |
| Inventory and alerts | Two warehouses, 42,580 inventory documents, 1,386 batches, 399 out-of-stock variants, 412 expired lots with remaining quantity, and 16 stocked lots expiring within seven days. Includes a transfer, a stock count, and a spoilage adjustment. Movement quantities and values match balances and FIFO layers. |
| Treasury and expenses | Five accounts, opening funding, sales proceeds, daily sweeps, refunds, payments, and 764 classified expense vouchers. Every treasury running balance was recomputed. |
| Workforce and delivery | 728 attendance records, 192 payroll payments, three advances, and three delivery drivers. Cashier shifts closed with counted and expected cash in agreement. |
| Installments and maintenance | Six linked installment contracts, 18 scheduled payments, and six maintenance tickets including one recorded receipt. |
| Settings, backup, printing, and licensing | Grocery activity and company profile were recreated. Recovery and preservation checks passed. Native printer jobs and payment providers were outside the simulation. |
| Related persisted workflows without samples | Serial and IMEI tracking, product bundles, linked installment collections, electronic refund confirmation, and posted invoice corrections. Branches, manufacturing, cash services, and unfinished analytics have no compatible posting flow in this app version. |

## Tests and performance

The Release desktop project built with zero warnings and errors. The unit suite passed 328 tests, the integration suite passed 496, and the frontend suite passed its 132-screen registry coverage. The installed SDK could not parse the `.slnx` solution format. The desktop project and test projects were built directly.

Annual application-service readings measured 21–48 ms for a catalog page, 11–20 ms for a barcode lookup, 313–361 ms for a paged sales report, 3.6–5.0 seconds for stock movement history, 14–15 seconds for annual net profit, and 38–48 seconds for product profitability. These are backend measurements on this Windows machine. Query plans showed full scans for the inventory cost-layer sum and for sales returns looked up by original invoice. Temporary generation indexes were removed before publication.

The net-profit report marks all 2,738 delivery-fee sales as incomplete revenue because it compares merchandise line sums with invoice totals that include delivery fees. Independent invoice and payment reconciliation includes those fees and passed. The report returned zero missing-cost rows and zero unclassified expenses. [Report calculation](../src/AlKhayal.Data/Reports/SqliteNetProfitStore.cs)

## Sign-in and reproducibility

On this local simulation database, each account’s password matches its username. The reset and sign-in checks passed for all ten accounts through the desktop API. The administrator username is `nakheel-admin`, and the cashier usernames are `nakheel-cashier-01` through `nakheel-cashier-09`. The generator guide covers initial credentials for future runs, staging, publication, and recovery. [Generator guide](../tools/TheCashier.Simulation/README.md)
