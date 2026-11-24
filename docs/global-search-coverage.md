# Global Search coverage

Both inputs use this inventory. The focused frontend suite verifies mapping and navigation state for every record provider using synthetic responses. HTTP role checks and browser evidence cover the subsets described in [QA](global-search-qa.md).

| Module or entity | Searchable fields | Read used for discovery | Result destination | Effective access and limits |
| --- | --- | --- | --- | --- |
| Products | Name, description, current SKU and barcode | `/api/catalog/products?directorySearch=true` | Product Directory read view by ID | Product Directory route plus catalog API grant. Category shown when present. Category text and unit names are unavailable as search fields. Archived products excluded. |
| Inventory movement documents | Numeric document ID or `INV-<id>` | `/api/inventory/movements` | Warehouse Directory movement history, filtered by warehouse and document ID | Warehouse Directory and existing inventory read grant. One result per document and warehouse. |
| Customers | Name, code, primary and secondary phone | `/api/customers/directory-search` | Customer Directory read view by ID | Customer Directory grant. Reuses the active customer query. No balance or address in results. |
| Suppliers | Name, code, contact and phone | `/api/suppliers/directory-search` | Supplier Directory read view by ID | Supplier Directory grant. No balances or account data in results. |
| Sales invoices | Invoice number and customer name | `/api/sales?searchLimit=20` | Sales Management by invoice ID | Sales Management route plus existing sales read grant. Date and status remain available in module workflows where implemented. |
| Purchase invoices | Invoice number and supplier name | `/api/purchases/directory-search` | Purchase Details by invoice ID | Purchase Management route plus existing purchase read grant. |
| Sales and purchase returns | Return number, original invoice number, party name | `/api/returns?searchLimit=20` | Return Details by type and ID | Returns Overview grant. |
| Delivery orders | Invoice number, customer name, primary phone | `/api/delivery/orders` | Delivery Orders filtered by exact invoice ID | Delivery Orders grant. Current posted orders only. Driver and status filters remain on the destination. |
| Drivers | Name and phone | `/api/delivery/drivers?limit=20` | Driver Management filtered by ID | Driver Management grant. |
| Maintenance tickets | Ticket number, customer name, device, model and serial | `/api/maintenance/tickets/directory-search` | Maintenance Tracking filtered by ticket ID | Maintenance Tracking grant. |
| Installment contracts | Contract number, customer name and phone | `/api/customers/installment-search` | Installment Payments with customer and contract IDs | Installment Payments grant. Schedule displayed within the selected contract. |
| Expense vouchers | Voucher reference, payee and category | `/api/expenses/vouchers/directory-search` | Existing voucher history filtered by ID | Expense Voucher grant. Amounts appear only on the authorized destination. |
| Cash and bank accounts | Account name and code | `/api/treasury/accounts/directory-search` | Treasury Directory read view by ID | Treasury Directory grant. Discovery excludes balance. |
| Wallets and card terminals | Name, number and provider | `/api/payment-methods/instruments?search=…` | Existing detail panel with instrument ID | Administrator and enabled destination route. |
| Electronic payment history | Invoice reference containing digits | `/api/payment-methods/history` | Existing payment history filtered by invoice ID | Administrator and enabled destination route. One result per invoice because the public history model has no payment ID. |
| Employees | Name, employee code, phone and job title | `/api/workforce/employees` | Employee Directory with selected row by ID | Administrator and employee management feature. No salary or private fields in search results. |
| Attendance | Employee name | `/api/workforce/attendance` | Attendance History filtered to selected ID | Administrator and attendance feature. The read endpoint searches employee names. |
| Shifts | Cashier display name, numeric ID, or `SHIFT-<id>` | `/api/workforce/shifts` | Shift Details by ID | Shifts grant and existing owner restriction for cashier reads. |
| Screens, Settings and reports | Registry title, route words, module, description and aliases | Local registry plus effective access state | Existing route or registered quick action | Effective route and feature access. Report calculations stay on their report screens. |

## Remaining record coverage

| Entity | Gap |
| --- | --- |
| Warehouse records, standalone product categories and units | The directories have module-specific lookups. The current APIs for these directory lists do not provide bounded text search. |
| Treasury movements, customer collections, and supplier payments | The treasury movements compatibility route redirects to the account directory. Customer and supplier histories are organized by party. Search by individual transaction reference and reliable ID-based selection require additional read projections and destination wiring. |
| Installment payment records | The contract screen displays the selected schedule. Individual payments have no search provider or verified destination by payment ID. |
| Payroll and employee advances | The read APIs are administrator scoped and organized by employee or payroll period. They have no verified bounded reference search with a record destination. |
| Audit and report rows, mock modules | Only the authorized destination is discoverable. Global Search indexes neither report calculations nor demonstration records. |

The current coverage is partial. The supported sources differ in case handling, spelling normalization, status and date filters, and exact-match ranking. The popup does not paginate across categories. It reports when the retained results are capped.
