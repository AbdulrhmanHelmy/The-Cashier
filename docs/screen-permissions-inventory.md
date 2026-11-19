# جرد الشاشات وصلاحيات المستخدمين

هذا الجرد مولد من `frontend/data/screens.json` بعد توحيد علامة `implemented` التي كان `app.js` يحتفظ بها في قائمة ثانية. يقرأ الخادم الاسم العربي والوحدة والوحدة الاختيارية ومعرفات التحويل و`permissionRoute` من السجل نفسه.

- عدد مسارات السجل التي جرى تدقيقها: **130**.
- كل مسار يصرح الآن بـ`implemented: true/false`؛ ويختبر سجل الواجهة وجود هذه العلامة حتى لا تعود قائمة تنفيذ موازية داخل الشِل.
- المسارات المنفذة المحددة حاليًا: **81** (يشمل مسارات التوافق المنفذة).
- صلاحيات الكاشير المستقلة بعد التنظيف: **53**.
- مفاتيح الكاشير القديمة التي لم تعد تظهر في المحرر: **44**.
- لم تظهر شاشة منفذة مستقلة بلا مفتاح قديم: **0**؛ كان الخادم يعرض كل مسارات السجل، بما فيها القديمة وغير المنفذة، بينما مصدر الأسماء والصفحات الفعلي كان موزعًا بين السجل وقائمة داخل `app.js`.
- الصلاحيات الجديدة التي تبدأ ممنوحة: **0**؛ أي شاشة جديدة تبدأ بلا صلاحية إلى أن يحددها مسؤول النظام.
- مسارات `permissionRoute` تُجمع تحت المفتاح المحدد. مسارات التحويل العادية لا تمنح وصولًا إلى وجهتها. استثناءات التنبيهات معلّمة `permissionControllable` لأن واجهة API تفرض صلاحية مستقلة لكل نوع تنبيه.
- لا توجد ترحيلات لقاعدة البيانات. مفاتيح التحويل القديمة تبقى محفوظة حتى يحفظ مسؤول النظام الصلاحيات؛ ولا تمنح تلك المفاتيح وصولًا جديدًا. المفاتيح القديمة ذات `permissionRoute` الصريح تُعرض كقرار الصلاحية القانوني، ثم تحفظ بهذا المعرف عند الحفظ.
- الشاشات الإدارية لا تظهر كصلاحيات كاشير؛ مسؤول النظام يظل بصلاحية كاملة. الوصول إلى أي شاشة مشروط كذلك بتفعيل `featureKey` إن وجد.
- أزيلت شاشة «نوع النشاط التجاري» من سجل الواجهة ومركز الإعدادات والبحث؛ بقيت جداول النشاط وواجهات API وتوصيات الميزات المحفوظة. لم يتغير سجل «الإعدادات العامة» أو عارضها أو عقود حفظها.
- يتصل `access-state.js` بالسجل لفرض المفتاح القانوني في القائمة الجانبية والوصول السريع والبحث وفتح المسار المباشر والتبويبات القائمة. تحديث الوصول يعيد بناء هذه المداخل ويغلق التبويبات التي سحبت صلاحيتها.

