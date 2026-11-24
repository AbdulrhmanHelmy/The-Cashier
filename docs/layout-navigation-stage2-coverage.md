# Stage 2 route coverage

The registry contains 132 routes in 16 sidebar modules. 76 routes are visible and 56 are hidden, including 40 compatibility redirects. The visible set includes 18 Reports Center views.

Navigation PASS means Edge activated the workspace with the synthetic administrator fixture or the specified entry point. Workflow persistence has separate coverage. Registry PASS means the production resolver terminated at a registered destination in the frontend tests. An empty access cell means the standard effective screen permission applies. Entries with additional restrictions show the registry metadata. Individual administrator permissions, document details and hidden contextual routes retain their existing entry paths.

| Route | Navigation group | Sidebar | Quick action | Search | Workspace | Additional access rules | QA |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `report-customers` | Reports and monitoring | — | — | Yes | Single per route |  | PASS (navigation) |
| `report-suppliers` | Reports and monitoring | — | — | Yes | Single per route |  | PASS (navigation) |
| `report-shifts` | Reports and monitoring | — | — | Yes | Single per route | Feature `advanced-shifts` | PASS (navigation) |
| `report-attendance` | Reports and monitoring | — | — | Yes | Single per route | Feature `employee-attendance` | PASS (navigation) |
| `report-maintenance` | Reports and monitoring | — | — | Yes | Single per route | Feature `maintenance-management` | PASS (navigation) |
| `report-installments` | Reports and monitoring | — | — | Yes | Single per route | Feature `installments` | PASS (navigation) |
| `report-overview` | Reports and monitoring | Yes | — | Yes | Single per route | Report grant can admit center | PASS (navigation) |
| `report-purchases` | Reports and monitoring | — | — | Yes | Single per route |  | PASS (navigation) |
| `report-returns` | Reports and monitoring | — | — | Yes | Single per route |  | PASS (navigation) |
| `report-payment-methods` | Reports and monitoring | — | — | Yes | Single per route |  | PASS (navigation) |
| `report-product-profit` | Reports and monitoring | — | — | Yes | Single per route |  | PASS (navigation) |
| `report-inventory-valuation` | Reports and monitoring | — | — | Yes | Single per route |  | PASS (navigation) |
| `report-top-products` | Reports and monitoring | — | — | Yes | Single per route |  | PASS (navigation) |
| `report-low-stock` | Reports and monitoring | — | — | Yes | Single per route |  | PASS (navigation) |
| `report-inventory-movements` | Reports and monitoring | — | — | Yes | Single per route |  | PASS (navigation) |
| `report-net-profit` | Reports and monitoring | — | — | Yes | Single per route |  | PASS (navigation) |
| `report-expenses` | Reports and monitoring | — | — | Yes | Single per route |  | PASS (navigation) |
| `report-sales` | Reports and monitoring | — | — | Yes | Single per route |  | PASS (navigation) |
| `payment-overview` | Business management | Yes | — | Yes | Single per route | Admin | PASS (sidebar landing) |
| `payment-wallets` | Business management | Yes | — | Yes | Single per route | Admin | PASS (sidebar submenu) |
| `payment-terminals` | Business management | Yes | — | Yes | Single per route | Admin | PASS (navigation) |
| `payment-history` | Business management | Yes | — | Yes | Single per route | Admin | PASS (navigation) |
| `product-labels` | المخازن | — | — | — | Single per route | Feature `barcode-label-printing` · Grant `products-list` | NOT TESTED |
| `dashboard` | Daily operations | Yes | — | Yes | Pinned |  | PASS (navigation) |
| `settings-program` | Settings | Footer | — | Yes | Single per route | Admin | PASS (footer navigation) |
| `feature-management` | Settings | — | — | — | Single per route | Admin | PASS (Settings hub navigation) |
| `settings-system` | الإعدادات | — | — | — | Redirect → `settings-general` | Admin | PASS (registry resolver) |
| `settings-general` | Settings | — | — | — | Single per route | Admin | PASS (Settings hub navigation) |
| `settings-company` | Settings | — | — | — | Single per route | Admin | PASS (Settings hub navigation) |
| `settings-appearance` | Settings | — | — | — | Single per route | Admin | PASS (Settings hub navigation) |
| `settings-invoice` | الإعدادات | — | — | — | Redirect → `settings-printers` | Admin | PASS (registry resolver) |
| `settings-printers` | Settings | — | — | — | Single per route | Admin | PASS (Settings hub navigation) |
| `business-activities` | Settings | — | — | — | Single per route | Admin | PASS (Settings hub navigation) |
| `settings-backup` | Settings | — | — | — | Single per route | Admin | PASS (Settings hub navigation) |
| `customers-list` | Daily operations | Yes | إضافة عميل | Yes | Single per route |  | PASS (navigation) |
| `customer-add` | العملاء | — | — | — | Single per route |  | NOT TESTED |
| `customer-edit` | العملاء | — | — | — | Single per route |  | NOT TESTED |
| `customers-export` | العملاء | — | — | — | Registered |  | NOT TESTED |
| `customer-subscriptions` | العملاء | — | — | — | Registered |  | NOT TESTED |
| `customer-statement` | Daily operations | Yes | — | Yes | Single per route |  | PASS (navigation) |
| `customer-credit-payment` | العملاء | — | — | — | Redirect → `customer-statement` |  | PASS (registry resolver) |
| `pos` | Daily operations | Yes | الكاشير اللمسي | Yes | Single per route |  | PASS (navigation) |
| `sales-management` | Daily operations | Yes | — | Yes | Single per route |  | PASS (navigation) |
| `invoice-lookup` | المبيعات | — | — | — | Registered |  | NOT TESTED |
| `sales-list` | المبيعات | — | — | — | Redirect → `sales-management` |  | PASS (registry resolver) |
| `sales-reports` | المبيعات | — | — | — | Registered |  | NOT TESTED |
| `sales-confirm` | المبيعات | — | — | — | Registered |  | NOT TESTED |
| `suppliers-list` | Business management | Yes | — | Yes | Single per route |  | PASS (navigation) |
| `supplier-credit` | الموردين | — | — | — | Redirect → `supplier-payment` |  | PASS (registry resolver) |
| `supplier-purchases` | الموردين | — | — | — | Redirect → `supplier-payment` |  | PASS (registry resolver) |
| `supplier-balance` | الموردين | — | — | — | Redirect → `supplier-payment` |  | PASS (registry resolver) |
| `supplier-payment` | Business management | Yes | — | Yes | Single per route |  | PASS (navigation) |
| `supplier-payment-confirm` | الموردين | — | — | — | Redirect → `supplier-payment` |  | PASS (registry resolver) |
| `supplier-reports` | الموردين | — | — | — | Redirect → `supplier-payment` |  | PASS (registry resolver) |
| `supplier-paid-report` | الموردين | — | — | — | Redirect → `supplier-payment` |  | PASS (registry resolver) |
| `purchase-management` | Daily operations | Yes | فاتورة شراء | Yes | Single per route |  | PASS (navigation) |
| `purchase-supplier` | المشتريات | — | — | — | Redirect → `purchase-management` |  | PASS (registry resolver) |
| `purchases-list` | Daily operations | Yes | — | Yes | Single per route |  | PASS (navigation) |
| `purchase-details` | المشتريات | — | — | — | Single per route | Grant `purchases-list` | NOT TESTED |
| `purchase-reports` | المشتريات | — | — | — | Redirect → `purchases-list` |  | PASS (registry resolver) |
| `purchase-expiry` | المشتريات | — | — | — | Redirect → `purchase-management` |  | PASS (registry resolver) |
| `purchase-confirm` | المشتريات | — | — | — | Redirect → `purchase-management` |  | PASS (registry resolver) |
| `returns-overview` | Business management | Yes | المرتجعات | Yes | Single per route |  | PASS (quick action navigation) |
| `return-details` | المرتجعات | — | — | — | Single per route | Grant `returns-overview` | NOT TESTED |
| `sales-returns` | المرتجعات | — | — | — | Redirect → `returns-overview` |  | PASS (registry resolver) |
| `purchase-returns` | المرتجعات | — | — | — | Redirect → `returns-overview` |  | PASS (registry resolver) |
| `receipt-printer` | الإعدادات | — | — | — | Redirect → `settings-printers` | Admin | PASS (registry resolver) |
| `barcode-printer` | الإعدادات | — | — | — | Redirect → `settings-printers` | Admin · Feature `barcode-label-printing` | PASS (registry resolver) |
| `barcode-designer` | الإعدادات | — | — | — | Redirect → `settings-printers` | Admin · Feature `barcode-label-printing` | PASS (registry resolver) |
| `print-design` | الإعدادات | — | — | — | Redirect → `settings-printers` | Admin · Feature `barcode-label-printing` | PASS (registry resolver) |
| `print-advanced` | الإعدادات | — | — | — | Redirect → `settings-printers` | Admin · Feature `barcode-label-printing` | PASS (registry resolver) |
| `barcode-product-edit` | الإعدادات | — | — | — | Redirect → `settings-printers` | Admin · Feature `barcode-label-printing` | PASS (registry resolver) |
| `expenses-list` | Business management | Yes | — | Yes | Single per route |  | PASS (navigation) |
| `expense-categories` | المصروفات والسندات | — | — | — | Redirect → `expenses-list` |  | PASS (registry resolver) |
| `expense-add` | Business management | Yes | — | Yes | Single per route |  | PASS (navigation) |
| `financial-movement` | المصروفات والسندات | — | — | — | Redirect → `expense-add` |  | PASS (registry resolver) |
| `accounts-list` | Business management | Yes | الخزنة | Yes | Single per route |  | PASS (quick action navigation) |
| `account-deposit` | Business management | Yes | — | Yes | Single per route |  | PASS (navigation) |
| `account-withdraw` | Business management | Yes | — | Yes | Single per route |  | PASS (navigation) |
| `account-add` | الخزنة والبنك | — | — | — | Redirect → `accounts-list` |  | PASS (registry resolver) |
| `accounts-balance` | الخزنة والبنك | — | — | — | Redirect → `accounts-list` |  | PASS (registry resolver) |
| `account-movements` | الخزنة والبنك | — | — | — | Redirect → `accounts-list` |  | PASS (registry resolver) |
| `alerts-list` | Reports and monitoring | Yes | Bell | Yes | Single per route | Legacy stock grant supported | PASS (bell navigation) |
| `reorder-alert` | التنبيهات | — | — | — | Redirect → `alerts-list` |  | PASS (registry resolver) |
| `low-stock-alert` | التنبيهات | — | — | — | Redirect → `alerts-list` |  | PASS (registry resolver) |
| `warehouses-list` | Daily operations | Yes | دليل المخازن | Yes | Single per route | Feature `basic-inventory` | PASS (navigation) |
| `warehouse-add` | المخزن | — | — | — | Redirect → `warehouses-list` | Feature `multiple-warehouses` | PASS (registry resolver) |
| `warehouse-settings` | الإعدادات | — | — | — | Redirect → `warehouses-list` | Admin · Feature `multiple-warehouses` | PASS (registry resolver) |
| `stock-adjustment` | المخزن | — | — | — | Redirect → `warehouses-list` |  | PASS (registry resolver) |
| `warehouse-transfer` | المخزن | — | — | — | Redirect → `warehouses-list` | Feature `multiple-warehouses` | PASS (registry resolver) |
| `stock-movements` | المخزن | — | — | — | Redirect → `warehouses-list` |  | PASS (registry resolver) |
| `low-stock` | المخزن | — | — | — | Redirect → `report-low-stock` |  | PASS (registry resolver) |
| `unit-add` | الإعدادات | — | — | Yes | Single per route | Admin · Feature `product-catalog` | PASS (navigation) |
| `product-categories` | الإعدادات | — | — | Yes | Single per route | Admin · Feature `product-catalog` | PASS (navigation) |
| `manufacturing` | المخزن | — | — | — | Registered | Feature `manufacturing` | NOT TESTED |
| `products-list` | Daily operations | Yes | — | Yes | Single per route | Feature `product-catalog` | PASS (keyboard navigation) |
| `product-add` | المخزن | — | — | — | Single per route | Feature `product-catalog` | NOT TESTED |
| `product-edit` | المخزن | — | — | — | Single per route | Feature `product-catalog` | NOT TESTED |
| `product-quick-add` | المخزن | — | إضافة سريعة للأصناف | Action | Single per route | Feature `product-catalog` | PASS (quick action navigation) |
| `employees-list` | Business management | Yes | — | Yes | Single per route | Admin · Feature `employee-management` | PASS (navigation) |
| `employee-add` | الموظفين | — | — | — | Single per route | Admin · Feature `employee-management` | NOT TESTED |
| `employee-payroll` | Business management | Yes | — | Yes | Single per route | Admin · Feature `employee-management` | PASS (navigation) |
| `employee-advances` | Business management | Yes | — | Yes | Single per route | Admin · Feature `employee-management` | PASS (navigation) |
| `shifts-list` | Business management | Yes | — | Yes | Single per route |  | PASS (navigation) |
| `shift-add` | Business management | Yes | — | Yes | Single per route |  | PASS (navigation) |
| `shift-reports` | الموظفين | — | — | Yes | Single per route |  | PASS (navigation) |
| `attendance` | Business management | Yes | — | Yes | Single per route | Admin · Feature `employee-attendance` | PASS (navigation) |
| `users-list` | Settings | — | — | — | Single per route | Admin | PASS (Settings hub navigation) |
| `operation-log` | Settings | — | — | — | Single per route | Admin | PASS (Settings hub navigation) |
| `user-permissions` | الإعدادات | — | — | — | Redirect → `users-list` | Admin | PASS (registry resolver) |
| `sales-permissions` | الإعدادات | — | — | — | Redirect → `users-list` | Admin | PASS (registry resolver) |
| `settings-permissions` | الإعدادات | — | — | — | Redirect → `users-list` | Admin | PASS (registry resolver) |
| `shift-approval` | الموظفين | — | — | — | Redirect → `shifts-list` | Feature `advanced-shifts` | PASS (registry resolver) |
| `profit-sales-report` | الإحصائيات والأرباح | — | — | Yes | Registered |  | PASS (navigation) |
| `profits` | الإحصائيات والأرباح | — | — | Yes | Registered |  | PASS (navigation) |
| `analytics-sales` | الإحصائيات والأرباح | — | — | Yes | Registered | Feature `advanced-reports` | PASS (navigation) |
| `analytics-purchases` | الإحصائيات والأرباح | — | — | Yes | Registered | Feature `advanced-reports` | PASS (navigation) |
| `analytics-suppliers` | الإحصائيات والأرباح | — | — | Yes | Registered | Feature `advanced-reports` | PASS (navigation) |
| `analytics-shifts` | الإحصائيات والأرباح | — | — | — | Redirect → `shift-reports` | Feature `advanced-shifts` | PASS (registry resolver) |
| `tracking-reports` | الإحصائيات والأرباح | — | — | Yes | Registered | Feature `advanced-reports` | PASS (navigation) |
| `installment-system` | Additional operations | Yes | — | Yes | Single per route | Feature `installments` | PASS (navigation) |
| `installment-payment` | Additional operations | Yes | — | Yes | Single per route | Feature `installments` | PASS (navigation) |
| `maintenance-intake` | Additional operations | Yes | — | Yes | Single per route | Feature `maintenance-management` | PASS (navigation) |
| `maintenance-tracking` | Additional operations | Yes | — | Yes | Single per route | Feature `maintenance-management` | PASS (navigation) |
| `branches-list` | Business management | Yes | — | Yes | Registered | Feature `multiple-branches` | PASS (navigation) |
| `branch-stock-transfer` | Business management | Yes | — | Yes | Registered | Feature `multiple-branches` | PASS (navigation) |
| `branch-finance` | Business management | Yes | — | Yes | Registered | Feature `multiple-branches` | PASS (navigation) |
| `cash-services` | Business management | Yes | — | Yes | Registered | Feature `cash-services` | PASS (navigation) |
| `shift-details` | الموظفين | — | — | — | Single per route | Grant `shifts-list` | NOT TESTED |
| `attendance-history` | Settings | — | — | — | Single per route | Admin · Feature `employee-attendance` | PASS (Settings hub navigation) |
| `delivery-orders` | Daily operations | Yes | — | Yes | Single per route |  | PASS (navigation) |
| `drivers-management` | Daily operations | Yes | — | Yes | Single per route |  | PASS (navigation) |
