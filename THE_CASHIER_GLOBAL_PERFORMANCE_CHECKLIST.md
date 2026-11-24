# The Cashier global performance checklist

This inventory covers all 
132
 registered routes. Open work is actionable or unverified. Redirects and unavailable routes have no independent persisted retrieval. Frontend suite coverage does not establish database efficiency.

| Route | Renderer | Retrieval, finding and status |
| --- | --- | --- |
| report-customers | operational-reports | SQLite reporting path includes pages or aggregations. Per-query filtering, scale and completeness verification open. |
| report-suppliers | operational-reports | SQLite reporting path includes pages or aggregations. Per-query filtering, scale and completeness verification open. |
| report-shifts | operational-reports | SQLite reporting path includes pages or aggregations. Per-query filtering, scale and completeness verification open. |
| report-attendance | operational-reports | SQLite reporting path includes pages or aggregations. Per-query filtering, scale and completeness verification open. |
| report-maintenance | operational-reports | SQLite reporting path includes pages or aggregations. Per-query filtering, scale and completeness verification open. |
| report-installments | operational-reports | SQLite reporting path includes pages or aggregations. Per-query filtering, scale and completeness verification open. |
| report-overview | reports-center | Retrieval and performance verification open. |
| report-purchases | transaction-reports | SQLite reporting path includes pages or aggregations. Per-query filtering, scale and completeness verification open. |
| report-returns | transaction-reports | SQLite reporting path includes pages or aggregations. Per-query filtering, scale and completeness verification open. |
| report-payment-methods | transaction-reports | SQLite reporting path includes pages or aggregations. Per-query filtering, scale and completeness verification open. |
| report-product-profit | stock-reports | SQLite reporting path includes pages or aggregations. Per-query filtering, scale and completeness verification open. |
| report-inventory-valuation | stock-reports | SQLite reporting path includes pages or aggregations. Per-query filtering, scale and completeness verification open. |
| report-top-products | stock-reports | SQLite reporting path includes pages or aggregations. Per-query filtering, scale and completeness verification open. |
| report-low-stock | stock-reports | SQLite reporting path includes pages or aggregations. Per-query filtering, scale and completeness verification open. |
| report-inventory-movements | stock-reports | SQLite reporting path includes pages or aggregations. Per-query filtering, scale and completeness verification open. |
| report-net-profit | net-profit-report | SQLite reporting path includes pages or aggregations. Per-query filtering, scale and completeness verification open. |
| report-expenses | expense-report | Retrieval and performance verification open. |
| report-sales | sales-report | SQLite reporting path includes pages or aggregations. Per-query filtering, scale and completeness verification open. |
| payment-overview | payment-methods | Retrieval and performance verification open. |
| payment-wallets | payment-methods | Retrieval and performance verification open. |
| payment-terminals | payment-methods | Retrieval and performance verification open. |
| payment-history | payment-methods | Retrieval and performance verification open. |
| product-labels | product-labels | Retrieval and performance verification open. |
| dashboard | dashboard | Retrieval and performance verification open. |
| settings-program | settings-hub | Retrieval and performance verification open. |
| feature-management | feature-management | Retrieval and performance verification open. |
| settings-system | legacy-redirect | Not applicable. Compatibility redirect to settings-general. |
| settings-general | general-settings | Retrieval and performance verification open. |
| settings-company | company-settings | Retrieval and performance verification open. |
| settings-appearance | appearance-settings | Retrieval and performance verification open. |
| settings-invoice | legacy-redirect | Not applicable. Compatibility redirect to settings-printers. |
| settings-printers | native-printing | Retrieval and performance verification open. |
| business-activities | business-activities | Retrieval and performance verification open. |
| settings-backup | database-settings | Retrieval and performance verification open. |
| customers-list | entity-list | SQLite count and page via CustomersController, CustomerService and SqliteCustomerRepository. Search and sort run in SQLite. Dataset grows. 10,000-record directory and authenticated loopback coverage. |
| customer-add | entity-form | Single-row POST. Removed the initial and post-save full collection reads. Frontend test passed. |
| customer-edit | entity-form | Full customer collection for the legacy selector. Open. |
| customers-export | under-construction | Not applicable. Placeholder has no persisted data path. |
| customer-subscriptions | under-construction | Not applicable. Placeholder has no persisted data path. |
| customer-statement | statement | SQLite totals and paged ledger via CustomersController, CustomerAccountService and SqliteCustomerAccountRepository. Full customer selector still open. |
| customer-credit-payment | legacy-redirect | Not applicable. Compatibility redirect to customer-statement. |
| pos | pos | Bounded product and customer searches. Native scale verification open. |
| sales-management | sales-invoices | SQLite page and count verified in Phase 2. Dataset grows. |
| invoice-lookup | invoice-lookup | Retrieval and performance verification open. |
| sales-list | legacy-redirect | Not applicable. Compatibility redirect to sales-management. |
| sales-reports | report | Retrieval and performance verification open. |
| sales-confirm | workflow | Retrieval and performance verification open. |
| suppliers-list | entity-list | Existing SQLite paged directory. Frontend coverage exists. Scale verification open. |
| supplier-credit | legacy-redirect | Not applicable. Compatibility redirect to supplier-payment. |
| supplier-purchases | legacy-redirect | Not applicable. Compatibility redirect to supplier-payment. |
| supplier-balance | legacy-redirect | Not applicable. Compatibility redirect to supplier-payment. |
| supplier-payment | payment | Supplier statement and invoice collections remain unbounded. Open. |
| supplier-payment-confirm | legacy-redirect | Not applicable. Compatibility redirect to supplier-payment. |
| supplier-reports | legacy-redirect | Not applicable. Compatibility redirect to supplier-payment. |
| supplier-paid-report | legacy-redirect | Not applicable. Compatibility redirect to supplier-payment. |
| purchase-management | purchase-management | Retrieval and performance verification open. |
| purchase-supplier | legacy-redirect | Not applicable. Compatibility redirect to purchase-management. |
| purchases-list | purchase-list | Indexed SQLite date and ID page verified in Phase 1. Dataset grows. |
| purchase-details | purchase-details | Retrieval and performance verification open. |
| purchase-reports | legacy-redirect | Not applicable. Compatibility redirect to purchases-list. |
| purchase-expiry | legacy-redirect | Not applicable. Compatibility redirect to purchase-management. |
| purchase-confirm | legacy-redirect | Not applicable. Compatibility redirect to purchase-management. |
| returns-overview | returns-management | SQLite page and count with full-history search verified in Phase 2. Dataset grows. |
| return-details | return-details | SQLite record selector pages and exact-ID detail verified in Phase 2. Dataset grows. |
| sales-returns | legacy-redirect | Not applicable. Compatibility redirect to returns-overview. |
| purchase-returns | legacy-redirect | Not applicable. Compatibility redirect to returns-overview. |
| receipt-printer | legacy-redirect | Not applicable. Compatibility redirect to settings-printers. |
| barcode-printer | legacy-redirect | Not applicable. Compatibility redirect to settings-printers. |
| barcode-designer | legacy-redirect | Not applicable. Compatibility redirect to settings-printers. |
| print-design | legacy-redirect | Not applicable. Compatibility redirect to settings-printers. |
| print-advanced | legacy-redirect | Not applicable. Compatibility redirect to settings-printers. |
| barcode-product-edit | legacy-redirect | Not applicable. Compatibility redirect to settings-printers. |
| expenses-list | expense-categories | Retrieval and performance verification open. |
| expense-categories | legacy-redirect | Not applicable. Compatibility redirect to expenses-list. |
| expense-add | expense-voucher | SQLite voucher pages and oldest record verified in Phase 2. Dataset grows. |
| financial-movement | legacy-redirect | Not applicable. Compatibility redirect to expense-add. |
| accounts-list | treasury-accounts | Retrieval and performance verification open. |
| account-deposit | treasury-movement | Retrieval and performance verification open. |
| account-withdraw | treasury-movement | Retrieval and performance verification open. |
| account-add | legacy-redirect | Not applicable. Compatibility redirect to accounts-list. |
| accounts-balance | legacy-redirect | Not applicable. Compatibility redirect to accounts-list. |
| account-movements | legacy-redirect | Not applicable. Compatibility redirect to accounts-list. |
| alerts-list | alerts | Retrieval and performance verification open. |
| reorder-alert | legacy-redirect | Not applicable. Compatibility redirect to alerts-list. |
| low-stock-alert | legacy-redirect | Not applicable. Compatibility redirect to alerts-list. |
| warehouses-list | warehouse-directory | SQLite pages exist for balances and movements. Count-history cap open. |
| warehouse-add | legacy-redirect | Not applicable. Compatibility redirect to warehouses-list. |
| warehouse-settings | legacy-redirect | Not applicable. Compatibility redirect to warehouses-list. |
| stock-adjustment | legacy-redirect | Not applicable. Compatibility redirect to warehouses-list. |
| warehouse-transfer | legacy-redirect | Not applicable. Compatibility redirect to warehouses-list. |
| stock-movements | legacy-redirect | Not applicable. Compatibility redirect to warehouses-list. |
| low-stock | legacy-redirect | Not applicable. Compatibility redirect to report-low-stock. |
| unit-add | catalog-units | Retrieval and performance verification open. |
| product-categories | catalog-categories | Retrieval and performance verification open. |
| manufacturing | manufacturing | Retrieval and performance verification open. |
| products-list | catalog-products | Existing SQLite catalog query page. Scale verification open. |
| product-add | catalog-product-form | Retrieval and performance verification open. |
| product-edit | catalog-product-form | Retrieval and performance verification open. |
| product-quick-add | catalog-product-form | Retrieval and performance verification open. |
| employees-list | workforce-employees | Retrieval and performance verification open. |
| employee-add | workforce-employees | Retrieval and performance verification open. |
| employee-payroll | workforce-payroll | 50-row SQLite history verified by focused persistence and frontend tests. Employee selector remains capped. |
| employee-advances | workforce-advances | 50-row SQLite history verified by focused persistence and frontend tests. Employee selector remains capped. |
| shifts-list | workforce-shifts | Retrieval and performance verification open. |
| shift-add | workforce-shifts | Retrieval and performance verification open. |
| shift-reports | workforce-shifts | Retrieval and performance verification open. |
| attendance | workforce-attendance | Retrieval and performance verification open. |
| users-list | user-management | Retrieval and performance verification open. |
| operation-log | operation-log | Existing SQLite search and pages. Scale verification open. |
| user-permissions | legacy-redirect | Not applicable. Compatibility redirect to users-list. |
| sales-permissions | legacy-redirect | Not applicable. Compatibility redirect to users-list. |
| settings-permissions | legacy-redirect | Not applicable. Compatibility redirect to users-list. |
| shift-approval | legacy-redirect | Not applicable. Compatibility redirect to shifts-list. |
| profit-sales-report | report | Retrieval and performance verification open. |
| profits | report | Retrieval and performance verification open. |
| analytics-sales | report | Retrieval and performance verification open. |
| analytics-purchases | report | Retrieval and performance verification open. |
| analytics-suppliers | report | Retrieval and performance verification open. |
| analytics-shifts | legacy-redirect | Not applicable. Compatibility redirect to shift-reports. |
| tracking-reports | report | Retrieval and performance verification open. |
| installment-system | installments | Retrieval and performance verification open. |
| installment-payment | installments | Retrieval and performance verification open. |
| maintenance-intake | maintenance-intake | Retrieval and performance verification open. |
| maintenance-tracking | maintenance-tracking | Retrieval and performance verification open. |
| branches-list | entity-list | Retrieval and performance verification open. |
| branch-stock-transfer | transfer | Retrieval and performance verification open. |
| branch-finance | workflow | Retrieval and performance verification open. |
| cash-services | cash-services | Retrieval and performance verification open. |
| shift-details | workforce-shifts | Retrieval and performance verification open. |
| attendance-history | workforce-attendance | Retrieval and performance verification open. |
| delivery-orders | delivery-orders | Existing SQLite search and pages. Scale verification open. |
| drivers-management | drivers-management | Retrieval and performance verification open. |

## Native and backend workflows

- Native WinForms/WebView2 rendering, memory, printing, previews and export dialogs. Sandbox executable and optional feature registration detected. Dedicated isolated launch, WebView2 runtime, and trial provisioning were not verified.
- Customer payment, installment, maintenance and legacy statement consumers. Full customer selectors or unpaged compatibility calls remain. Open.
- Supplier invoice and payment history. SqliteSupplierAccountRepository materializes invoice and payment sets before statement calculation. Open.
- Workforce, attendance, treasury, inventory and print history. Route entries track remaining verification and caps.
- Exports. Scope is independent of the visible page. Full read and write buffering remain. Open.