| معرف المسار | الاسم العربي | الوحدة | التنفيذ/الوصول | قابل للتحكم | الوحدة الاختيارية | المفتاح القديم | المفتاح المعتمد |
|---|---|---|---|---|---|---|---|
| `report-customers` | تقرير العملاء والمديونيات | التقارير | منفذة؛ القائمة الجانبية | نعم | — | `report-customers` | `report-customers` |
| `report-suppliers` | تقرير الموردين والمستحقات | التقارير | منفذة؛ القائمة الجانبية | نعم | — | `report-suppliers` | `report-suppliers` |
| `report-shifts` | تقرير الورديات والكاشير | التقارير | منفذة؛ القائمة الجانبية | نعم | `advanced-shifts` | `report-shifts` | `report-shifts` |
| `report-attendance` | تقرير الموظفين والحضور | التقارير | منفذة؛ القائمة الجانبية | نعم | `employee-attendance` | `report-attendance` | `report-attendance` |
| `report-maintenance` | تقرير الصيانة | التقارير | منفذة؛ القائمة الجانبية | نعم | `maintenance-management` | `report-maintenance` | `report-maintenance` |
| `report-installments` | تقرير التقسيط | التقارير | منفذة؛ القائمة الجانبية | نعم | `installments` | `report-installments` | `report-installments` |
| `report-overview` | ملخص النشاط | التقارير | منفذة؛ القائمة الجانبية | نعم | — | `report-overview` | `report-overview` |
| `report-purchases` | تقرير المشتريات | التقارير | منفذة؛ القائمة الجانبية | نعم | — | `report-purchases` | `report-purchases` |
| `report-returns` | تقرير المرتجعات | التقارير | منفذة؛ القائمة الجانبية | نعم | — | `report-returns` | `report-returns` |
| `report-payment-methods` | تقرير وسائل الدفع | التقارير | منفذة؛ القائمة الجانبية | نعم | — | `report-payment-methods` | `report-payment-methods` |
| `report-product-profit` | تقرير أرباح المنتجات | التقارير | منفذة؛ القائمة الجانبية | نعم | — | `report-product-profit` | `report-product-profit` |
| `report-inventory-valuation` | تقرير تقييم المخزون | التقارير | منفذة؛ القائمة الجانبية | نعم | — | `report-inventory-valuation` | `report-inventory-valuation` |
| `report-top-products` | المنتجات الأكثر مبيعًا | التقارير | منفذة؛ القائمة الجانبية | نعم | — | `report-top-products` | `report-top-products` |
| `report-low-stock` | تقرير نواقص المخزون | التقارير | منفذة؛ القائمة الجانبية | نعم | — | `report-low-stock` | `report-low-stock` |
| `report-inventory-movements` | تقرير حركة المخزون | التقارير | منفذة؛ القائمة الجانبية | نعم | — | `report-inventory-movements` | `report-inventory-movements` |
| `report-net-profit` | تقرير صافي الأرباح | التقارير | منفذة؛ القائمة الجانبية | نعم | — | `report-net-profit` | `report-net-profit` |
| `report-expenses` | تقرير المصروفات | التقارير | منفذة؛ القائمة الجانبية | نعم | — | `report-expenses` | `report-expenses` |
| `report-sales` | تقرير المبيعات | التقارير | منفذة؛ القائمة الجانبية | نعم | — | `report-sales` | `report-sales` |
| `payment-overview` | نظرة عامة | وسائل الدفع | منفذة؛ القائمة الجانبية | لا | — | — | إدارة فقط |
| `payment-wallets` | المحافظ الإلكترونية | وسائل الدفع | منفذة؛ القائمة الجانبية | لا | — | — | إدارة فقط |
| `payment-terminals` | ماكينات الفيزا | وسائل الدفع | منفذة؛ القائمة الجانبية | لا | — | — | إدارة فقط |
| `payment-history` | سجل العمليات | وسائل الدفع | منفذة؛ القائمة الجانبية | لا | — | — | إدارة فقط |
| `product-labels` | طباعة ملصقات المنتجات | المخازن | منفذة، مخفية/سياقية؛ استدعاء سياقي/مباشر | لا | `barcode-label-printing` | `product-labels` | `products-list` |
| `dashboard` | الرئيسية | الرئيسية | منفذة؛ القائمة الجانبية | نعم | — | `dashboard` | `dashboard` |
| `settings-program` | الإعدادات | الإعدادات | منفذة؛ رابط مباشر مسجل | لا | — | — | إدارة فقط |
| `feature-management` | الوحدات والميزات | الإعدادات | منفذة؛ مركز الإعدادات | لا | — | — | إدارة فقط |
| `settings-system` | إعدادات النظام | الإعدادات | تحويل إلى `settings-general`؛ مسار توافق | لا | — | — | إدارة فقط |
| `settings-general` | الإعدادات العامة | الإعدادات | منفذة؛ مركز الإعدادات | لا | — | — | إدارة فقط |
| `settings-company` | بيانات المنشأة | الإعدادات | منفذة؛ مركز الإعدادات | لا | — | — | إدارة فقط |
| `settings-appearance` | المظهر | الإعدادات | منفذة؛ مركز الإعدادات | لا | — | — | إدارة فقط |
| `settings-invoice` | إعدادات الفاتورة | الإعدادات | تحويل إلى `settings-printers`؛ مسار توافق | لا | — | — | إدارة فقط |
| `settings-printers` | الطابعات والطباعة | الإعدادات | منفذة؛ مركز الإعدادات | لا | — | — | إدارة فقط |
| `settings-backup` | البيانات والصيانة | الإعدادات | منفذة؛ مركز الإعدادات | لا | — | — | إدارة فقط |
| `customers-list` | إدارة العملاء | العملاء | منفذة؛ القائمة الجانبية | نعم | — | `customers-list` | `customers-list` |
| `customer-add` | إضافة عميل | العملاء | منفذة، مخفية/سياقية؛ استدعاء سياقي/مباشر | نعم | — | `customer-add` | `customer-add` |
| `customer-edit` | تعديل بيانات عميل | العملاء | منفذة، مخفية/سياقية؛ استدعاء سياقي/مباشر | نعم | — | `customer-edit` | `customer-edit` |
| `customers-export` | تصدير بيانات العملاء | العملاء | غير منفذة حاليًا؛ استدعاء سياقي/مباشر | لا | — | `customers-export` | — |
| `customer-subscriptions` | اشتراكات العملاء | العملاء | غير منفذة حاليًا؛ استدعاء سياقي/مباشر | لا | — | `customer-subscriptions` | — |
| `customer-statement` | كشف حساب العميل | العملاء | منفذة؛ القائمة الجانبية | نعم | — | `customer-statement` | `customer-statement` |
| `customer-credit-payment` | دفع آجل للعميل | العملاء | تحويل إلى `customer-statement`؛ مسار توافق | لا | — | `customer-credit-payment` | — |
| `pos` | الكاشير اللمسي | المبيعات | منفذة؛ القائمة الجانبية | نعم | — | `pos` | `pos` |
| `sales-management` | فواتير البيع | المبيعات | منفذة؛ القائمة الجانبية | نعم | — | `sales-management` | `sales-management` |
| `invoice-lookup` | البحث السريع برقم الفاتورة للتعديل | المبيعات | غير منفذة حاليًا؛ استدعاء سياقي/مباشر | لا | — | `invoice-lookup` | — |
| `sales-list` | قائمة فواتير البيع | المبيعات | منفذة؛ القائمة الجانبية | نعم | — | `sales-list` | `sales-list` |
| `sales-reports` | تقارير المبيعات | المبيعات | غير منفذة حاليًا؛ استدعاء سياقي/مباشر | لا | — | `sales-reports` | — |
| `sales-confirm` | تعديل أو تأكيد الفواتير | المبيعات | غير منفذة حاليًا؛ استدعاء سياقي/مباشر | لا | — | `sales-confirm` | — |
| `suppliers-list` | قائمة الموردين | الموردين | منفذة؛ القائمة الجانبية | نعم | — | `suppliers-list` | `suppliers-list` |
| `supplier-credit` | حسابات الموردين آجلاً | الموردين | تحويل إلى `supplier-payment`؛ مسار توافق | لا | — | `supplier-credit` | — |
| `supplier-purchases` | مشتريات الموردين | الموردين | تحويل إلى `supplier-payment`؛ مسار توافق | لا | — | `supplier-purchases` | — |
| `supplier-balance` | أرصدة الموردين | الموردين | تحويل إلى `supplier-payment`؛ مسار توافق | لا | — | `supplier-balance` | — |
| `supplier-payment` | حسابات الموردين | الموردين | منفذة؛ القائمة الجانبية | نعم | — | `supplier-payment` | `supplier-payment` |
| `supplier-payment-confirm` | مراجعة سداد الموردين | الموردين | تحويل إلى `supplier-payment`؛ مسار توافق | لا | — | `supplier-payment-confirm` | — |
| `supplier-reports` | تقارير الموردين | الموردين | تحويل إلى `supplier-payment`؛ مسار توافق | لا | — | `supplier-reports` | — |
| `supplier-paid-report` | تقرير مدفوعات الموردين | الموردين | تحويل إلى `supplier-payment`؛ مسار توافق | لا | — | `supplier-paid-report` | — |
| `purchase-management` | إدارة المشتريات | المشتريات | منفذة؛ القائمة الجانبية | نعم | — | `purchase-management` | `purchase-management` |
| `purchase-supplier` | إدارة الموردين أثناء الشراء | المشتريات | تحويل إلى `purchase-management`؛ مسار توافق | لا | — | `purchase-supplier` | — |
| `purchases-list` | قائمة المشتريات | المشتريات | منفذة؛ القائمة الجانبية | نعم | — | `purchases-list` | `purchases-list` |
| `purchase-reports` | تقارير المشتريات | المشتريات | تحويل إلى `purchases-list`؛ مسار توافق | لا | — | `purchase-reports` | — |
| `purchase-expiry` | تعيين تاريخ صلاحية المنتجات | المشتريات | تحويل إلى `purchase-management`؛ مسار توافق | لا | — | `purchase-expiry` | — |
| `purchase-confirm` | تأكيد عملية الشراء | المشتريات | تحويل إلى `purchase-management`؛ مسار توافق | لا | — | `purchase-confirm` | — |
| `returns-overview` | إدارة المرتجعات | المرتجعات | منفذة؛ القائمة الجانبية | نعم | — | `returns-overview` | `returns-overview` |
| `return-details` | تفاصيل المرتجع | المرتجعات | منفذة؛ القائمة الجانبية | لا | — | `return-details` | `returns-overview` |
| `sales-returns` | إدارة مرتجعات المبيعات | المرتجعات | تحويل إلى `returns-overview`؛ مسار توافق | لا | — | `sales-returns` | — |
| `purchase-returns` | إدارة مرتجعات المشتريات | المرتجعات | تحويل إلى `returns-overview`؛ مسار توافق | لا | — | `purchase-returns` | — |
| `receipt-printer` | تسطيب برنتر الفواتير | الإعدادات | تحويل إلى `settings-printers`؛ مسار توافق | لا | — | — | إدارة فقط |
| `barcode-printer` | تسطيب برنتر الباركود | الإعدادات | تحويل إلى `settings-printers`؛ مسار توافق | لا | `barcode-label-printing` | — | إدارة فقط |
| `barcode-designer` | تصميم وطباعة الباركود | الإعدادات | تحويل إلى `settings-printers`؛ مسار توافق | لا | `barcode-label-printing` | — | إدارة فقط |
| `print-design` | إعدادات التصميم | الإعدادات | تحويل إلى `settings-printers`؛ مسار توافق | لا | `barcode-label-printing` | — | إدارة فقط |
| `print-advanced` | التنسيق المتقدم | الإعدادات | تحويل إلى `settings-printers`؛ مسار توافق | لا | `barcode-label-printing` | — | إدارة فقط |
| `barcode-product-edit` | تعديل السعر والكمية والباركود | الإعدادات | تحويل إلى `settings-printers`؛ مسار توافق | لا | `barcode-label-printing` | — | إدارة فقط |
| `expenses-list` | إدارة المصروفات | المصروفات والسندات | منفذة؛ القائمة الجانبية | نعم | — | `expenses-list` | `expenses-list` |
| `expense-categories` | أنواع المصروفات | المصروفات والسندات | تحويل إلى `expenses-list`؛ مسار توافق | لا | — | `expense-categories` | — |
| `expense-add` | سند صرف | المصروفات والسندات | منفذة؛ القائمة الجانبية | نعم | — | `expense-add` | `expense-add` |
| `financial-movement` | حركة مالية | المصروفات والسندات | تحويل إلى `expense-add`؛ مسار توافق | لا | — | `financial-movement` | — |
| `accounts-list` | قائمة الخزنة والبنك | الخزنة والبنك | منفذة؛ القائمة الجانبية | نعم | — | `accounts-list` | `accounts-list` |
| `account-deposit` | إيداع رصيد | الخزنة والبنك | منفذة؛ القائمة الجانبية | نعم | — | `account-deposit` | `account-deposit` |
| `account-withdraw` | سحب رصيد | الخزنة والبنك | منفذة؛ القائمة الجانبية | نعم | — | `account-withdraw` | `account-withdraw` |
| `account-add` | إضافة خزنة جديدة | الخزنة والبنك | تحويل إلى `accounts-list`؛ مسار توافق | لا | — | `account-add` | — |
| `accounts-balance` | الرصيد الحالي للخزنة والبنك | الخزنة والبنك | تحويل إلى `accounts-list`؛ مسار توافق | لا | — | `accounts-balance` | — |
| `account-movements` | حركة الخزنة والبنك | الخزنة والبنك | تحويل إلى `accounts-list`؛ مسار توافق | لا | — | `account-movements` | — |
| `alerts-list` | التنبيهات | التنبيهات | منفذة؛ القائمة الجانبية | نعم | — | `alerts-list` | `alerts-list` |
| `reorder-alert` | تنبيه حد الطلب | التنبيهات | تحويل إلى `alerts-list`؛ مسار توافق | نعم | — | `reorder-alert` | `reorder-alert` |
| `low-stock-alert` | تنبيه نقص الأصناف | التنبيهات | تحويل إلى `alerts-list`؛ مسار توافق | نعم | — | `low-stock-alert` | `low-stock-alert` |
| `warehouses-list` | دليل المخازن | المخزن | منفذة؛ القائمة الجانبية | نعم | `basic-inventory` | `warehouses-list` | `warehouses-list` |
| `warehouse-add` | إضافة مخزن جديد | المخزن | تحويل إلى `warehouses-list`؛ مسار توافق | لا | `multiple-warehouses` | `warehouse-add` | — |
| `warehouse-settings` | إعدادات المخزن (مسار سابق) | الإعدادات | تحويل مخفي إلى `warehouses-list`؛ مسار توافق | لا | `multiple-warehouses` | — | إدارة فقط |
| `stock-adjustment` | تسوية المخزون | المخزن | تحويل إلى `warehouses-list`؛ مسار توافق | لا | — | `stock-adjustment` | — |
| `warehouse-transfer` | نقل الأصناف بين المخازن | المخزن | تحويل إلى `warehouses-list`؛ مسار توافق | لا | `multiple-warehouses` | `warehouse-transfer` | — |
| `stock-movements` | حركة الأصناف | المخزن | تحويل إلى `warehouses-list`؛ مسار توافق | لا | — | `stock-movements` | — |
| `low-stock` | الأصناف الناقصة | المخزن | تحويل إلى `report-low-stock`؛ مسار توافق | لا | — | `low-stock` | — |
| `unit-add` | وحدات القياس | الإعدادات | منفذة؛ دخول سياقي من دليل المنتجات | لا | `product-catalog` | — | إدارة فقط |
| `product-categories` | تصنيفات المنتجات | الإعدادات | منفذة؛ دخول سياقي من دليل المنتجات | لا | `product-catalog` | — | إدارة فقط |
| `manufacturing` | تصنيع | المخزن | غير منفذة حاليًا؛ استدعاء سياقي/مباشر | لا | `manufacturing` | `manufacturing` | — |
| `products-list` | دليل المنتجات | المخزن | منفذة؛ القائمة الجانبية | نعم | `product-catalog` | `products-list` | `products-list` |
| `product-add` | إضافة المنتجات | المخزن | منفذة، مخفية/سياقية؛ استدعاء سياقي/مباشر | نعم | `product-catalog` | `product-add` | `product-add` |
| `product-edit` | تعديل المنتجات | المخزن | منفذة، مخفية/سياقية؛ استدعاء سياقي/مباشر | نعم | `product-catalog` | `product-edit` | `product-edit` |
| `product-quick-add` | إضافة منتج سريع | المخزن | منفذة، مخفية/سياقية؛ الوصول السريع | نعم | `product-catalog` | `product-quick-add` | `product-quick-add` |
| `employees-list` | إضافة وإدارة الموظفين | الموظفين | منفذة؛ القائمة الجانبية | لا | `employee-management` | — | إدارة فقط |
| `employee-add` | إضافة موظف جديد | الموظفين | منفذة، مخفية/سياقية؛ استدعاء سياقي/مباشر | لا | `employee-management` | — | إدارة فقط |
| `employee-payroll` | صرف الرواتب | الموظفين | منفذة؛ القائمة الجانبية | لا | `employee-management` | — | إدارة فقط |
| `employee-advances` | السلفيات | الموظفين | منفذة؛ القائمة الجانبية | لا | `employee-management` | — | إدارة فقط |
| `shifts-list` | ورديات الكاشير | الموظفين | منفذة؛ القائمة الجانبية | نعم | — | `shifts-list` | `shifts-list` |
| `shift-add` | الوردية الحالية | الموظفين | منفذة؛ القائمة الجانبية | نعم | — | `shift-add` | `shift-add` |
| `shift-reports` | تقارير الورديات | الموظفين | منفذة؛ القائمة الجانبية | نعم | — | `shift-reports` | `shift-reports` |
| `attendance` | الحضور والانصراف | الموظفين | منفذة؛ القائمة الجانبية | لا | `employee-attendance` | — | إدارة فقط |
| `users-list` | المستخدمون والصلاحيات | الإعدادات | منفذة؛ مركز الإعدادات | لا | — | — | إدارة فقط |
| `operation-log` | سجل العمليات | الإعدادات | منفذة؛ مركز الإعدادات | لا | — | — | إدارة فقط |
| `user-permissions` | صلاحيات المستخدمين | الإعدادات | تحويل إلى `users-list`؛ مسار توافق | لا | — | — | إدارة فقط |
| `sales-permissions` | صلاحيات إدارة المبيعات | الإعدادات | تحويل إلى `users-list`؛ مسار توافق | لا | — | — | إدارة فقط |
| `settings-permissions` | صلاحيات الإعدادات | الإعدادات | تحويل إلى `users-list`؛ مسار توافق | لا | — | — | إدارة فقط |
| `shift-approval` | إضافة ورديات وتأكيدها | الموظفين | تحويل إلى `shifts-list`؛ مسار توافق | لا | `advanced-shifts` | `shift-approval` | — |
| `profit-sales-report` | تقارير أرباح المبيعات | الإحصائيات والأرباح | غير منفذة حاليًا؛ رابط مباشر مسجل | لا | — | `profit-sales-report` | — |
| `profits` | الأرباح | الإحصائيات والأرباح | غير منفذة حاليًا؛ رابط مباشر مسجل | لا | — | `profits` | — |
| `analytics-sales` | تقارير المبيعات | الإحصائيات والأرباح | غير منفذة حاليًا؛ رابط مباشر مسجل | لا | `advanced-reports` | `analytics-sales` | — |
| `analytics-purchases` | تقارير المشتريات | الإحصائيات والأرباح | غير منفذة حاليًا؛ رابط مباشر مسجل | لا | `advanced-reports` | `analytics-purchases` | — |
| `analytics-suppliers` | تقارير الموردين | الإحصائيات والأرباح | غير منفذة حاليًا؛ رابط مباشر مسجل | لا | `advanced-reports` | `analytics-suppliers` | — |
| `analytics-shifts` | تقارير الورديات | الإحصائيات والأرباح | تحويل إلى `shift-reports`؛ مسار توافق | لا | `advanced-shifts` | `analytics-shifts` | — |
| `tracking-reports` | تقارير التتبع | الإحصائيات والأرباح | غير منفذة حاليًا؛ رابط مباشر مسجل | لا | `advanced-reports` | `tracking-reports` | — |
| `installment-system` | البيع بالتقسيط | البيع بالتقسيط | منفذة؛ القائمة الجانبية | نعم | `installments` | `installment-system` | `installment-system` |
| `installment-payment` | سداد القسط | البيع بالتقسيط | منفذة؛ القائمة الجانبية | نعم | `installments` | `installment-payment` | `installment-payment` |
| `maintenance-intake` | تسجيل جهاز للصيانة | الصيانة | منفذة؛ القائمة الجانبية | نعم | `maintenance-management` | `maintenance-intake` | `maintenance-intake` |
| `maintenance-tracking` | قائمة الصيانة | الصيانة | منفذة؛ القائمة الجانبية | نعم | `maintenance-management` | `maintenance-tracking` | `maintenance-tracking` |
| `branches-list` | الفروع والفرعيات | الفروع والخدمات | غير منفذة حاليًا؛ القائمة الجانبية | لا | `multiple-branches` | `branches-list` | — |
| `branch-stock-transfer` | حركة الأصناف بين الفروع | الفروع والخدمات | غير منفذة حاليًا؛ القائمة الجانبية | لا | `multiple-branches` | `branch-stock-transfer` | — |
| `branch-finance` | حركة مالية للفروع | الفروع والخدمات | غير منفذة حاليًا؛ القائمة الجانبية | لا | `multiple-branches` | `branch-finance` | — |
| `cash-services` | خدمات كاش | الفروع والخدمات | غير منفذة حاليًا؛ القائمة الجانبية | لا | `cash-services` | `cash-services` | — |
| `shift-details` | تفاصيل الوردية | الموظفين | منفذة، مخفية/سياقية؛ استدعاء سياقي/مباشر | لا | — | `shift-details` | `shifts-list` |
| `attendance-history` | سجل الحضور | الموظفين | منفذة؛ مركز الإعدادات | لا | `employee-attendance` | — | إدارة فقط |
| `delivery-orders` | طلبات التوصيل | المبيعات | منفذة؛ القائمة الجانبية | نعم | — | `delivery-orders` | `delivery-orders` |
| `drivers-management` | إدارة الطيارين | المبيعات | منفذة؛ القائمة الجانبية | نعم | — | `drivers-management` | `drivers-management` |
