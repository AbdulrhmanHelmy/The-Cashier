
"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");
require("./export-tests")();
require("./sidebar-flyout-tests")();
require("./quickbar-tests")();
var root = path.resolve(__dirname, "..", ".."), calculations = require(path.join(root, "frontend", "js", "core", "calculations.js"));
var registry = JSON.parse(fs.readFileSync(path.join(root, "frontend", "data", "screens.json"), "utf8"));
var indexHtml = fs.readFileSync(path.join(root, "frontend", "index.html"), "utf8");
var layoutCss = fs.readFileSync(path.join(root, "frontend", "css", "layout.css"), "utf8");
var tokensCss = fs.readFileSync(path.join(root, "frontend", "css", "tokens.css"), "utf8");
var themesCss = fs.readFileSync(path.join(root, "frontend", "css", "themes.css"), "utf8");
var componentsCss = fs.readFileSync(path.join(root, "frontend", "css", "components.css"), "utf8");
var modulesCss = fs.readFileSync(path.join(root, "frontend", "css", "modules.css"), "utf8");
var appJs = fs.readFileSync(path.join(root, "frontend", "js", "app.js"), "utf8");
var apiClientJs = fs.readFileSync(path.join(root, "frontend", "js", "api-client.js"), "utf8");
var featureJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "feature-management.js"), "utf8");
var featureStateJs = fs.readFileSync(path.join(root, "frontend", "js", "services", "feature-state.js"), "utf8");
var accessStateJs = fs.readFileSync(path.join(root, "frontend", "js", "services", "access-state.js"), "utf8");
var userManagementJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "user-management.js"), "utf8");
var dashboardJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "dashboard.js"), "utf8");
var renderersJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "renderers.js"), "utf8");
var catalogJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "catalog.js"), "utf8");
var inventoryJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "inventory.js"), "utf8");
var returnsJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "returns.js"), "utf8");
var expensesJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "expenses.js"), "utf8");
var treasuryJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "treasury.js"), "utf8");
var uiJs = fs.readFileSync(path.join(root, "frontend", "js", "components", "ui.js"), "utf8");
var settingsHubJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "settings-hub.js"), "utf8");
var backupSettingsJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "backup-settings.js"), "utf8");
var generalSettingsJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "general-settings.js"), "utf8");
var settingsStateJs = fs.readFileSync(path.join(root, "frontend", "js", "services", "settings-state.js"), "utf8");
var salesJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "sales.js"), "utf8");
var purchasingJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "purchasing.js"), "utf8");
var loginHtml = fs.readFileSync(path.join(root, "frontend", "login.html"), "utf8");
var loginJs = fs.readFileSync(path.join(root, "frontend", "js", "login.js"), "utf8");
var mainFormCs = fs.readFileSync(path.join(root, "src", "AlKhayal.UI", "MainForm.cs"), "utf8");
var serverHostCs = fs.readFileSync(path.join(root, "src", "AlKhayal.Server.Host", "AlKhayalServer.cs"), "utf8");
var workforceJs = fs.readFileSync(path.join(root, "frontend", "js", "modules", "workforce.js"), "utf8");
var printingJs = fs.readFileSync(path.join(root,"frontend/js/modules/printing.js"),"utf8");
var labelsJs=fs.readFileSync(path.join(root,"frontend/js/modules/labels.js"),"utf8");
var allowedRenderers = new Set(["reports-center","operational-reports","stock-reports","net-profit-report","expense-report","transaction-reports","sales-report","payment-methods","product-labels","native-printing", "workforce-employees", "workforce-attendance", "workforce-shifts", "workforce-payroll", "workforce-advances", "dashboard", "settings", "settings-hub", "legacy-redirect", "under-construction", "feature-management", "user-management", "company-settings", "general-settings", "appearance-settings", "database-settings", "catalog-products", "catalog-product-form", "catalog-categories", "catalog-units", "warehouse-directory", "treasury-accounts", "treasury-movement", "expense-categories", "expense-voucher", "returns-management", "return-details", "entity-list", "entity-form", "statement", "payment", "export", "report", "pos", "sales-invoices", "sales-returns", "invoice-lookup", "workflow", "invoice-editor", "picker", "returns", "printer", "barcode", "expense-form", "alerts", "threshold", "adjustment", "transfer", "manufacturing", "attendance", "permissions", "installments", "maintenance-intake", "maintenance-tracking", "cash-services", "purchase-management", "purchase-list", "purchase-details", "purchase-returns", "delivery-orders", "drivers-management"]);
allowedRenderers.add("operation-log");
allowedRenderers.add("business-activities");

assert.ok(mainFormCs.indexOf("Application.ExecutablePath") >= 0 && mainFormCs.indexOf("ExtractAssociatedIcon") >= 0, "desktop host must use the executable AlKhayal icon");
assert.ok(mainFormCs.indexOf("WindowState = FormWindowState.Maximized") >= 0 && mainFormCs.indexOf("FormBorderStyle.None") < 0, "desktop host must start maximized with normal window chrome");
assert.ok(mainFormCs.indexOf("Screen.PrimaryScreen.Bounds") < 0 && mainFormCs.indexOf("keyData == Keys.F11") < 0, "desktop host must not implement custom true-fullscreen toggling");

assert.ok(loginHtml.indexOf("مرحبًا بك في The Cashier. يجب إنشاء حساب المدير للبدء.") >= 0, "login must contain the first-run Arabic message");
assert.ok(loginHtml.indexOf("v1.1.0") >= 0 && indexHtml.indexOf("v1.1.0") >= 0, "login and persistent status bar must show application version 1.1.0");
assert.ok(indexHtml.indexOf("آخر نسخة: غير متحقق") < 0, "persistent status bar must not show an unverified latest-version notice");
assert.ok(loginHtml.indexOf("إنشاء حساب المدير") >= 0, "login must expose the first Admin action");
assert.ok(loginJs.indexOf("/api/auth/bootstrap-status") >= 0 && loginJs.indexOf("/api/auth/bootstrap") >= 0, "login must use the bootstrap API contract");
assert.ok(loginHtml.indexOf('id="loginUsers"') >= 0 && loginJs.indexOf("/api/auth/login-users") >= 0, "login must load the active-user picker through its dedicated API");
assert.ok(loginHtml.indexOf("تذكر كلمة المرور") >= 0 && loginJs.indexOf("hostObjects.sync.alkhayalCredentials") >= 0, "desktop login must use the narrow native credential bridge");
assert.ok(loginJs.indexOf("alkhayal_username") < 0 && loginJs.indexOf("localStorage") < 0, "login must not persist credentials in browser storage");
assert.ok(apiClientJs.indexOf("request_timeout") >= 0 && apiClientJs.indexOf("20000") >= 0, "API requests must abort after a bounded wait instead of leaving pages loading indefinitely");
assert.ok(renderersJs.indexOf("maintenanceCustomerSearch") >= 0 && renderersJs.indexOf("maintenanceTechnicianSearch") >= 0 && renderersJs.indexOf("/api/maintenance/tickets/lookup/customers?query=") >= 0 && renderersJs.indexOf("/api/maintenance/tickets/lookup/technicians?query=") >= 0 && renderersJs.indexOf('document.title===screen.title+" | The Cashier"') >= 0, "maintenance intake must search customers and technicians and discard responses for inactive screens");
assert.ok(/function maintenanceIntake\(screen, container, context\)/.test(renderersJs) && /function maintenanceTracking\(screen, container, context\)/.test(renderersJs), "maintenance renderers must receive the screen context used by async callbacks");
assert.ok(renderersJs.indexOf('id: "received", label: "تاريخ الاستلام", type: "date"') >= 0, "maintenance intake must render the receipt-date field that it initializes and submits");

var routes = registry.screens.map(function (screen) { return screen.route; });
assert.strictEqual(new Set(routes).size, routes.length, "screen routes must be unique");
assert.ok(routes.length >= 90, "the full inventory should register at least 90 distinct screens");
var operationLogScreen = registry.screens.find(function (screen) { return screen.route === "operation-log"; });
assert.ok(operationLogScreen && operationLogScreen.administratorOnly === true && operationLogScreen.settingsGroup === "users-security" && operationLogScreen.renderer === "operation-log", "operation log must be an administrator-only Settings route with a renderer");
registry.modules.forEach(function (module) { module.items.forEach(function (route) { assert.ok(routes.indexOf(route) >= 0, "navigation route is missing: " + route); }); });
var sidebarRoutes = registry.modules.reduce(function(all,module){return all.concat(module.items);},[]);
registry.modules.forEach(function (module) {
  if (module.landingRoute) assert.ok(module.items.includes(module.landingRoute), "module landing must be one of its registered destinations");
});
var indexedScreens = Object.fromEntries(registry.screens.map(function (screen) { return [screen.route, screen]; }));
var redirects = { screensByRoute: indexedScreens };
vm.runInNewContext(appJs.match(/  function resolveRoute\(route\) \{[^\n]+/)[0], redirects);
registry.screens.filter(function (screen) { return screen.redirectTo; }).forEach(function (screen) {
  var destination = indexedScreens[redirects.resolveRoute(screen.route)];
  assert.ok(screen.navigationHidden && destination && !destination.redirectTo, "compatibility route must resolve without cycles: " + screen.route);
});

assert.strictEqual(new Set(sidebarRoutes).size,sidebarRoutes.length,"main sidebar routes must not be duplicated");
assert.equal(registry.screens.find(function(screen){return screen.route==="low-stock";}).redirectTo,"report-low-stock","legacy availability must open the persisted report");
assert.deepStrictEqual(registry.quickAccess.map(function(item){return item.route;}),["pos","product-quick-add","purchase-management","customers-list","warehouses-list","returns-overview","accounts-list","alerts-list"],"quick access must prioritize existing daily operation routes");
assert.ok(appJs.indexOf('localStorage.removeItem("alkhayal_quickbar_collapsed")')>=0&&appJs.indexOf("data-quickbar-toggle")<0&&appJs.indexOf("quickbar-collapsed")<0,"quick access must discard the obsolete hidden preference and never render a hide control");
assert.ok(appJs.indexOf('tab.view = document.createElement("div")')>=0&&appJs.indexOf('workspace.appendChild(tab.view)')>=0&&appJs.indexOf('tab.view === view')>=0,"each workspace tab must preserve its mounted page and reject stale async updates after replacement");
assert.ok(appJs.indexOf('"alerts-list"')>=0&&renderersJs.indexOf('function alerts(screen, container)')>=0,"notifications shortcut must open its registered notifications renderer");
var reportRoutes = registry.screens.filter(function (screen) { return screen.route.indexOf("report-") === 0 && !screen.navigationHidden; });
assert.deepStrictEqual(registry.modules.find(function (module) { return module.id === "reports"; }).items, ["report-overview"], "Reports must have one sidebar destination");
assert.deepStrictEqual(sidebarRoutes.filter(function (route) { return route.indexOf("report-") === 0; }), ["report-overview"], "individual reports must not appear in the sidebar");
assert.strictEqual(reportRoutes.length, 18, "existing report routes must remain registered");
assert.ok(reportRoutes.filter(function (screen) { return screen.route !== "report-overview"; }).every(function (screen) { return screen.reportCategory && screen.implemented; }), "every report must have a directory category");
require("./reports-center-tests")();
["report-sales","report-purchases","report-returns","report-payment-methods","report-expenses","report-net-profit","payment-history"].forEach(function(route){assert.ok(registry.screens.find(function(screen){return screen.route===route;}).implemented===true,"the registry must mark implemented reports and history screens: "+route);});
assert.deepStrictEqual(registry.modules.find(function(module){return module.id==="inventory";}).items,["products-list","warehouses-list"],"inventory sidebar must keep its operational directories compact");
assert.ok(indexHtml.indexOf('js/modules/inventory.js')>=0&&inventoryJs.indexOf('R["warehouse-directory"]=directory')>=0,"real warehouse renderer must load after catalog module");
assert.ok(indexHtml.indexOf('js/modules/purchasing.js')>=0&&purchasingJs.indexOf('global.AlKhayalRenderers["purchase-management"]')>=0&&purchasingJs.indexOf('global.AlKhayalRenderers["purchase-returns"]')>=0,"purchasing and traceable return screens must load real renderers");
assert.deepStrictEqual(registry.modules.find(function (module) { return module.id === "purchases"; }).items, ["purchase-management", "purchases-list"], "purchases sidebar must retain its workflow pages without duplicating reports");
["purchase-supplier", "purchase-reports", "purchase-expiry", "purchase-confirm"].forEach(function (route) {
  var screen = registry.screens.find(function (item) { return item.route === route; });
  assert.strictEqual(screen.navigationHidden, true, "removed purchase page must be hidden: " + route);
  assert.strictEqual(screen.renderer, "legacy-redirect");
  assert.strictEqual(screen.redirectTo, route === "purchase-reports" ? "purchases-list" : "purchase-management");
});
assert.ok(purchasingJs.indexOf('"imei-tracking"')>=0&&purchasingJs.indexOf('returnMode: state.mode')>=0&&purchasingJs.indexOf('خزنة مرتجعات الشراء')>=0,"purchase UI must collect tracking identities and explain the cash refund destination");
assert.ok(indexHtml.indexOf('js/modules/sales.js')>=0&&salesJs.indexOf('/api/sales/drafts')>=0&&salesJs.indexOf('/api/sales/')>=0&&salesJs.indexOf('/api/catalog/barcodes/lookup')>=0,"standard sales and touch cashier must use persistent invoice, posting, and barcode APIs");
assert.ok(salesJs.indexOf('R["sales-returns"]=salesReturns')>=0&&salesJs.indexOf('/payments')>=0&&salesJs.indexOf('/returns')>=0,"sales collections and returns must use persisted APIs and dedicated renderers");
assert.ok(salesJs.indexOf('الكاشير اللمسي')>=0&&salesJs.indexOf('data-checkout')<0&&modulesCss.indexOf('.touch-pos')>=0&&modulesCss.indexOf('touch-cashier-active')>=0,"touch cashier must use a dedicated bounded layout rather than the demonstration checkout");
assert.ok(salesJs.indexOf('container.oncontextmenu=productContextMenu')>=0&&salesJs.indexOf('data-context-unit')>=0&&salesJs.indexOf('data-sale-unit-count')>=0&&modulesCss.indexOf('.touch-unit-menu')>=0,"Touch POS must offer a viewport-bounded, accessible context menu only for multi-unit product cards");
assert.ok(/\.workspace\.touch-cashier-active > \.workspace-page\s*\{[^}]*height: 100%;[^}]*min-height: 0;/.test(modulesCss)&&/\.workspace\.touch-cashier-active > \.workspace-page > \.touch-pos\s*\{[^}]*height: 100%;/.test(modulesCss),"Touch POS must inherit the bounded workspace height through the mounted tab wrapper");
assert.ok(/\.touch-cart\s*\{[^}]*grid-template-rows: auto minmax\(0,1fr\) auto/.test(modulesCss)&&/\.touch-cart > \.sale-summary\s*\{[^}]*min-height: 0;[^}]*overflow: auto/.test(modulesCss),"wrapped payment controls and checkout must stay accessible while cart lines scroll independently");
assert.ok(salesJs.indexOf('listQuery')>=0&&salesJs.indexOf('productQuery')>=0,"invoice history and catalog search must keep independent query state");
assert.ok(salesJs.indexOf('method:state.invoice?"PUT":"POST"')>=0,"posting an existing draft must persist the current editor payload before posting");
assert.ok(salesJs.indexOf('function variantLabel')>=0&&salesJs.indexOf("variantName:variantLabel(product,variant)")>=0,"touch and standard carts must preserve a readable selected-variant label");
assert.ok(salesJs.indexOf('getTimezoneOffset')>=0,"sales dates must use the local business date rather than the UTC date boundary");
assert.ok(appJs.indexOf('activeRoute = route; localStorage.setItem("alkhayal_last_route",route)')>=0,"workspace tab activation must persist the actual active route for a later reload");
assert.ok(appJs.indexOf('id="navigationBack"')>=0&&appJs.indexOf("function navigateBack()")>=0&&appJs.indexOf("navigationHistory.push(activeRoute)")>=0&&appJs.indexOf('quickbar-back')>=0,"back button at the left edge of quick access must return to the previous visited screen");
assert.ok(salesJs.indexOf('occurredOn:localDate()')>=0,"sales collections must use the local business date rather than the UTC date boundary");
assert.ok(modulesCss.indexOf('grid-template-rows:minmax(180px,.8fr) minmax(250px,1fr)')<0&&modulesCss.indexOf('.touch-head{height:42px}')<0,"compact touch layout must not stack fixed-height panes or collapse a multi-row header");
assert.ok(!registry.screens.find(function(x){return x.route==="pos";}).description.includes("محاكاة")&&registry.screens.find(function(x){return x.route==="pos";}).title==="الكاشير اللمسي","POS registry must use the exact cashier title and no mock description");
assert.ok(inventoryJs.indexOf('/api/inventory/post')>=0&&inventoryJs.indexOf('/api/inventory/counts')>=0&&inventoryJs.indexOf('/api/inventory/movements')>=0,"inventory views must use persisted APIs");
assert.ok(registry.screens.filter(function(screen){return ["stock-adjustment","warehouse-transfer","stock-movements"].indexOf(screen.route)>=0;}).every(function(screen){return screen.redirectTo==="warehouses-list";}),"legacy stock routes must resolve through the warehouse directory");
registry.quickAccess.forEach(function (item) { assert.ok(routes.indexOf(item.route) >= 0, "quick-access route is missing: " + item.route); });
registry.screens.forEach(function (screen) { assert.ok(allowedRenderers.has(screen.renderer), "unknown renderer: " + screen.renderer); assert.ok(screen.title && screen.module && screen.description, "screen metadata is incomplete: " + screen.route); });
assert.ok(indexHtml.indexOf('id="workspace"') >= 0 && indexHtml.indexOf('id="sidebarNavigation"') >= 0, "shell scroll regions must remain addressable");
assert.ok(layoutCss.indexOf("min-height: 0") >= 0 && layoutCss.indexOf("overflow-y: auto") >= 0, "shell must define a bounded vertical content scroller");
assert.ok(layoutCss.indexOf("grid-template-columns: minmax(0, 1fr) var(--sidebar-width)") >= 0, "shell must use intrinsic grid sizing");
assert.ok(modulesCss.indexOf("height: calc(100vh") < 0, "module layouts must not own a second viewport height");
assert.ok(indexHtml.indexOf("toggle-icon-close") >= 0 && indexHtml.indexOf("toggle-icon-open") >= 0, "sidebar toggle must expose directional panel icons");
assert.ok(indexHtml.includes('class="sidebar-logo"') && indexHtml.includes('نظام إدارة الأعمال'), "sidebar displays the authentic mark and Arabic brand subtitle");
assert.ok(layoutCss.indexOf(".quickbar.collapsed .quickbar-tiles")<0&&layoutCss.indexOf(".quickbar-menu")>=0,"quick access must use a compact overflow menu instead of a hidden state");
assert.ok(indexHtml.indexOf("طي القائمة الجانبية") >= 0 && appJs.indexOf("توسيع القائمة الجانبية") >= 0, "sidebar toggle labels must be Arabic and state-specific");
assert.ok(appJs.indexOf("iconPaths") >= 0 && appJs.indexOf("settings:") >= 0 && appJs.indexOf("suppliers:") >= 0, "navigation icons must use the local semantic SVG map");
assert.ok(layoutCss.indexOf("gap: 0; padding-inline: 0") >= 0, "collapsed rail controls must remove expanded spacing");
assert.ok(tokensCss.indexOf("--sidebar-bg")>=0&&themesCss.indexOf("--tc-sidebar-bg")>=0&&layoutCss.indexOf("var(--sidebar-active)")>=0,"sidebar colors must use distinct light/dark semantic tokens for active and hover states");
var scaleSettingsRoute=registry.screens.find(function(screen){return screen.route==="business-activities";});
var scaleSettingsJs=fs.readFileSync(path.join(root,"frontend/js/modules/business-activities.js"),"utf8");
backupSettingsJs += "\n" + scaleSettingsJs;
assert.ok(scaleSettingsRoute&&scaleSettingsRoute.title==="إعداد الموازين"&&scaleSettingsRoute.description==="إعداد باركود الموازين وربط رموز PLU وSKU."&&scaleSettingsRoute.settingsGroup==="operations", "Scale Settings must retain its compatible route with the approved Settings metadata");
assert.ok(indexHtml.indexOf("business-activities.js")>=0&&scaleSettingsJs.indexOf("/api/catalog/scale-barcode-profiles")>=0, "Scale Settings must remain loaded and use the persisted scale-profile API");
assert.ok(scaleSettingsJs.indexOf("/api/business-activities")<0&&scaleSettingsJs.indexOf("data-save-activities")<0&&scaleSettingsJs.indexOf("activity-card")<0, "Scale Settings must not load, display, or update business activity configuration");
assert.ok(!settingsHubJs.includes("نوع النشاط التجاري")&&settingsHubJs.includes('"modules-features":{title:"الوحدات والميزات"'), "Settings must expose module configuration without an activity-type entry");
assert.ok(registry.screens.some(function(screen){return screen.route==="feature-management"&&screen.settingsGroup==="modules-features";}), "module and feature configuration must remain registered in Settings");
var generalSettingsRoute=registry.screens.find(function(screen){return screen.route==="settings-general";});
assert.ok(generalSettingsRoute&&generalSettingsRoute.title==="الإعدادات العامة"&&generalSettingsRoute.renderer==="general-settings"&&generalSettingsRoute.settingsGroup==="general", "approved General Settings metadata and renderer must remain unchanged");
assert.ok(appJs.indexOf("var initial=HOME_ROUTE") >= 0, "login must always start on the Home screen");
assert.ok(routes.indexOf("feature-management") >= 0 && indexHtml.indexOf("feature-management.js") >= 0 && indexHtml.indexOf("feature-state.js") >= 0, "feature management must be registered and loaded");
assert.ok(featureJs.indexOf('"/api/features/"') >= 0 && featureJs.indexOf('kind === "module" ? "modules/" : "capabilities/"') >= 0, "feature switches must persist through the real API");
assert.ok(featureJs.indexOf("UI.confirm") >= 0 && featureJs.indexOf("reason.message") >= 0, "feature changes must confirm and expose API errors");
assert.ok(featureJs.indexOf('role === "administrator"') >= 0 && featureJs.indexOf("للعرض فقط") >= 0, "cashiers must receive read-only feature management");
assert.ok(featureStateJs.indexOf("/api/features/effective") >= 0 && appJs.indexOf("Features.routeEnabled") >= 0, "navigation must use the effective server snapshot");
assert.ok(appJs.indexOf("لا تملك صلاحية فتح هذه الشاشة أو أن وحدتها معطلة") >= 0 && appJs.indexOf("reconcileAccess") >= 0, "direct routes and already-open tabs must react to disabled modules or revoked access");
assert.ok(featureJs.indexOf("Features.load({ notify: false })") >= 0, "the feature screen must refresh state without recursively notifying the shell");
assert.strictEqual((appJs.match(/alkhayal:features-changed/g) || []).length, 1, "the shell must register exactly one feature-state listener");
assert.ok(/if\s*\(\s*removedActive\s*\|\|\s*!activeRoute\s*\)\s*\{[^}]*renderActive\(\);/.test(appJs), "feature and access changes must not recreate an unaffected active screen");
assert.ok(appJs.indexOf('!screen.navigationHidden && routeEnabled(screen)') >= 0, "global navigation search must exclude hidden, disabled, and unauthorized routes");
assert.ok(registry.screens.filter(function (screen) { return screen.featureKey; }).length >= 20, "optional module routes must carry stable feature metadata");
assert.ok(modulesCss.indexOf(".feature-grid") >= 0 && modulesCss.indexOf("@media (max-width: 760px)") >= 0, "feature settings must support compact token-driven layouts");
assert.ok(routes.indexOf("users-list") >= 0 && indexHtml.indexOf("user-management.js") >= 0 && indexHtml.indexOf("access-state.js") >= 0, "real user management and access state must be registered");
assert.ok(registry.screens.filter(function(screen){return screen.administratorOnly;}).every(function(screen){return screen.module === "وسائل الدفع" || screen.module === "الإعدادات" || screen.module === "صلاحيات المستخدم" || ["employees-list","employee-add","employee-payroll","employee-advances","attendance","attendance-history"].includes(screen.route);}), "administrator-only metadata must be explicit in the canonical registry");
assert.ok(userManagementJs.indexOf('"/api/users"') >= 0 && userManagementJs.indexOf("/screen-permissions") >= 0 && userManagementJs.indexOf("/reset-password") >= 0, "user management must use real account APIs");
assert.ok(userManagementJs.indexOf("data-role-filter") >= 0 && userManagementJs.indexOf("data-status-filter") >= 0 && userManagementJs.indexOf("data-permission-search") >= 0, "user list and permission editor must provide search and filters");
assert.ok(userManagementJs.indexOf("تغييرات غير محفوظة") >= 0 && userManagementJs.indexOf("data-select-all") >= 0 && userManagementJs.indexOf("data-clear-all") >= 0, "permission editor must warn and support bulk selection");
assert.ok(dashboardJs.indexOf("context.canNavigate") >= 0 && appJs.indexOf("!routeEnabled(screen)") >= 0, "dashboard shortcuts and direct navigation must enforce effective access");
assert.ok(dashboardJs.indexOf("/api/reports/sales") < 0 && dashboardJs.indexOf("/api/alerts") < 0 && dashboardJs.indexOf("dashboard-kpi") < 0, "the home landing page must not request or render dashboard statistics");
assert.ok(dashboardJs.indexOf("الوردية الحالية") < 0 && dashboardJs.indexOf("/api/workforce/shifts/current") < 0 && dashboardJs.indexOf("data-shift-elapsed") < 0, "dashboard must not render or load the current shift card");
assert.ok(appJs.indexOf("screen.implemented === true")>=0&&appJs.indexOf('renderers["under-construction"]')>=0&&renderersJs.indexOf("هذه الشاشة قيد الإنشاء")>=0,"implemented route status must come from the canonical registry and unfinished routes must use the uniform construction state");
assert.ok(featureJs.indexOf("muted ltr")<0,"feature cards must not expose internal feature keys to staff");
assert.strictEqual((appJs.match(/alkhayal:access-changed/g) || []).length, 1, "the shell must register exactly one access-state listener");
assert.ok(indexHtml.indexOf("settings-hub.js")>=0&&settingsHubJs.indexOf("settingsGroup")>=0,"the Settings hub must load and derive entries from registry metadata");
assert.ok(indexHtml.indexOf("general-settings.js")>=0&&indexHtml.indexOf("settings-state.js")>=0,"real settings UI and shared formatter state must load in the shell");
assert.ok(settingsStateJs.indexOf('minimumFractionDigits: 2, maximumFractionDigits: 2')>=0,"money formatting must remain fixed at two decimal digits");
assert.ok(generalSettingsJs.indexOf("options:['2']")>=0,"general settings must not offer non-piastre display precision");
var realSettings={"settings-general":"general-settings","settings-company":"company-settings","settings-appearance":"appearance-settings","settings-backup":"database-settings"};
Object.keys(realSettings).forEach(function(route){var screen=registry.screens.find(function(item){return item.route===route;});assert.strictEqual(screen.renderer,realSettings[route],route+" must use a real renderer");assert.ok(screen.settingsGroup,"real settings routes must remain in the registry-driven Settings hub");});
assert.ok(generalSettingsJs.indexOf("/api/settings/company")>=0&&generalSettingsJs.indexOf("/api/settings/general")>=0&&backupSettingsJs.indexOf("/api/settings/database-info")>=0,"settings screens must use the authenticated API");
assert.ok(indexHtml.includes("backup-settings.js") && backupSettingsJs.includes("/api/settings/backups"), "backup Settings must load its authenticated API renderer");
assert.ok(registry.screens.find(function (screen) { return screen.route === "settings-backup"; }).administratorOnly, "backup Settings must be administrator-only");
assert.ok(backupSettingsJs.includes('accept=".alkbackup"') && backupSettingsJs.includes("UI.modal") &&
  backupSettingsJs.indexOf('request("/inspect"') < backupSettingsJs.indexOf('request("/restore"') &&
  backupSettingsJs.includes("confirmation: manifest.confirmation") && backupSettingsJs.includes("Api.clearSession()"),
  "restore must use the file picker, inspect before explicit confirmation, and clear client authentication");
assert.ok(generalSettingsJs.indexOf("إلغاء التغييرات")>=0&&generalSettingsJs.indexOf("context.isActive()")>=0,"settings forms must support cancel and stale-response protection");
assert.ok(generalSettingsJs.indexOf("context.state.companyDraft")>=0&&generalSettingsJs.indexOf("context.state.generalDraft")>=0,"unsaved settings drafts must survive workspace tab switches");
assert.ok(generalSettingsJs.indexOf("AlKhayalTheme.set")>=0&&generalSettingsJs.indexOf("light")>=0&&generalSettingsJs.indexOf("dark")>=0&&generalSettingsJs.indexOf("system")>=0,"appearance must reuse all three existing theme modes");
assert.ok(indexHtml.indexOf("catalog.js")>=0&&catalogJs.indexOf("/api/catalog/products")>=0,"the real catalog module must load and use the catalog API");
assert.strictEqual(registry.screens.find(function(screen){return screen.route==="products-list";}).renderer,"catalog-products","product list must use the real catalog renderer");
assert.strictEqual(registry.screens.find(function(screen){return screen.route==="product-add";}).renderer,"catalog-product-form","product creation must use the real catalog renderer");
assert.strictEqual(registry.screens.find(function(screen){return screen.route==="product-edit";}).renderer,"catalog-product-form","product editing must use the real catalog renderer");
assert.ok(catalogJs.indexOf('R["warehouse-directory"]')<0&&inventoryJs.indexOf('R["warehouse-directory"]=directory')>=0,"Warehouse Directory must have one real renderer");
assert.ok(catalogJs.indexOf("/api/catalog/attributes")>=0&&catalogJs.indexOf("/api/catalog/barcodes/lookup")>=0,"Task 2.2 catalog configuration endpoints must be wired");
assert.ok(catalogJs.indexOf("data-capability")>=0&&catalogJs.indexOf("data-unit-num")>=0&&catalogJs.indexOf("data-variant-id")>=0,"product detail must expose capabilities, exact unit conversions, and variants");
assert.ok(catalogJs.indexOf('data-unit-purchase-price')>=0&&catalogJs.indexOf('purchasePrice:row.querySelector("[data-unit-purchase-price]")')>=0,"additional units must retain editable purchase and selling prices");
assert.ok(catalogJs.indexOf("data-capability-toggle")>=0&&catalogJs.indexOf("catalog-capability-switch")>=0,"product capabilities must use accessible Arabic toggle switches");
assert.ok(catalogJs.indexOf('var capabilityOrder=["expiry-dates"]')>=0&&catalogJs.indexOf('isGloballyEnabled:true')>=0&&catalogJs.indexOf("item.isAssigned?' checked'")>=0,"expiry configuration must always be available while saved assignments remain selected");
assert.ok(!catalogJs.includes('data-free-values')&&!catalogJs.includes('sizeValues')&&!catalogJs.includes('colorValues'),"retired product controls and summary fields must be absent");
assert.ok(catalogJs.indexOf('unitRows=[{id:base?base.id:null,unitId:baseId,numerator:1,denominator:1,isBase:true')>=0,"the selected base unit must produce a canonical 1/1 mapping without enabling multiple units");
assert.ok(catalogJs.indexOf('data-generate-barcode')>=0&&catalogJs.indexOf('إنشاء باركود')>=0&&catalogJs.indexOf('barcodeChecksum')>=0&&catalogJs.indexOf('duplicate_barcode')<0,"barcode input must be always available with an Arabic generation control while uniqueness stays server-authoritative");
assert.ok(catalogJs.indexOf('state.drafts')>=0&&catalogJs.indexOf('restoreProductDraft')>=0&&catalogJs.indexOf('catalog-form-actions')>=0,"product drafts and one accessible save footer must survive tab renders");
assert.ok(catalogJs.indexOf('var capabilityOrder=["expiry-dates"]')>=0&&catalogJs.indexOf('data-capability-toggle="batch-tracking"')<0&&catalogJs.indexOf('data-capability-toggle="serial-numbers"')<0&&catalogJs.indexOf('data-capability-toggle="imei-tracking"')<0&&catalogJs.indexOf('catalogType')<0,"new-product UI must expose only expiry without a product-type or legacy tracking control");
assert.ok(catalogJs.indexOf('record.capabilities||[]')>=0&&catalogJs.indexOf('filter(function(unit){return !unit.isBase;})')>=0,"edits must carry forward hidden legacy capabilities and non-base units instead of silently overwriting them");
assert.ok(featureJs.indexOf('section("خصائص المنتجات"')>=0&&featureJs.indexOf('snapshot.capabilities.filter')>=0,"supported product properties must remain configurable in feature management");
assert.ok(modulesCss.indexOf(".catalog-switch-track")>=0&&modulesCss.indexOf("focus-within")>=0,"catalog switches must have visible keyboard focus and theme-aware styling");
assert.ok(catalogJs.indexOf("context.isActive()")>=0&&catalogJs.indexOf("data-catalog-retry")>=0,"catalog renderers must protect stale responses and expose retry states");
assert.ok(catalogJs.indexOf("سعر الشراء")>=0&&catalogJs.indexOf("purchasePrice")>=0&&catalogJs.indexOf("ليست متوسط تكلفة المخزون")>=0,"product forms must capture the optional reference purchase cost honestly");
assert.ok(catalogJs.indexOf("data-add-category-inline")>=0&&catalogJs.indexOf("categories.push(saved)")>=0&&catalogJs.indexOf("select.value=String(saved.id)")>=0,"inline category creation must preserve the mounted draft, refresh options, and select the new category");
assert.ok(catalogJs.indexOf("data-manage-categories")>=0&&catalogJs.indexOf('context.navigate("product-categories")')>=0&&catalogJs.indexOf('context.navigate("unit-add")')>=0,"catalog must expose category and unit management contextually");
assert.ok(catalogJs.indexOf("compactCategories")>=0&&catalogJs.indexOf("compactUnits")>=0&&catalogJs.indexOf('R["catalog-categories"]=compactCategories')>=0&&catalogJs.indexOf('R["catalog-units"]=compactUnits')>=0,"category and unit routes must use table-first renderers");
assert.ok(catalogJs.indexOf("catalogManagementModal")>=0&&catalogJs.indexOf("+ إضافة تصنيف")>=0&&catalogJs.indexOf("+ إضافة وحدة قياس")>=0,"catalog management must expose compact modal add actions");
assert.ok(catalogJs.indexOf("جارٍ الحفظ")>=0&&catalogJs.indexOf("تغييرات غير محفوظة")>=0&&catalogJs.indexOf("data-toggle-management")>=0,"management modals must validate, prevent duplicate saves, and protect unsaved edits");
assert.ok(settingsHubJs.indexOf("setSelectionRange")<0&&settingsHubJs.indexOf(".focus()")<0,"settings hub search must preserve its input node naturally without a programmatic focus restore");
assert.ok(settingsHubJs.includes("data-settings-results") && settingsHubJs.includes("mountSettingsSearch") &&
  settingsHubJs.includes("data-settings-search-panel"), "Settings must retain its cards and mount the shared search panel");
assert.ok(catalogJs.indexOf("products(screen,container,context);},220")<0&&catalogJs.indexOf("render();},180")<0,"catalog searches must not remount the whole screen from the search handler");
assert.ok(catalogJs.indexOf('id="catalogResults"')>=0&&catalogJs.indexOf('id="categoryManagementResults"')>=0&&catalogJs.indexOf('id="unitManagementResults"')>=0,"catalog list searches must update a persistent in-place results region only");
assert.ok(uiJs.indexOf("beforeClose")>=0&&uiJs.indexOf("focusSelector")>=0&&uiJs.indexOf("event.key === \"Escape\"")>=0,"shared modal behavior must support close guards, initial focus, Escape, and focus trapping");
assert.ok(uiJs.indexOf("function loadingModal")>=0&&uiJs.indexOf('role="alertdialog"')>=0&&uiJs.indexOf('aria-busy="true"')>=0&&uiJs.indexOf('class="loading-modal')>=0,"shared UI must provide an accessible blocking loading modal");
assert.ok(componentsCss.indexOf(".loading-modal")>=0&&componentsCss.indexOf(".loading-spinner")>=0&&componentsCss.indexOf("@keyframes loading-spin")>=0,"shared loading modal must use theme tokens and a lightweight spinner");
var inventoryModule=registry.modules.find(function(module){return module.id==="inventory";});
var customerModule=registry.modules.find(function(module){return module.id==="customers";});
assert.deepStrictEqual(Array.from(customerModule.items),["customers-list","customer-statement"],"customer sidebar must expose its daily workflows without duplicating reports");
["customers-list","customer-statement"].forEach(function(route){assert.ok(customerModule.items.indexOf(route)>=0,"customer sidebar must keep "+route);});
["customer-add","customer-edit","customer-subscriptions"].forEach(function(route){assert.ok(customerModule.items.indexOf(route)<0,"customer sidebar must not expose the standalone "+route+" entry");assert.ok(sidebarRoutes.indexOf(route)<0,"removed customer entry must not stay in any sidebar group: "+route);});
["customer-add","customer-edit","customer-subscriptions"].forEach(function(route){var screen=registry.screens.find(function(item){return item.route===route;});assert.ok(screen,"customer route must remain registered: "+route);assert.ok(screen.navigationHidden,"customer action route must stay out of navigation and search: "+route);});
["customers-export","supplier-reports","supplier-paid-report","invoice-lookup","sales-reports","sales-confirm"].forEach(function(route){var screen=registry.screens.find(function(item){return item.route===route;});assert.ok(screen&&screen.navigationHidden,"requested sidebar removal must preserve a hidden compatibility route: "+route);assert.ok(sidebarRoutes.indexOf(route)<0,"requested route must not remain in a sidebar group: "+route);});
assert.strictEqual(registry.screens.find(function(screen){return screen.route==="customer-add";}).renderer,"entity-form","customer creation must use the real customer form");
assert.strictEqual(registry.screens.find(function(screen){return screen.route==="customer-edit";}).renderer,"entity-form","customer editing must use the real customer form");
assert.ok(registry.screens.find(function(screen){return screen.route==="customer-add";}).implemented&&registry.screens.find(function(screen){return screen.route==="customer-edit";}).implemented,"customer creation and editing routes must render their real forms");
assert.strictEqual(registry.screens.find(function(screen){return screen.route==="customers-list";}).renderer,"entity-list","customer management must use the shared entity list");
assert.ok(renderersJs.indexOf("data-list-add")>=0&&renderersJs.indexOf('data-row-action="edit"')>=0,"customer management must expose Arabic add and edit actions");
assert.ok(renderersJs.indexOf('info.singular.replace(/^ال/, "")')>=0,"the customer management add action must name the Arabic entity");
assert.ok(renderersJs.indexOf('method: isEdit ? "PUT" : "POST"')>=0&&renderersJs.indexOf('"/api/customers"')>=0,"customer creation and editing must persist through the customer API");
assert.deepStrictEqual(Array.from(inventoryModule.items),["products-list","warehouses-list"],"inventory sidebar must contain only its daily operational directories");
assert.strictEqual(registry.screens.find(function(screen){return screen.route==="warehouses-list";}).renderer,"warehouse-directory","Warehouse Directory must resolve to its real renderer");
assert.ok(inventoryJs.indexOf('/api/inventory/warehouses')>=0&&inventoryJs.indexOf('/api/inventory/balance-page')>=0&&inventoryJs.indexOf('data-select-warehouse')>=0,"Warehouse Directory must query persisted warehouses and balances with searchable selection");
assert.ok(inventoryJs.indexOf('/api/inventory/balance-page')>=0&&inventoryJs.indexOf('inventoryProductSearch')>=0&&inventoryJs.indexOf('data-balance-page')>=0,"warehouse balances must use server-side product search and pagination");
assert.ok(inventoryJs.indexOf('receiptModal')>=0&&inventoryJs.indexOf('data-operation")==="manual_receipt"')>=0,"the add-stock action must open the simplified receipt modal");
assert.ok(inventoryJs.indexOf('data-save-receipt')>=0&&inventoryJs.indexOf(">حفظ<")>=0&&inventoryJs.indexOf('جارٍ الحفظ')>=0,"the simplified receipt must save directly with a disabled-while-saving button");
var receiptSource=inventoryJs.slice(inventoryJs.indexOf('    function receiptModal('));
assert.ok(receiptSource.indexOf('سعر الشراء الجديد')>=0&&receiptSource.indexOf('سعر البيع الجديد')>=0,"receipt must request explicit acquisition and selling prices");
assert.ok(receiptSource.indexOf('unitCost:plus?cost:null')>=0&&receiptSource.indexOf('salePrice:receipt?sale:null')>=0,"receipt must post selected-unit prices without a reference-cost fallback");
assert.ok(inventoryJs.indexOf('name="reason"')<0&&inventoryJs.indexOf('countReason')<0&&inventoryJs.indexOf('setTimeout')<0,"warehouse forms must not contain reason fields or timer-based hiding");
assert.ok(receiptSource.indexOf('data-review')<0&&receiptSource.indexOf('variantField.hidden=state.variants.length<=1')>=0,"receipt must save directly and show variants only when needed");
assert.ok(inventoryJs.indexOf("<th>المخزن</th>")>=0&&inventoryJs.indexOf("<th>ملاحظات</th>")>=0&&inventoryJs.indexOf("x.warehouseName")>=0,"movement history must show the warehouse and optional notes in Arabic");
var hiddenInventoryActions=["product-add","product-edit","product-quick-add","warehouse-add","stock-adjustment","warehouse-transfer","stock-movements","low-stock","manufacturing"];
hiddenInventoryActions.forEach(function(route){var screen=registry.screens.find(function(item){return item.route===route;});assert.ok(screen&&screen.navigationHidden,"action-only inventory route must remain registered but hidden from top-level discovery: "+route);assert.strictEqual(typeof screen.renderer,"string");});
assert.ok(settingsStateJs.indexOf("loadPromise")>=0&&settingsStateJs.indexOf("/api/settings/general")>=0,"settings reads must be deduplicated");
assert.ok(!registry.modules.some(function(module){return module.id==="permissions"||module.id==="printing";}),"standalone permissions and configuration-only printing groups must be removed from the sidebar");
assert.ok(!sidebarRoutes.some(function(route){return route==="users-list"||route==="user-permissions"||route==="sales-permissions"||route==="settings-permissions";}),"user administration routes must not appear directly in sidebar groups");
assert.strictEqual(registry.screens.find(function(s){return s.route==="shift-approval";}).redirectTo,"shifts-list","old approval route resolves to the persisted shift lifecycle");
assert.ok(!sidebarRoutes.includes("shift-approval"),"no unsupported approval operation in navigation");
assert.deepStrictEqual(registry.modules.map(function(module){return module.id;}),["home","sales","inventory","purchases","suppliers","customers","payment-methods","treasury","expenses","returns","alerts","employees","reports","installments","maintenance","branches"],"sidebar modules must follow the daily retail workflow before secondary modules");
var salesList=registry.screens.find(function(screen){return screen.route==="sales-list";});
assert.ok(salesList.navigationHidden&&salesList.redirectTo==="sales-management"&&salesList.renderer==="legacy-redirect","legacy sales-list bookmark must redirect to the unified sales invoice screen and stay out of discovery");
var returnDetails=registry.screens.find(function(screen){return screen.route==="return-details";});
assert.ok(returnDetails.navigationHidden&&returnDetails.permissionRoute==="returns-overview","return details must stay contextual while inheriting the management permission");
assert.ok(!sidebarRoutes.includes("sales-list")&&!sidebarRoutes.includes("return-details"),"duplicate sale history and contextual return details must not be primary sidebar destinations");
var usersScreen=registry.screens.find(function(screen){return screen.route==="users-list";});
assert.strictEqual(usersScreen.renderer,"user-management","User Management must resolve to its real renderer");
assert.strictEqual(usersScreen.settingsGroup,"users-security","User Management must be located in the Settings security group");
assert.ok(usersScreen.administratorOnly,"User Management must remain administrator-only");
var legacyPermissionRoutes=["user-permissions","sales-permissions","settings-permissions"];
legacyPermissionRoutes.forEach(function(route){var screen=registry.screens.find(function(item){return item.route===route;});assert.strictEqual(screen.redirectTo,"users-list","legacy permission routes must redirect to User Management");assert.ok(screen.navigationHidden,"legacy permission routes must stay out of navigation and search");});
var permissionEntries=registry.screens.filter(function(screen){return !screen.administratorOnly&&(screen.permissionControllable===true||(screen.implemented===true&&!screen.redirectTo&&!screen.permissionRoute&&screen.renderer!=="under-construction"));});
var permissionIds=permissionEntries.map(function(screen){return screen.route;});
assert.ok(registry.screens.every(function(screen){return typeof screen.implemented==="boolean";}),"every screen must explicitly declare whether its current renderer is implemented");
assert.strictEqual(new Set(permissionIds).size,permissionIds.length,"permission entry IDs must be unique");
assert.ok(permissionEntries.every(function(screen){return screen.title&&screen.module&&screen.renderer&&allowedRenderers.has(screen.renderer);}),"every cashier permission must use an implemented registry route and its active Arabic label/module");
registry.screens.filter(function(screen){return screen.permissionRoute;}).forEach(function(screen){var canonical=registry.screens.find(function(item){return item.route===screen.permissionRoute;});assert.ok(canonical&&permissionIds.includes(canonical.route),"permission aliases must target one canonical permission entry: "+screen.route);assert.ok(!permissionIds.includes(screen.route),"permission aliases must not produce duplicate checkboxes: "+screen.route);});
assert.ok(permissionIds.includes("reorder-alert")&&permissionIds.includes("low-stock-alert"),"alert-specific permission IDs must remain independent because the API enforces those separate scopes");
assert.ok(!permissionEntries.some(function(screen){return screen.renderer==="under-construction"||(screen.redirectTo&&screen.permissionControllable!==true);}),"ordinary redirects and unimplemented routes must not appear in cashier permissions");
assert.ok(accessStateJs.indexOf("screen.permissionRoute || screen.route")>=0&&appJs.indexOf("!routeEnabled(screen)")>=0&&/tabs\s*=\s*tabs\.filter\s*\(\s*function\s*\(\s*tab\s*\)\s*\{\s*return\s+routeEnabled\s*\(\s*screensByRoute\s*\[\s*tab\.route\s*\]\s*\)\s*;\s*\}\s*\)/.test(appJs),"canonical permissions must gate direct routes and remove revoked workspace tabs");
assert.ok(appJs.indexOf("routeEnabled(screensByRoute[item.route])")>=0&&appJs.indexOf("routeEnabled(screen)")>=0&&settingsHubJs.indexOf("context.canNavigate(item.route)")>=0,"sidebar, quick access, global search and Settings links must use effective screen access");
assert.ok(registry.screens.filter(function(screen){return screen.administratorOnly;}).every(function(screen){return screen.module==="وسائل الدفع"||screen.module==="الموظفين"||screen.route==="settings-program"||screen.settingsGroup||screen.navigationHidden||["product-categories","unit-add"].includes(screen.route);}),"every administrative screen must be reachable from Settings, workforce, Product Directory, or an explicit hidden compatibility route");
var settingsEntries=registry.screens.filter(function(screen){return screen.settingsGroup&&!screen.navigationHidden;});
settingsEntries.forEach(function(screen){assert.ok(routes.indexOf(screen.route)>=0&&allowedRenderers.has(screen.renderer),"Settings entry must resolve to a registered renderer: "+screen.route);});
assert.ok(appJs.indexOf("typeof renderers[screen.renderer]")>=0,"startup must fail visibly when registry renderer code is missing");
assert.ok(mainFormCs.indexOf("Network.clearBrowserCache")>=0&&serverHostCs.indexOf("no-store, no-cache, must-revalidate")>=0,"desktop startup and static serving must prevent shell/registry cache version skew");
assert.ok(appJs.indexOf("resolveRoute")>=0&&appJs.indexOf("screen.redirectTo")>=0,"legacy route compatibility must use centralized route resolution");
function verifyRuntimeRendererResolution(){var context={window:{AlKhayalUtils:{},AlKhayalUI:{},AlKhayalServices:{},AlKhayalCalculations:{},AlKhayalApi:{},AlKhayalFeatures:{},AlKhayalSettings:{},AlKhayalTheme:{}}};context.window.window=context.window;[renderersJs,fs.readFileSync(path.join(root,"frontend/js/modules/report-ui.js"),"utf8"),fs.readFileSync(path.join(root,"frontend/js/modules/operational-reports.js"),"utf8"),fs.readFileSync(path.join(root,"frontend/js/modules/reports-center.js"),"utf8"),fs.readFileSync(path.join(root,"frontend/js/modules/transaction-reports.js"),"utf8"),fs.readFileSync(path.join(root,"frontend/js/modules/expense-report.js"),"utf8"),fs.readFileSync(path.join(root,"frontend/js/modules/net-profit-report.js"),"utf8"),fs.readFileSync(path.join(root,"frontend/js/modules/stock-reports.js"),"utf8"),fs.readFileSync(path.join(root,"frontend/js/modules/sales-report.js"),"utf8"),fs.readFileSync(path.join(root,"frontend/js/modules/payment-methods.js"),"utf8"),catalogJs,inventoryJs,treasuryJs,expensesJs,returnsJs,featureJs,settingsHubJs,generalSettingsJs,backupSettingsJs,userManagementJs,dashboardJs,salesJs,purchasingJs,workforceJs,printingJs,labelsJs].forEach(function(source,index){vm.runInNewContext(source,context,{filename:"renderer-"+index+".js"});});registry.screens.forEach(function(screen){assert.strictEqual(typeof context.window.AlKhayalRenderers[screen.renderer],"function","route must resolve to executable renderer: "+screen.route+" -> "+screen.renderer);});}
verifyRuntimeRendererResolution();
function verifyRepeatedAdministrationRendering(){var requests=0;var utils={normalize:function(value){return String(value||"").toLowerCase();},escape:function(value){return String(value==null?"":value);}};var ui={pageHeader:function(){return "";},field:function(){return "";}};var context={window:{AlKhayalUtils:utils,AlKhayalUI:ui,AlKhayalFeatures:{routeEnabled:function(){return true;}},AlKhayalApi:{request:function(){requests+=1;return new Promise(function(){});}},AlKhayalRenderers:{},confirm:function(){return true;}}};context.window.window=context.window;vm.runInNewContext(settingsHubJs,context,{filename:"settings-hub-repeat.js"});vm.runInNewContext(userManagementJs,context,{filename:"user-management-repeat.js"});function container(){var input={focus:function(){},setSelectionRange:function(){}};return{innerHTML:"",querySelector:function(selector){return selector==="[data-settings-search]"?input:null;},querySelectorAll:function(){return[];}};}var renderContext={registry:registry,user:{role:"administrator",id:"admin"},canNavigate:function(){return true;},navigate:function(){},isActive:function(){return true;}};var settings=registry.screens.find(function(screen){return screen.route==="settings-program";}),users=registry.screens.find(function(screen){return screen.route==="users-list";});for(var index=0;index<5;index+=1){context.window.AlKhayalRenderers["settings-hub"](settings,container(),renderContext);context.window.AlKhayalRenderers["user-management"](users,container(),renderContext);}assert.strictEqual(requests,5,"five User Management reopen cycles must issue one bounded list request each without recursive loads");}
verifyRepeatedAdministrationRendering();
function verifySalesPrecisionAndVariantLabels(){
  var source=salesJs.replace('  R["sales-invoices"]=sales;', '  global.__salesTest={scaled:scaled,taxTotals:taxTotals,variantLabel:variantLabel,invoiceDiscount:invoiceDiscount,moneyInput:moneyInput};\n  R["sales-invoices"]=sales;');
  assert.notStrictEqual(source,salesJs,"sales test hook must attach to the registered module");
  var context={window:{AlKhayalUtils:{},AlKhayalUI:{},AlKhayalApi:{},AlKhayalSettings:{},AlKhayalRenderers:{}},Date:Date,BigInt:BigInt};context.window.window=context.window;
  vm.runInNewContext(source,context,{filename:"sales-financial-regressions.js"});
  var helpers=context.window.__salesTest;
  assert.strictEqual(helpers.scaled("-0.50",4),-5000n,"negative sub-unit amounts must retain their sign");
  var included=helpers.taxTotals(1150000n,0n,{taxEnabled:true,defaultTaxRate:15,pricesIncludeTax:true});
  assert.strictEqual(included.tax,150000n,"tax included in EGP 115 at 15% must be EGP 15");
  assert.strictEqual(included.total,1150000n,"tax-inclusive total must remain the entered total");
  assert.strictEqual(helpers.invoiceDiscount(1000050n,"percent","12.50"),125000n,"percentage discount must round to a monetary cent");
  assert.strictEqual(helpers.moneyInput(helpers.invoiceDiscount(1000050n,"percent","12.50")),"12.50","percentage discount must persist as the exact fixed amount accepted by sales posting");
  assert.throws(function(){helpers.invoiceDiscount(1000000n,"percent","100.01");},/0 و100/,"percentage over 100 must be rejected");
  assert.throws(function(){helpers.invoiceDiscount(1000000n,"fixed","101.00");},/أكبر/,"fixed discount cannot exceed subtotal");
  var product={name:"قميص",attributes:[{values:[{valueId:11,value:"M"},{valueId:12,value:"L"}]}]};
  assert.strictEqual(helpers.variantLabel(product,{name:"قميص",attributeValueIds:[12]}),"L","generated variants must be distinguishable by their assigned values");
}
verifySalesPrecisionAndVariantLabels();
var responsiveViewports = [[1920, 1080], [1600, 900], [1366, 768], [1280, 720], [1024, 768], [900, 600]];
assert.strictEqual(responsiveViewports.length, 6, "responsive viewport matrix is explicit");

async function verifyFeatureStateLifecycle() {
  var resolveRequest, requestCount = 0, notifications = 0;
  var context = {
    CustomEvent: function (name, options) { this.type = name; this.detail = options.detail; },
    window: {
      AlKhayalApi: { request: function () { requestCount += 1; return new Promise(function (resolve) { resolveRequest = resolve; }); } },
      dispatchEvent: function () { notifications += 1; }
    }
  };
  vm.runInNewContext(featureStateJs, context, { filename: "feature-state.js" });
  var initial = { modules: [{ key: "installments", isEnabled: false }], capabilities: [], generatedUtc: "2026-10-21T10:00:00Z" };
  var first = context.window.AlKhayalFeatures.load({ notify: false });
  var second = context.window.AlKhayalFeatures.load({ notify: false });
  var third = context.window.AlKhayalFeatures.load({ notify: false });
  assert.strictEqual(requestCount, 1, "concurrent feature loads must share one API request");
  assert.strictEqual(first, second); assert.strictEqual(second, third);
  resolveRequest(initial); await Promise.all([first, second, third]);
  assert.strictEqual(notifications, 0, "screen-owned refreshes must not notify the shell");
  context.window.AlKhayalFeatures.setSnapshot({ modules: initial.modules, capabilities: [], generatedUtc: "2026-10-21T10:01:00Z" });
  assert.strictEqual(notifications, 0, "timestamp-only snapshots must not trigger navigation reconciliation");
  context.window.AlKhayalFeatures.setSnapshot({ modules: [{ key: "installments", isEnabled: true }], capabilities: [] });
  assert.strictEqual(notifications, 1, "an effective feature change must notify the shell exactly once");
}

async function verifyAccessStateLifecycle() {
  var resolveRequest, requestCount=0, notifications=0;
  var context={CustomEvent:function(name,options){this.type=name;this.detail=options.detail;},window:{AlKhayalApi:{request:function(){requestCount+=1;return new Promise(function(resolve){resolveRequest=resolve;});}},dispatchEvent:function(){notifications+=1;}}};
  vm.runInNewContext(accessStateJs,context,{filename:"access-state.js"});
  var first=context.window.AlKhayalAccess.load({notify:false}),second=context.window.AlKhayalAccess.load({notify:false});
  assert.strictEqual(requestCount,1,"concurrent access loads must share one API request");assert.strictEqual(first,second);
  resolveRequest({role:"cashier",screenIds:["dashboard"],canEditSalePrices:false,canViewPosStockQuantities:false});await Promise.all([first,second]);assert.strictEqual(notifications,0,"screen-owned access loads must not notify the shell");assert.strictEqual(context.window.AlKhayalAccess.current().canEditSalePrices,false,"cashiers without an explicit grant default to read-only sale prices");assert.strictEqual(context.window.AlKhayalAccess.current().canViewPosStockQuantities,false,"cashiers without a stock-visibility grant default to hidden quantities");
  context.window.AlKhayalAccess.setSnapshot({role:"cashier",screenIds:["dashboard"]});assert.strictEqual(notifications,0,"unchanged access must not trigger reconciliation");
  context.window.AlKhayalAccess.setSnapshot({role:"cashier",screenIds:["dashboard","pos"]});assert.strictEqual(notifications,1,"an access change must notify exactly once");
  var detail=registry.screens.find(function(screen){return screen.route==="return-details";});
  assert.strictEqual(context.window.AlKhayalAccess.routeEnabled(detail),false,"returns details must require the management permission");
  context.window.AlKhayalAccess.setSnapshot({role:"cashier",screenIds:["returns-overview"]});
  assert.strictEqual(context.window.AlKhayalAccess.routeEnabled(detail),true,"contextual returns details inherit management access");
  context.window.AlKhayalAccess.setSnapshot({role:"administrator",screenIds:[],canEditSalePrices:false,canViewPosStockQuantities:false});assert.strictEqual(context.window.AlKhayalAccess.current().canEditSalePrices,true,"administrators retain manual invoice price access");assert.strictEqual(context.window.AlKhayalAccess.current().canViewPosStockQuantities,true,"administrators retain POS stock visibility");
}

async function verifySettingsStateLifecycle() {
  var resolveRequest,requestCount=0,notifications=0,storage={};
  var context={Intl:Intl,Date:Date,JSON:JSON,CustomEvent:function(name,options){this.type=name;this.detail=options.detail;},window:{AlKhayalUtils:{},AlKhayalApi:{request:function(){requestCount+=1;return new Promise(function(resolve){resolveRequest=resolve;});}},dispatchEvent:function(){notifications+=1;}}};
  vm.runInNewContext(settingsStateJs,context,{filename:"settings-state.js"});
  var first=context.window.AlKhayalSettings.load({notify:false}),second=context.window.AlKhayalSettings.load({notify:false});assert.strictEqual(requestCount,1,"concurrent settings loads must share one API request");assert.strictEqual(first,second);
  resolveRequest({dateFormat:"dd/MM/yyyy",timeFormat:"HH:mm",useGroupSeparators:true,currencySymbol:"ج.م",decimalPrecision:2});await Promise.all([first,second]);assert.strictEqual(notifications,0,"initial settings formatting must not recursively notify the shell");
  assert.ok(context.window.AlKhayalUtils.money(10).indexOf("ج.م")>=0,"shared money formatter must consume the persisted symbol");
  assert.ok(context.window.AlKhayalUtils.dateTime(new Date(2026,9,21,13,5)).indexOf("21/10/2026")>=0,"shared date-time formatter must consume persisted formats");
  context.window.AlKhayalSettings.setSnapshot({dateFormat:"yyyy-MM-dd",timeFormat:"HH:mm",useGroupSeparators:true,currencySymbol:"$",decimalPrecision:2});assert.strictEqual(notifications,1,"a real settings change may notify exactly once");
}

var totals = calculations.invoice([{ quantity: 2, price: 19.95 }, { quantity: 3, price: 10 }], 4.90, 14);
assert.deepStrictEqual(totals, { subtotal: 69.9, discount: 4.9, tax: 9.1, total: 74.1 });
var schedule = calculations.installments(1000, 100, 3, "2026-11-01");
assert.strictEqual(schedule.length, 3); assert.strictEqual(schedule.reduce(function (sum, item) { return sum + item.amount; }, 0), 900); assert.strictEqual(schedule[2].dueDate, "2027-01-01");
var installmentRoutes=registry.screens.filter(function(screen){return screen.route==="installment-system"||screen.route==="installment-payment";});
assert.strictEqual(installmentRoutes.length,2,"installments navigation must expose exactly sale and payment screens");
assert.ok(installmentRoutes.some(function(screen){return screen.title==="البيع بالتقسيط"&&screen.mode==="create";})&&installmentRoutes.some(function(screen){return screen.title==="سداد القسط"&&screen.mode==="payment";}),"installment screens must have the requested Arabic names and modes");
assert.ok(registry.screens.find(function(screen){return screen.route==="installment-system";}).implemented&&registry.screens.find(function(screen){return screen.route==="installment-payment";}).implemented,"both installment routes must select their real renderer instead of the construction placeholder");
assert.ok(renderersJs.indexOf('id="installmentEvery"')>=0&&renderersJs.indexOf('id="installmentCustomerFilter"')>=0&&renderersJs.indexOf('data-pay=')>=0,"installment sale must calculate interval and payment must select a customer and settle individual rows");
assert.ok(renderersJs.indexOf('container.ownerDocument.createElement("button")')>=0,"installment settlement action must be materialized as a real DOM button after safe table text escaping");
assert.ok(renderersJs.indexOf('/api/sales/installments')>=0&&renderersJs.indexOf('/api/customers/"+encodeURIComponent(customerId)+"/installments')>=0&&renderersJs.indexOf('data-pay="\'+contract.customerId+\':\'+contract.id')>=0,"installment sale must use atomic sales API and payment must use persisted contracts");
assert.ok(renderersJs.indexOf('id="installmentProductSearch"')>=0&&renderersJs.indexOf('/api/inventory/products?search=')>=0&&renderersJs.indexOf('id="installmentUnit"')>=0&&renderersJs.indexOf('id="installmentQuantity"')>=0&&renderersJs.indexOf('id="installmentWarehouse"')>=0,"installment sale must search server products and select unit, quantity, and warehouse");
assert.ok(renderersJs.indexOf('id="installmentCustomerSearch"')>=0&&renderersJs.indexOf('/api/customers/search?query=')>=0&&renderersJs.indexOf('/statement").then(function(data)')>=0&&renderersJs.indexOf('data-open-installment-statement')>=0,"installment sale must search saved customers and show their persisted account statement");
assert.ok(renderersJs.indexOf('{ key: "balance", label: "الرصيد الحالي", money: true }')>=0&&renderersJs.indexOf('context.state.installmentCustomerSearch=button.textContent')>=0&&renderersJs.indexOf('context.state.installmentCustomerId=Number(this.value)')>=0,"customer directory must show current balance and installment selection/search must survive navigation redraws");
assert.ok(renderersJs.indexOf('context.state.installmentCustomerId=null;context.state.installmentCustomerSearch="";customerSelect.value="";customerSearch.value=""')>=0,"successful installment sale must clear the selected customer and search for the next operation");
assert.ok(renderersJs.indexOf('S.store.add("installments"')<0&&renderersJs.indexOf('تم نشر الفاتورة')>=0,"installment actions must use persisted atomic sale posting");
var maintenanceModule=registry.modules.find(function(module){return module.id==="maintenance";}), maintenanceScreens=registry.screens.filter(function(screen){return screen.route==="maintenance-intake"||screen.route==="maintenance-tracking";});
assert.deepStrictEqual(maintenanceModule.items,["maintenance-intake","maintenance-tracking"],"the Maintenance section must contain its workflows without duplicating its report");
assert.ok(maintenanceScreens.some(function(screen){return screen.title==="تسجيل جهاز للصيانة";})&&maintenanceScreens.some(function(screen){return screen.title==="قائمة الصيانة";}),"maintenance routes must use the requested screen names");
assert.ok(registry.screens.find(function(screen){return screen.route==="maintenance-intake";}).implemented&&registry.screens.find(function(screen){return screen.route==="maintenance-tracking";}).implemented,"maintenance routes must render their implementation instead of the construction placeholder");
assert.ok(renderersJs.indexOf('/api/maintenance/tickets')>=0&&renderersJs.indexOf('maintenanceCustomerFilter')>=0&&renderersJs.indexOf('maintenanceHasAdvance')>=0&&renderersJs.indexOf('تسليم للعميل')>=0&&renderersJs.indexOf('settlementType')>=0,"maintenance intake, customer filtering, advance collection and delivery settlement must use persisted maintenance APIs");
var sample = [{ name: "أحمد", amount: 2 }, { name: "محمود", amount: 1 }];
assert.strictEqual(calculations.filter(sample, "أح", ["name"]).length, 1); assert.strictEqual(calculations.sort(sample, "amount", "asc")[0].amount, 1);
function verifyCatalogSavedSummaryAndEditHydration(){
  var source=catalogJs.replace('  R["catalog-products"]=products;', '  global.__catalogTest={catalogSummaryBadges:catalogSummaryBadges,capabilityDetails:capabilityDetails};\n  R["catalog-products"]=products;');
  assert.notStrictEqual(source,catalogJs,"catalog test hook must attach to the registered module");
  var utils={escape:function(value){return String(value==null?"":value).replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/"/g,"&quot;");},number:function(value){return String(value);},money:function(value){return String(value);}};
  var context={window:{AlKhayalUtils:utils,AlKhayalUI:{badge:function(value){return "<span>"+value+"</span>";}},AlKhayalApi:{},AlKhayalRenderers:{}}};
  vm.runInNewContext(source,context,{filename:"catalog-summary-hydration.js"});
  var helpers=context.window.__catalogTest;
  assert.strictEqual(helpers.catalogSummaryBadges({sizeValues:[],colorValues:[],variantCount:1}),"","simple products must have no optional badges");
  assert.strictEqual(helpers.catalogSummaryBadges({sizeValues:[],colorValues:[],variantCount:1,hasBatchTracking:true}),"","internal batch history must not render a customer-facing badge");
  assert.ok(helpers.catalogSummaryBadges({sizeValues:[],colorValues:[],variantCount:1,hasExpiryTracking:true,hasBatchTracking:true}).indexOf("تشغيلات")<0,"expiry and its required batch must not create redundant badges");
  var badges=helpers.catalogSummaryBadges({sizeValues:["M","L","XL"],colorValues:["أسود","أبيض"],variantCount:6,hasExpiryTracking:true});
  assert.ok(badges.includes("صلاحية")&&!badges.includes("مقاسات")&&!badges.includes("ألوان"),"retired values must not render badges while expiry remains");
  var record={id:7,variantMode:true,attributes:[{attributeId:1,kind:"size",values:[{valueId:11,value:"M"},{valueId:12,value:"L"}]},{attributeId:2,kind:"color",values:[{valueId:21,value:"أسود"},{valueId:22,value:"أبيض"}]}],capabilities:[{key:"sizes",isAssigned:true},{key:"colors",isAssigned:true}],variants:[{id:101,isDefault:true,isActive:true,attributeValueIds:[11,21],sku:"SKU-1",barcodes:[{code:"BC-1",isRetired:false}]},{id:102,isDefault:false,isActive:true,attributeValueIds:[11,22],barcodes:[]},{id:103,isDefault:false,isActive:true,attributeValueIds:[12,21],barcodes:[]},{id:104,isDefault:false,isActive:true,attributeValueIds:[12,22],barcodes:[]}]};
  var snapshot={capabilities:[{key:"sizes",isEnabled:true},{key:"colors",isEnabled:true}]};
  var html=helpers.capabilityDetails(record,[],[],true,snapshot);
  assert.ok(html.indexOf('data-generate-barcode')>=0&&html.indexOf('إنشاء باركود')>=0&&html.indexOf('id="catalogSku"')>=0&&html.indexOf('value="SKU-1"')>=0&&html.indexOf('SKU / كود الصنف (PLU)')>=0,"the product form must expose the saved SKU/PLU used by scale barcodes alongside fixed barcode entry/generation");
  assert.ok(!html.includes("data-free-values")&&!html.includes('data-capability-toggle="sizes"')&&!html.includes('data-capability-toggle="colors"'),"legacy assignments cannot restore retired controls");
  assert.ok(!html.includes("data-free-values")&&!html.includes('data-capability-toggle="sizes"')&&!html.includes('data-capability-toggle="colors"'),"legacy assignments cannot restore retired controls");
  assert.ok(html.indexOf('data-capability-toggle="batch-tracking"')<0&&html.indexOf('data-capability-toggle="serial-numbers"')<0&&html.indexOf('data-capability-toggle="imei-tracking"')<0,"legacy tracking controls must never be presented in product creation/editing");
  assert.ok(html.indexOf('data-variant-id="101"')>=0&&html.indexOf('data-variant-id="104"')>=0&&html.indexOf('SKU-1')>=0&&html.indexOf('BC-1')>=0,"variant summary must show saved combinations and identifiers");
  assert.ok(html.indexOf('<td>11, 21</td>')<0,"variant summary must show value labels instead of raw IDs");
  var disabled=helpers.capabilityDetails(record,[],[],true,{capabilities:[{key:"sizes",isEnabled:false},{key:"colors",isEnabled:false}]});
  assert.ok(!disabled.includes("data-free-values"),"global state cannot restore retired controls");
}
verifyCatalogSavedSummaryAndEditHydration();
(function verifySupplierWiring(){
  assert.ok(renderersJs.indexOf('"/api/suppliers"')>=0&&renderersJs.indexOf('supplierStatement')>=0&&renderersJs.indexOf('supplierPayment')>=0,"supplier screens must use the persisted suppliers API");
  assert.ok(renderersJs.indexOf('data-pay-invoice')>=0&&renderersJs.indexOf('دفع آجل')>=0&&renderersJs.indexOf('/payments')>=0,"supplier invoices must expose a credit-payment button wired to the payments endpoint");
  assert.ok(renderersJs.indexOf('/invoices')>=0,"supplier account page must load invoices from the API");
  ["suppliers-list","supplier-credit","supplier-payment","supplier-balance"].forEach(function(route){var screen=registry.screens.find(function(item){return item.route===route;});assert.ok(screen&&screen.description.indexOf("تجريبي")<0,"supplier screen must not be described as a demo: "+route);});
}());
function verifyCustomerManagementCreateAndEdit(){
  function fakeElement(){var element={innerHTML:"",textContent:"",value:"",hidden:false,selectors:{},classList:{add:function(){},remove:function(){},toggle:function(){},contains:function(){return false;}},querySelector:function(selector){if(!element.selectors[selector])element.selectors[selector]=fakeElement();return element.selectors[selector];},querySelectorAll:function(){return[];},addEventListener:function(){},removeEventListener:function(){},focus:function(){},setAttribute:function(){},removeAttribute:function(){},appendChild:function(child){return child;},remove:function(){},click:function(){}};return element;}
  var modalLayer=fakeElement(),toastStack=fakeElement(),apiCalls=[];
  modalLayer.selectors["#recordForm"]=fakeElement();
  modalLayer.selectors["#recordForm"].elements=new Proxy({}, {get:function(target,key){return typeof key==="string"?Object.assign(fakeElement(),{value:"CUS-900"}):undefined;}});
  var apiStub={request:function(target,options){apiCalls.push({target:target,options:options||{}});if(target.startsWith("/api/customers/page?")){var rows=context.window.AlKhayalMockSeed.customers;return Promise.resolve({items:rows,page:1,pageSize:50,totalCount:rows.length});}return Promise.resolve({success:true,data:[]});}};
  var documentStub={activeElement:null,getElementById:function(id){if(id==="modalLayer")return modalLayer;if(id==="toastStack")return toastStack;return null;},createElement:function(){return fakeElement();},body:{appendChild:function(){}}};
  var context={window:{},console:console,document:documentStub,localStorage:{getItem:function(){return null;},setItem:function(){}},setTimeout:function(){return 0;},clearTimeout:function(){},AlKhayalApi:apiStub};
  context.window.window=context.window;
  function mirrorGlobals(){["AlKhayalUtils","AlKhayalCalculations","AlKhayalMockSeed","AlKhayalServices","AlKhayalUI","AlKhayalApi"].forEach(function(key){if(context[key]!==undefined)context.window[key]=context[key];});}
  ["frontend/js/core/utils.js","frontend/js/core/calculations.js","frontend/js/mocks/data.js","frontend/js/services/mock-services.js","frontend/js/components/ui.js"].forEach(function(relative){vm.runInNewContext(fs.readFileSync(path.join(root,relative),"utf8"),context,{filename:relative});mirrorGlobals();});
  vm.runInNewContext(renderersJs,context,{filename:"renderers.js"});
  var screen=registry.screens.find(function(item){return item.route==="customers-list";});
  var listPager=fakeElement(); var container={innerHTML:"",querySelector:function(selector){return selector==="#listSearch"?{addEventListener:function(){}}:selector==="[data-list-pagination]"?listPager:null;},querySelectorAll:function(){return[];}};
  var navigation=[];var renderContext={state:{},navigate:function(route,state){navigation.push({route:route,state:state});},canNavigate:function(){return true;},registry:registry,user:{role:"administrator"},isActive:function(){return true;}};
  function render(){context.window.AlKhayalRenderers["entity-list"](screen,container,renderContext,{items:context.window.AlKhayalMockSeed.customers,page:1,pageSize:50,totalCount:context.window.AlKhayalMockSeed.customers.length});}
  render();
  assert.ok(container.innerHTML.indexOf('data-list-add>+ إضافة عميل<')>=0,"customer management must expose an Arabic add-customer action");
  assert.ok(container.innerHTML.indexOf('data-row-action="edit"')>=0&&container.innerHTML.indexOf(">تعديل<")>=0,"customer management must expose an Arabic edit action for each customer");
  var row={getAttribute:function(name){return name==="data-row-id"?"1":null;}};
  container.onclick({target:{closest:function(selector){return selector==="tr[data-row-id]"?row:null;}}});
  assert.strictEqual(navigation[0].route,"customer-statement","clicking a customer row must open the statement route");
  assert.strictEqual(navigation[0].state.customerStatementId,1,"clicking a customer row must navigate with that exact persisted customer ID");
  return container.onclick({target:{closest:function(selector){if(selector==="tr[data-row-id]")return row;if(selector==="[data-row-action]")return {getAttribute:function(){return "edit";},closest:function(){return row;}};return null;}}}).then(function(){
    assert.ok(modalLayer.innerHTML.indexOf("تعديل بيانات العميل")>=0,"the customer edit action must open the Arabic customer edit form");
    assert.ok(modalLayer.innerHTML.indexOf('value="CUS-001"')>=0&&modalLayer.innerHTML.indexOf('value="أحمد محمد علي"')>=0,"the customer edit form must open with the saved customer values");
    assert.ok(modalLayer.innerHTML.includes('id="phone"')&&modalLayer.innerHTML.includes('id="secondaryPhone"')&&modalLayer.innerHTML.includes('id="address"'),"customer edit must expose both phone fields and delivery address");
    assert.ok(modalLayer.innerHTML.includes("رقم الهاتف الأساسي")&&modalLayer.innerHTML.includes("رقم الهاتف الإضافي")&&modalLayer.innerHTML.includes("عنوان التوصيل"),"customer contact labels must be explicit");
    return modalLayer.selectors["[data-save-record]"].onclick();
  }).then(function(){
    var update=apiCalls.filter(function(call){return call.target==="/api/customers/1"&&call.options.method==="PUT";});
    assert.strictEqual(update.length,1,"the customer edit form must save through the persisted customer API");
    assert.ok(Object.prototype.hasOwnProperty.call(JSON.parse(update[0].options.body),"secondaryPhone"),"customer edit payload must include the secondary phone");
    modalLayer.selectors["[data-save-record]"].disabled=false;
    context.window.AlKhayalServices.store.replace("customers",context.window.AlKhayalMockSeed.customers);
    render();
    return container.onclick({target:{closest:function(selector){return selector==="[data-list-add]"?{}:null;}}});
  }).then(function(){
    assert.ok(modalLayer.innerHTML.indexOf("إضافة عميل")>=0,"the customer management screen must open the Arabic add-customer form");
    assert.ok(modalLayer.innerHTML.indexOf('id="code"')<0&&modalLayer.innerHTML.indexOf('id="name"')>=0&&modalLayer.innerHTML.indexOf('id="phone"')>=0,"the add-customer form must generate the code and keep the other customer fields");
    assert.ok(modalLayer.innerHTML.includes('id="secondaryPhone"')&&modalLayer.innerHTML.includes('id="address"'),"customer creation must expose optional contact fields");
    return modalLayer.selectors["[data-save-record]"].onclick();
  }).then(function(){
    var create=apiCalls.filter(function(call){return call.target==="/api/customers"&&call.options.method==="POST";});
    assert.strictEqual(create.length,1,"the add-customer form must save through the persisted customer API");
    assert.ok(Object.prototype.hasOwnProperty.call(JSON.parse(create[0].options.body),"secondaryPhone"),"customer create payload must include the secondary phone");
    var formElement=fakeElement();
    formElement.elements=new Proxy({}, {get:function(target,key){return typeof key==="string"?Object.assign(fakeElement(),{value:"CUS-777"}):undefined;}});
    formElement.reset=function(){};
    var addScreen=registry.screens.find(function(item){return item.route==="customer-add";});
    var formContainer={innerHTML:"",querySelector:function(selector){return selector==="#entityForm"?formElement:null;},querySelectorAll:function(){return[];}};
    context.window.AlKhayalRenderers["entity-form"](addScreen,formContainer,renderContext,true);
    assert.ok(formContainer.innerHTML.indexOf('id="entityForm"')>=0&&formContainer.innerHTML.indexOf('id="code"')<0&&formContainer.innerHTML.indexOf('id="name"')>=0,"the contextual customer-add route must generate the code and open the real customer form");
    return formElement.onsubmit({preventDefault:function(){}});
  }).then(function(){
    var routeCreate=apiCalls.filter(function(call){return call.target==="/api/customers"&&call.options.method==="POST";});
    assert.strictEqual(routeCreate.length,2,"the contextual customer-add route must save through the persisted customer API");
  });
}
function verifyEntityListSearchKeepsFocus(){
  function fakeElement(){var element={innerHTML:"",textContent:"",value:"",listeners:{},addEventListener:function(name,handler){element.listeners[name]=handler;},querySelector:function(){return null;},querySelectorAll:function(){return[];},getAttribute:function(){return null;}};return element;}
  var context={window:{},console:console,document:{activeElement:null,getElementById:function(id){return id==="modalLayer"||id==="toastStack"?fakeElement():null;},createElement:function(){return fakeElement();},body:{appendChild:function(){}}},localStorage:{getItem:function(){return null;},setItem:function(){}},setTimeout:setTimeout,clearTimeout:clearTimeout,AlKhayalApi:{request:function(url){assert.ok(url.startsWith("/api/customers/page?"));var query=new URL(url,"http://localhost").searchParams.get("search");var rows=context.window.AlKhayalMockSeed.customers.filter(function(row){return row.name.includes(query);});return Promise.resolve({items:rows,page:1,pageSize:50,totalCount:rows.length});}}};
  context.window.window=context.window;
  function mirrorGlobals(){["AlKhayalUtils","AlKhayalCalculations","AlKhayalMockSeed","AlKhayalServices","AlKhayalUI","AlKhayalApi"].forEach(function(key){if(context[key]!==undefined)context.window[key]=context[key];});}
  ["frontend/js/core/utils.js","frontend/js/core/calculations.js","frontend/js/mocks/data.js","frontend/js/services/mock-services.js","frontend/js/components/ui.js"].forEach(function(relative){vm.runInNewContext(fs.readFileSync(path.join(root,relative),"utf8"),context,{filename:relative});mirrorGlobals();});
  vm.runInNewContext(renderersJs,context,{filename:"renderers.js"});
  var html="",selectors={};
  var container={onclick:null,querySelectorAll:function(){return[];}};
  Object.defineProperty(container,"innerHTML",{get:function(){return html;},set:function(value){html=String(value);selectors={};}});
  container.querySelector=function(selector){
    var markers={"#listSearch":'id="listSearch"',"[data-table-host]":"data-table-host","[data-list-count]":"data-list-count","[data-list-pagination]":"data-list-pagination"};
    if(markers[selector]===undefined||html.indexOf(markers[selector])<0)return null;
    if(!selectors[selector])selectors[selector]=fakeElement();
    return selectors[selector];
  };
  var screen=registry.screens.find(function(item){return item.route==="customers-list";});
  var renderContext={state:{},navigate:function(){},canNavigate:function(){return true;},registry:registry,user:{role:"administrator"},isActive:function(){return true;}};
  context.window.AlKhayalRenderers["entity-list"](screen,container,renderContext,{items:context.window.AlKhayalMockSeed.customers,page:1,pageSize:50,totalCount:context.window.AlKhayalMockSeed.customers.length});
  var search=container.querySelector("#listSearch"),tableHost=container.querySelector("[data-table-host]");
  assert.ok(search&&typeof search.listeners.input==="function","entity list must wire a debounced search listener on #listSearch");
  function type(text){search.value=text;search.listeners.input();return new Promise(function(resolve){setTimeout(resolve,250);});}
  return type("أحمد").then(function(){
    assert.strictEqual(container.querySelector("#listSearch"),search,"debounced entity-list search must preserve the #listSearch DOM node so keyboard focus survives");
    assert.strictEqual(container.querySelector("[data-table-host]"),tableHost,"search refresh must reuse the table host node instead of rebuilding the toolbar");
    assert.ok(tableHost.innerHTML.indexOf('data-row-id="1"')>=0,"filtered entity list must render the matching customer row");
    assert.ok(tableHost.innerHTML.indexOf('data-row-id="2"')<0,"filtered entity list must drop non-matching customer rows");
    assert.strictEqual(container.querySelector("[data-list-count]").textContent,context.window.AlKhayalUtils.number(1)+" سجل","search refresh must update the Arabic record count in place");
    return type("محمود").then(function(){
      assert.strictEqual(container.querySelector("#listSearch"),search,"a second search keystroke must still hit the same preserved input node");
      assert.ok(tableHost.innerHTML.indexOf('data-row-id="2"')>=0&&tableHost.innerHTML.indexOf('data-row-id="1"')<0,"the preserved input must keep driving filtered results");
      assert.strictEqual(container.querySelector("[data-list-count]").textContent,context.window.AlKhayalUtils.number(1)+" سجل","repeated searches must keep the record count in sync");
    });
  });
}
async function verifySearchInputFocusSurvival(){
  function fakeElement(){var el={isConnected:true,checked:false,disabled:false,value:"",textContent:"",innerHTML:""};return el;}
  function makeContainer(markers){
    var html="",writes=0,selectors={};
    var node=fakeElement();
    Object.defineProperty(node,"innerHTML",{get:function(){return html;},set:function(value){html=String(value);writes+=1;selectors={};}});
    node.querySelector=function(selector){
      var needle=markers[selector];
      if(!needle||html.indexOf(needle)<0)return null;
      if(!selectors[selector])selectors[selector]=fakeElement();
      return selectors[selector];
    };
    node.querySelectorAll=function(){return[];};
    return{node:node,selectors:function(){return selectors;},writes:function(){return writes;},innerHTML:function(){return html;}};
  }
  function tick(ms){return new Promise(function(resolve){setTimeout(resolve,ms||0);});}
  function flush(){return tick().then(function(){return tick();});}
  var context={window:{},console:console,setTimeout:setTimeout,clearTimeout:clearTimeout,Url:URL};
  context.window.window=context.window;
  vm.runInNewContext(fs.readFileSync(path.join(root,"frontend","js","core","utils.js"),"utf8"),context,{filename:"utils.js"});
  var toasts=[],queue=[],calls=[];
  context.window.AlKhayalUI={pageHeader:function(screen,actions){return String(actions||"");},toast:function(message,kind){toasts.push({message:message,kind:kind});},badge:function(text){return "<span>"+text+"</span>";},confirm:function(){return Promise.resolve(false);}};
  context.window.AlKhayalFeatures={isEnabled:function(){return true;},routeEnabled:function(){return true;},find:function(){return null;},load:function(){return Promise.resolve({});}};
  context.window.AlKhayalApi={request:function(url){calls.push(url);return new Promise(function(resolve,reject){var item=queue.shift();if(!item){reject(new Error("طلب غير متوقع: "+url));return;}if(item.error){reject(new Error(item.error));return;}resolve(item.value);});}};
  var renderContext={registry:registry,state:{},user:{role:"administrator",id:"admin"},canNavigate:function(){return true;},navigate:function(){},isActive:function(){return true;}};
  context.window.AlKhayalRenderers={};
  [settingsHubJs,catalogJs].forEach(function(source,index){vm.runInNewContext(source,context,{filename:"search-focus-"+index+".js"});});
  // Settings delegates its existing input to the shared search without repainting cards.
  var box=makeContainer({"[data-settings-search]":'data-settings-search',"[data-settings-search-panel]":'data-settings-search-panel'});
  var bound; renderContext.mountSettingsSearch=function(input,panel){bound={input:input,panel:panel};};
  context.window.AlKhayalRenderers["settings-hub"](registry.screens.find(function(item){return item.route==="settings-program";}),box.node,renderContext);
  assert.ok(box.writes()===1,"settings hub initial render must write the shell exactly once");
  assert.strictEqual(bound.input,box.node.querySelector("[data-settings-search]"));
  assert.strictEqual(bound.panel,box.node.querySelector("[data-settings-search-panel]"));
  assert.ok(box.node.innerHTML.includes("data-settings-route"),"Settings retains its grouped links");
  // Product list: debounced search re-queries the persisted API and repaints only the results region.
  var box=makeContainer({"#catalogSearch":'id="catalogSearch"',"#catalogResults":'id="catalogResults"',"#catalogCategory":'id="catalogCategory"',"#catalogStatus":'id="catalogStatus"'});
  queue.length=0;calls.length=0;
  queue.push({value:[{id:1,name:"عام",isActive:true}]});
  queue.push({value:{items:[{id:11,name:"قميص",description:"",categoryName:"عام",baseUnitName:"قطعة",unitSymbol:"قطعة",productType:"stock",salePrice:"10.00",isActive:true,isArchived:false,version:1,sizeValues:[],colorValues:[],variantCount:1,baseUnitId:1}],page:1,pageSize:20,totalCount:1}});
  queue.push({value:{items:[],page:1,pageSize:20,totalCount:0}});
  context.window.AlKhayalRenderers["catalog-products"](registry.screens.find(function(item){return item.route==="products-list";}),box.node,renderContext);
  await flush();
  assert.ok(queue.length===1,"product list must load categories and products in one bounded cycle");
  assert.ok(box.innerHTML().indexOf('data-add-product')>=0,"product directory must retain the normal Add Product action");
  assert.ok(box.innerHTML().indexOf('data-quick-product')<0,"product directory must not render Quick Add");
  var search=box.node.querySelector("#catalogSearch"),results=box.node.querySelector("#catalogResults"),writes=box.writes();
  assert.ok(box.node.querySelector("#catalogResults").innerHTML.indexOf('data-product-id="11"')>=0,"product list must render persisted product rows");
  assert.ok(results.innerHTML.indexOf('data-edit-product')>=0&&results.innerHTML.indexOf('catalog-actions-menu')>=0&&results.innerHTML.indexOf('data-product-barcodes')>=0,"product rows must keep Edit visible and secondary actions in a menu");
  assert.ok(box.innerHTML().indexOf('data-product-id="11"')<0,"product rows must live in the results region, not the shell markup");
  search.value="قميص";search.oninput();
  assert.strictEqual(box.node.querySelector("#catalogSearch"),search,"product search must not replace its own input node");
  await tick(320);
  assert.ok(box.writes()===writes,"product search must not rewrite the page container after the debounced fetch");
  assert.ok(calls.some(function(url){return url.indexOf("/api/catalog/products?search="+encodeURIComponent("قميص"))===0;}),"typing in the product search must re-query the persisted products API");
  assert.ok(box.node.querySelector("#catalogResults").innerHTML.indexOf("لا توجد منتجات مطابقة")>=0,"the empty filtered product page must render the Arabic empty state in the results region");
  // Category and unit management: search filters rows in place.
  var box=makeContainer({"#categoryManagementSearch":'id="categoryManagementSearch"',"#categoryManagementResults":'id="categoryManagementResults"',"[data-add-management]":'data-add-management'});
  queue.length=0;calls.length=0;
  queue.push({value:[{id:1,name:"مخزون",parentCategoryId:null,isActive:true,sortOrder:1}]});
  context.window.AlKhayalRenderers["catalog-categories"](registry.screens.find(function(item){return item.route==="product-categories";}),box.node,renderContext);
  await flush();
  var search=box.node.querySelector("#categoryManagementSearch"),results=box.node.querySelector("#categoryManagementResults"),writes=box.writes();
  assert.ok(results.innerHTML.indexOf('data-management-id="1"')>=0,"category management must render the persisted category rows");
  assert.ok(box.innerHTML().indexOf('data-management-id="1"')<0,"category rows must live in the results region, not the shell markup");
  search.value="لا يوجد";search.oninput();
  assert.strictEqual(box.node.querySelector("#categoryManagementSearch"),search,"category search must not replace its input node");
  assert.ok(box.writes()===writes,"category search must not rewrite the page container");
  assert.ok(results.innerHTML.indexOf('data-management-id="1"')<0&&results.innerHTML.indexOf("لا توجد تصنيفات")>=0,"category search must filter rows in the persistent region");
  var unitBox=makeContainer({"#unitManagementSearch":'id="unitManagementSearch"',"#unitManagementResults":'id="unitManagementResults"',"[data-add-management]":'data-add-management'});
  queue.length=0;calls.length=0;
  queue.push({value:[{id:5,code:"PCS",name:"قطعة",symbol:"قطعة",isActive:true,isSystem:true}]});
  context.window.AlKhayalRenderers["catalog-units"](registry.screens.find(function(item){return item.route==="unit-add";}),unitBox.node,renderContext);
  await flush();
  var unitSearch=unitBox.node.querySelector("#unitManagementSearch"),unitResults=unitBox.node.querySelector("#unitManagementResults"),unitWrites=unitBox.writes();
  unitSearch.value="لا شيء";unitSearch.oninput();
  assert.strictEqual(unitBox.node.querySelector("#unitManagementSearch"),unitSearch,"unit search must not replace its input node");
  assert.ok(unitBox.writes()===unitWrites,"unit search must not rewrite the page container");
  assert.ok(unitResults.innerHTML.indexOf('data-management-id="5"')<0&&unitResults.innerHTML.indexOf("لا توجد وحدات مطابقة")>=0,"unit search must filter rows in the persistent region");
}

function makeProduct(id, name, price, quantity) {
  return { id: id, productType: "stock", isActive: true, isArchived: false, name: name, salePrice: price, unitName: "قطعة", availableQuantityScaled:quantity == null ? 1000000 : quantity * 1000000 };
}
async function verifySalesSearchMountedInput() {
  function fakeElement() { var el = { isConnected: true, checked: false, disabled: false, value: "", textContent: "", innerHTML: "" }; return el; }
  function makeContainer(markers) {
    var html = "", writes = 0, selectors = {};
    var node = fakeElement();
    Object.defineProperty(node, "innerHTML", { get: function () { return html; }, set: function (value) { html = String(value); writes += 1; selectors = {}; } });
    node.querySelector = function (selector) {
      var needle = markers[selector];
      if (!needle || html.indexOf(needle) < 0) return null;
      if (!selectors[selector]) selectors[selector] = fakeElement();
      return selectors[selector];
    };
    node.querySelectorAll = function () { return []; };
    return { node: node, writes: function () { return writes; }, innerHTML: function () { return html; } };
  }
  function tick(ms) { return new Promise(function (resolve) { setTimeout(resolve, ms || 0); }); }
  function flush() { return tick().then(function () { return tick(); }); }
  assert.ok(salesJs.indexOf("next.focus();next.setSelectionRange") < 0, "the POS product search must not rely on a refocus-after-redraw hack");
  var deferred = [], queue = [], requestedUrls=[];
  function buildContext() {
    var context = { window: {}, console: console, setTimeout: setTimeout, clearTimeout: clearTimeout, URL: URL, URLSearchParams: URLSearchParams, Date: Date, BigInt: BigInt };
    context.window.window = context.window;
    vm.runInNewContext(fs.readFileSync(path.join(root, "frontend", "js", "core", "utils.js"), "utf8"), context, { filename: "utils.js" });
    context.window.AlKhayalUI = { pageHeader: function (screen, actions) { return String(actions || ""); }, toast: function () { }, badge: function (text) { return "<span>" + text + "</span>"; }, confirm: function () { return Promise.resolve(true); } };
    context.window.AlKhayalSettings = { current: function () { return {}; }, load: function () { return Promise.resolve({}); } };
    context.window.AlKhayalFeatures = { isEnabled: function () { return true; }, routeEnabled: function () { return true; }, find: function () { return null; }, load: function () { return Promise.resolve({}); } };
    context.window.AlKhayalApi = {
      request: function (url) {
        requestedUrls.push(url);
        if(url==="/api/payment-methods/active"||url==="/api/treasury/accounts")return Promise.resolve([]);
        return new Promise(function (resolve, reject) {
          var item = queue.shift();
          if (!item) throw new Error("طلب غير متوقع: " + url);
          if (item.error) { reject(new Error(item.error)); return; }
          if (item.defer) { deferred.push({ resolve: resolve, reject: reject, url: url }); return; }
          resolve(item.value);
        });
      }
    };
    context.window.AlKhayalRenderers = {};
    return context;
  }
  var renderContext = { state: {}, user: { role: "administrator", id: "admin" }, canNavigate: function () { return true; }, navigate: function () { }, registry: registry, isActive: function () { return true; } };
  function runModule(context) {
    var source = salesJs.replace("    init();", "    global.__salesTestFor={state:state,draw:draw,drawProducts:drawProducts,accessChanged:accessChanged,searchProducts:searchProducts,searchCustomers:searchCustomers,click:click,payload:payload,captureBarcodeKey:captureBarcodeKey};\n    init();");
    assert.strictEqual((source.match(/__salesTestFor/g) || []).length, 1, "the sales test hook must attach exactly once");
    vm.runInNewContext(source, context, { filename: "sales-search-focus.js" });
  }
  // --- POS: stale product-search responses lose against newer typed queries, and the input stays mounted. ---
  var posContext = buildContext();
  posContext.window._accessSnapshot={role:"cashier",canViewPosStockQuantities:false};

  runModule(posContext);
  queue.push({ value: [{ id: 1, name: "مخزن رئيسي", isActive: true, isDefault: true }] }, { value: {} },{ value:[{id:5,name:"ملابس"}] },{value:{items:[makeProduct(3,"قميص","10")],totalCount:42}});
  var posBox = makeContainer({ "[data-search]": "data-search", ".touch-product-grid": "touch-product-grid", ".touch-product-status":"touch-product-status", "[data-customer-search]":"data-customer-search", "[data-customer-results]":"data-customer-results", "[data-warehouse-search]":"data-warehouse-search", "[data-warehouse-results]":"data-warehouse-results" });
  posContext.window.AlKhayalRenderers.pos(registry.screens.find(function (item) { return item.route === "pos"; }), posBox.node, renderContext);
  await flush();
  assert.ok(posBox.innerHTML().indexOf('input class="input touch-search" data-search') >= 0, "touch POS must render its persistent search input");
  assert.ok(requestedUrls.some(function(url){return url.indexOf("/api/inventory/products?search=&page=1&pageSize=40")===0;}),"touch POS must load its first product page without a search");
  assert.ok(posBox.innerHTML().indexOf('data-category="5"')>=0,"touch POS must render category browsing");
  var test = posContext.window.__salesTestFor, posState = test.state;
  assert.ok(posBox.innerHTML().indexOf('data-category="" class="selected">كل المنتجات')>=0,"All products must be selected by default");
  assert.strictEqual(posState.categoryId,"","initial category must be All");
  var originalCategories=JSON.stringify(posState.categories);

  function assertCheckout(populated) {
    var html=posBox.innerHTML(),footer=html.slice(html.indexOf('<footer class="sale-summary">'));
    ["data-discount-mode","data-discount","data-net","data-tax","data-total","data-clear","data-post"].forEach(function(marker){assert.ok(footer.indexOf(marker)>=0,"checkout must retain "+marker+" with an "+(populated?"occupied":"empty")+" cart");});
    assert.ok(html.indexOf('data-payment aria-label="طريقة الدفع"')>=0&&html.indexOf("data-payment-instruments")>=0,"payment controls must remain in the cart header");
    assert.strictEqual(/data-post\s+disabled/.test(footer),!populated,"Complete Sale stays rendered and is enabled only for a populated editable cart");
  }
  assertCheckout(false);
  var grid = posBox.node.querySelector(".touch-product-grid");
  assert.ok(grid, "touch POS must render its product grid inside the shell");
  var writesAfterRender = posBox.writes();
  var searchInput = posBox.node.querySelector("[data-search]");
  // First (older) search stays in flight while a second query starts and resolves first:
  posState.productQuery = "بخل";
  queue.push({ value: { items: [makeProduct(9, "بطيخ", "10")],totalCount:1 }, defer: true });
  test.searchProducts();
  posState.productQuery = "بهند";
  queue.push({ value: { items: [makeProduct(2, "بهند", "20", 20)],totalCount:1 } });
  var newer = test.searchProducts();
  await flush();
  assert.strictEqual(posBox.writes(), writesAfterRender, "POS product searches must not rewrite the shell container");
  assert.ok(grid.innerHTML.indexOf('data-product="2"') >= 0, "the newest product search payload must paint the grid");
  assert.ok(!grid.innerHTML.includes("المتبقي:"),"stock quantity is hidden from cashiers without the independent visibility permission");
  var writesBeforeDeferred=posBox.writes();
  deferred.shift().resolve({ items: [makeProduct(9, "بطيخ", "10")],totalCount:1 }); // the resident stale first response arrives now
  await flush();
  assert.ok(grid.innerHTML.indexOf('data-product="9"') < 0, "a stale product search response must never replace newer results");
  assert.ok(grid.innerHTML.indexOf('data-product="2"') >= 0, "new results must survive the stale response");
  assert.strictEqual(posBox.writes(), writesBeforeDeferred, "POS search repaints must stay inside the product grid region");
  // Permission changes redraw the product grid and preserve the mounted search input.
  assert.ok(posBox.node.querySelector("[data-search]"), "POS typing must retain its mounted search input");
  posState.products=[makeProduct(2,"بهند","20",20)];
  test.accessChanged({detail:{role:"administrator",screenIds:["pos"],canViewPosStockQuantities:true}});
  assert.ok(grid.innerHTML.indexOf("المتبقي: 20")>=0,"authorized users see the current remaining quantity on POS cards");
  test.accessChanged({detail:{role:"cashier",screenIds:["pos"],canViewPosStockQuantities:false}});
  assert.ok(grid.innerHTML.indexOf("المتبقي:")<0,"revoking permission hides quantities without logout");
  assert.ok(accessStateJs.includes('next.canViewPosStockQuantities = next.role === "administrator"'),"administrator access state always enables stock visibility");
  assert.ok(salesJs.includes('access.canViewPosStockQuantities&&p.availableQuantityScaled!=null?\'<small class="sale-stock">المتبقي: '),"POS card template gates stock visibility on the grant");
  posState.productQuery="";
  queue.push({value:{items:[makeProduct(2,"بهند","20")],totalCount:2}});
  var categoryOption={getAttribute:function(){return "5";}};
  await test.click({target:{closest:function(selector){return selector==="[data-category]"?categoryOption:null;}}});await flush();
  assert.ok(requestedUrls[requestedUrls.length-1].indexOf("categoryId=5")>=0,"category selection must constrain the server-side product page");
  assert.ok(requestedUrls[requestedUrls.length-1].indexOf("posWarehouseId=1")>=0,"POS browse must send its selected warehouse");
  queue.push({value:{items:[makeProduct(4,"قميص","30")],totalCount:2}});
  await test.searchProducts(true);
  assert.ok(requestedUrls[requestedUrls.length-1].indexOf("page=2")>=0,"more products must request the next server page");
  assert.ok(grid.innerHTML.indexOf('data-product="2"')>=0&&grid.innerHTML.indexOf('data-product="4"')>=0,"pagination must append without losing prior cards");

  assert.strictEqual(posState.categoryId,"5","pagination must retain the selected category");
  assert.strictEqual(JSON.stringify(posState.categories),originalCategories,"search and paging must not rebuild categories from current results");
  var zeroProduct=makeProduct(8,"نفد المخزون","10");zeroProduct.categoryId=5;zeroProduct.availableQuantityScaled=0;
  queue.push({value:{items:[zeroProduct],totalCount:1}});posState.productQuery="نفد";
  await test.searchProducts(false);
  assert.strictEqual(posState.categoryId,"5","live search must retain selected category");
  assert.ok(requestedUrls[requestedUrls.length-1].indexOf("categoryId=5")>=0,"live search must filter on server within selected category");
  assert.ok(grid.innerHTML.indexOf("sale-category-color-5")>=0,"tile color must derive from category ID, independent of result position");
  assert.ok(!/data-product="8"[^>]*disabled/.test(grid.innerHTML),"zero stock tile must remain enabled");
  queue.push({value:{items:[zeroProduct],totalCount:1}});posState.productQuery="";await test.searchProducts(false);
  assert.ok(grid.innerHTML.indexOf("sale-category-color-5")>=0,"category color must survive search reset");
  assert.strictEqual(JSON.stringify(posState.categories),originalCategories,"category list must survive search reset");
  posState.customerQuery="01055";
  queue.push({value:[{id:55,name:"عميل هاتف",code:"C55",phone:"01055"}]});
  await test.searchCustomers();
  assert.ok(requestedUrls[requestedUrls.length-1].indexOf("/api/customers/search?query=01055")===0,"customer search must query the bounded server selector by phone");
  assert.ok(posBox.node.querySelector("[data-customer-results]").innerHTML.indexOf('data-customer-option="55"')>=0,"customer matches must remain in a separate results region");
  posState.customerQuery="قديم";queue.push({value:[],defer:true});var olderCustomer=test.searchCustomers();
  posState.customerQuery="جديد";queue.push({value:[{id:55,name:"عميل جديد",code:"C55",phone:"01055"}]});await test.searchCustomers();
  deferred.shift().resolve([{id:99,name:"عميل قديم",code:"C99",phone:"099"}]);await olderCustomer;
  assert.ok(posBox.node.querySelector("[data-customer-results]").innerHTML.indexOf('data-customer-option="55"')>=0&&posBox.node.querySelector("[data-customer-results]").innerHTML.indexOf('data-customer-option="99"')<0,"stale customer lookup must not replace newer matches");
  var customerOption={getAttribute:function(name){return name==="data-customer-option"?"55":"عميل هاتف";}};
  await test.click({target:{closest:function(selector){return selector==="[data-customer-option]"?customerOption:null;}}});
  assert.strictEqual(posState.customerId,"55","selecting a customer must preserve its invoice association");
  assert.strictEqual(test.payload().customerId,55,"checkout payload must carry the selected customer ID");
  posState.warehouses.push({id:2,name:"مخزن ثان",code:"W2",isActive:true});posState.multipleWarehouses=true;test.draw();
  queue.push({value:{items:[],totalCount:0}});
  var warehouseOption={getAttribute:function(){return "2";}};
  await test.click({target:{closest:function(selector){return selector==="[data-warehouse-option]"?warehouseOption:null;}}});await flush();
  assert.strictEqual(posState.warehouseId,2,"warehouse selector must preserve the selected warehouse");
  assert.ok(requestedUrls[requestedUrls.length-1].indexOf("posWarehouseId=2")>=0,"warehouse changes must query availability for the new warehouse");
  var barcode="123456789";
  queue.push({value:{isSellable:true,productId:70,variantId:71,productUnitId:72,effectiveSalePrice:"12.00"}},
    {value:{id:70,name:"صنف باركود",salePrice:"12.00",attributes:[],capabilities:[],variants:[{id:71,name:"صنف باركود",isActive:true,isArchived:false,isDefault:true}],units:[{id:72,name:"قطعة",isActive:true,isSaleUnit:true,isBase:true}]}},
    {value:{items:[],totalCount:0}});
  barcode.split("").forEach(function(ch){test.captureBarcodeKey({key:ch,target:{tagName:"BODY"}});});
  test.captureBarcodeKey({key:"Enter",target:{tagName:"BODY"},preventDefault:function(){this.prevented=true;}});
  await flush();
  assert.strictEqual(posState.lines.length,1,"a scanner sequence followed by Enter must add an exact barcode once");
  assertCheckout(true);
  assert.strictEqual(requestedUrls.filter(function(url){return url.indexOf("/api/catalog/barcodes/lookup?code="+barcode)===0;}).length,1,"scanner must issue one exact barcode lookup");
  queue.push({value:{isSellable:true,productId:70,variantId:71,productUnitId:72,effectiveSalePrice:"12.00"}},
    {value:{id:70,name:"صنف باركود",salePrice:"12.00",attributes:[],capabilities:[],variants:[{id:71,name:"صنف باركود",isActive:true,isArchived:false,isDefault:true}],units:[{id:72,name:"قطعة",isActive:true,isSaleUnit:true,isBase:true}]}},
    {value:{items:[],totalCount:0}});
  barcode.split("").forEach(function(ch){test.captureBarcodeKey({key:ch,target:{tagName:"BODY"}});});
  test.captureBarcodeKey({key:"Enter",target:{tagName:"BODY"},preventDefault:function(){}});
  await flush();
  assert.strictEqual(posState.lines[0].quantity,"2","repeated scans must increase the existing cart quantity");
  var protectedTarget={tagName:"INPUT",closest:function(selector){return selector.indexOf("[data-customer-search]")>=0?{}:null;}};
  "555555555".split("").forEach(function(ch){test.captureBarcodeKey({key:ch,target:protectedTarget});});
  test.captureBarcodeKey({key:"Enter",target:protectedTarget,preventDefault:function(){}});
  assert.strictEqual(posState.lines[0].quantity,"2","customer search typing and Enter must not trigger barcode scans");
  queue.push({error:"لم يتم العثور على الباركود"});
  "000000000".split("").forEach(function(ch){test.captureBarcodeKey({key:ch,target:{tagName:"BODY"}});});
  test.captureBarcodeKey({key:"Enter",target:{tagName:"BODY"},preventDefault:function(){}});
  await flush();
  assert.strictEqual(posState.lines[0].quantity,"2","unknown scans must not alter the cart");
  var manualInput=posBox.node.querySelector("[data-search]");
  manualInput.value="123456789";
  queue.push({value:{isSellable:true,productId:70,variantId:71,productUnitId:72,effectiveSalePrice:"12.00"}},
    {value:{id:70,name:"صنف باركود",salePrice:"12.00",attributes:[],capabilities:[],variants:[{id:71,name:"صنف باركود",isActive:true,isArchived:false,isDefault:true}],units:[{id:72,name:"قطعة",isActive:true,isBase:true,isSaleUnit:true}]}},
    {value:{items:[],totalCount:0}});
  manualInput.onkeydown({key:"Enter",preventDefault:function(){}});
  await flush();
  assert.strictEqual(posState.lines[0].quantity,"3","manual barcode entry followed by Enter must use the same lookup/cart path");
  var pasteInput=posBox.node.querySelector("[data-search]");pasteInput.value="قميص";queue.push({value:{items:[makeProduct(6,"قميص","10")],totalCount:1}});pasteInput.oninput({inputType:"insertFromPaste"});
  await tick(220);
  assert.ok(posBox.node.querySelector(".touch-product-grid").innerHTML.indexOf('data-product="6"')>=0,"pasting a search term must update results without removing the input");
  assert.strictEqual(posBox.node.querySelector("[data-search]"),pasteInput,"paste search must preserve the focused input node");
  assert.strictEqual(posState.lines.length,1,"paste must not be misread as a scanner sale");
  assert.ok(posBox.innerHTML().includes("خزنة إيراد الوردية"), "touch POS must explain the automatic cash receipt destination");
  posState.invoice={id:99,status:"posted",paymentMode:"cash",totalScaled:100000,paidScaled:100000,treasuryMovementId:7,treasuryAccountName:"إيراد الوردية"};
  test.draw();
  assert.ok(posBox.innerHTML().includes("تم إيداع") && posBox.innerHTML().includes("إيراد الوردية"), "posted POS must confirm the persisted treasury receipt");
  // New invoice is immediate when empty and offers explicit choices when dirty.
  var activeModal=null,modalButtons={};
  posContext.window.AlKhayalUI.modal=function(options){activeModal=options;modalButtons={"[data-suspend-invoice]":{disabled:false},"[data-discard-invoice]":{disabled:false}};options.onOpen({querySelector:function(selector){return modalButtons[selector]||{hidden:true,textContent:""};}});};
  posContext.window.AlKhayalUI.closeModal=function(){if(activeModal&&activeModal.beforeClose)activeModal.beforeClose();activeModal=null;};
  function newInvoiceClick(){return test.click({target:{closest:function(selector){return selector==="[data-new-touch-invoice]"?{}:null;}}});}
  posState.invoice={id:99,status:"posted",paymentMode:"cash",totalScaled:100000,paidScaled:100000};test.draw();await newInvoiceClick();
  assert.strictEqual(posState.invoice,null);assert.strictEqual(posState.lines.length,0);assert.strictEqual(activeModal,null);
  await newInvoiceClick();assert.strictEqual(activeModal,null,"empty invoice starts without confirmation");
  posState.lines=[{productName:"item",variantName:"base",unitName:"unit",variantId:71,productUnitId:72,quantity:"2",unitPrice:"10.00",discount:"0.00"}];
  test.draw();var preserved=JSON.stringify(posState),pendingReset=newInvoiceClick();await tick(0);
  assert.ok(activeModal,"dirty invoice opens confirmation");posContext.window.AlKhayalUI.closeModal();await pendingReset;
  assert.strictEqual(JSON.stringify(posState),preserved,"cancel preserves the invoice");
  pendingReset=newInvoiceClick();await tick(0);modalButtons["[data-discard-invoice]"].onclick();await pendingReset;
  assert.strictEqual(posState.lines.length,0,"discard starts an empty invoice");
  assertCheckout(false);
  posState.busy=true;test.draw();assert.ok(/data-new-touch-invoice disabled/.test(posBox.innerHTML()));await newInvoiceClick();posState.busy=false;
  // --- Invoice history: typing refreshes only the dedicated results region. ---
  var listContext = buildContext(), salesNavigation=[];
  runModule(listContext);
  var listBox = makeContainer({ "#salesSearch": 'id="salesSearch"', "#salesListResults": 'id="salesListResults"' });
  queue.length = 0; deferred.length = 0;
  queue.push({ value: { items: [{ id: 20, status: "posted", invoiceNumber: "S-00020", invoiceDate: "2026-10-20", customerName: "", warehouseName: "م", totalScaled: 100000, paidScaled: 100000 }], page: 1, pageSize: 50, totalCount: 1 } });
  var salesListContext=Object.assign({},renderContext,{navigate:function(route,state){salesNavigation.push({route:route,state:state});}});
  listContext.window.AlKhayalRenderers["sales-invoices"](registry.screens.find(function (item) { return item.route === "sales-list"; }), listBox.node, salesListContext);
  await flush();
  var listWrites = listBox.writes(), listSearch = listBox.node.querySelector("#salesSearch"), listResults = listBox.node.querySelector("#salesListResults");
  assert.ok(listResults, "invoice history must render a dedicated results region");
  assert.ok(listResults.innerHTML.indexOf('data-open="20"') >= 0 && listBox.innerHTML().indexOf('id="salesListResults"') >= 0, "invoice history must render its rows in a dedicated results region");
  listSearch.value = "S-00021"; listSearch.oninput();
  queue.push({ value: { items: [{ id: 21, status: "posted", invoiceNumber: "S-00021", invoiceDate: "2026-10-21", customerName: "", warehouseName: "م", totalScaled: 100000, paidScaled: 100000 }], page: 1, pageSize: 50, totalCount: 1 } });
  await tick(220);
  assert.ok(listBox.writes() === listWrites, "invoice-history typing must not rewrite the page container");
  assert.strictEqual(listBox.node.querySelector("#salesSearch"), listSearch, "invoice history must keep a single mounted search input");
  assert.ok(listResults.innerHTML.indexOf('data-open="21"') >= 0, "invoice-history search must refresh its own results region");
  queue.push({ defer: true });
  listSearch.value = "older"; listSearch.oninput();
  await tick(220);
  var stale = deferred[deferred.length - 1];
  listSearch.value = "newer"; listSearch.oninput();
  stale.resolve({items:[],page:1,pageSize:50,totalCount:0});
  await flush();
  assert.ok(listResults.innerHTML.includes("جارٍ تحميل"), "an older result must lose even during the debounce window");
  queue.push({ value: {items:[],page:1,pageSize:50,totalCount:0} });
  await tick(220);
  assert.ok(listResults.innerHTML.includes("لا توجد فواتير مطابقة"));
  assert.ok(requestedUrls[requestedUrls.length - 1].includes("search=newer&page=1&pageSize=50"));
  listBox.node.onclick({target:{closest:function(selector){return selector==="[data-new-sale]"?{}:null;}}});
  assert.strictEqual(salesNavigation.length,1,"New Invoice from sales history must use the registered navigation path");
  assert.strictEqual(salesNavigation[0].route,"pos","New Invoice must open Touch POS directly");
  assert.deepStrictEqual(JSON.parse(JSON.stringify(salesNavigation[0].state)),{newInvoice:true,posCart:null,saleInvoiceId:null,saleLoadedInvoiceId:null},"new invoice navigation must discard cached cart and invoice/view context after the POS guard accepts");
}

async function verifySupplierPagedSearch(){
  var pending=[],calls=[],search={value:""},host={innerHTML:""},count={textContent:""},exportButton={disabled:false};
  var nodes={"#supplierSearch":search,"[data-supplier-results]":host,"[data-supplier-count]":count,"[data-supplier-export]":exportButton};
  var container={innerHTML:"",querySelector:function(selector){return nodes[selector];}};
  var U={escape:function(x){return String(x==null?"":x);},number:String,money:String,debounce:function(fn,delay){var timer;return function(){clearTimeout(timer);timer=setTimeout(fn,delay);};}};
  var UI={pageHeader:function(){return "";},badge:String},store={replace:function(){},find:function(){}};
  var api={request:function(url){calls.push(url);return new Promise(function(resolve){pending.push(resolve);});}};
  var sandbox={window:{AlKhayalUtils:U,AlKhayalUI:UI,AlKhayalServices:{store:store},AlKhayalCalculations:{}},AlKhayalApi:api,localStorage:{},setTimeout:setTimeout,clearTimeout:clearTimeout,console:console};
  vm.runInNewContext(renderersJs,sandbox,{filename:"renderers.js"});
  var context={state:{},isActive:function(){return true;}};
  sandbox.window.AlKhayalRenderers["entity-list"]({data:"suppliers"},container,context);
  search.value="new";search.oninput();await new Promise(function(resolve){setTimeout(resolve,220);});
  assert.strictEqual(nodes["#supplierSearch"],search,"supplier search keeps the input mounted");
  assert.ok(calls[1].indexOf("search=new&page=1")>=0,"supplier search resets the server page");
  pending[1]({items:[{id:2,code:"NEW",name:"new supplier",phone:"1",payable:0,paid:0,status:"نشط"}],totalCount:1,page:1,pageSize:25});await Promise.resolve();
  pending[0]({items:[{id:1,code:"OLD",name:"old supplier",phone:"1",payable:0,paid:0,status:"نشط"}],totalCount:1,page:1,pageSize:25});await Promise.resolve();
  assert.ok(host.innerHTML.indexOf("NEW")>=0&&host.innerHTML.indexOf("OLD")<0,"late supplier results cannot overwrite the newer search");
}

Promise.all([require("./global-search-tests.js")(),require("./settings-cleanup-tests.js")(),require("./catalog-margin-tests.js")(),require("./backup-settings-tests.js")(),require("./customer-language-tests.js")(),require("./shift-localization-tests.js")(),require("./localization-tests.js")(),require("./delivery-tests.js")(),require("./alerts-tests.js")(),require("./touch-pos-invoice-tests.js")(),require("./report-tests.js")(),require("./pricing-fifo-tests.js")(),require("./manual-weight-tests.js")(),require("./payment-methods-tests.js")(),require("./label-tests.js")(),require("./printing-tests.js")(),require("./workforce-tests.js")(),require("./invoice-return-badges-tests.js")(),require("./returns-tests.js")(),require("./expense-tests.js")(),require("./customer-statement-tests.js")(),require("./installment-sales-tests.js")(),require("./supplier-accounts-tests.js")(),require("./purchasing-tests.js")(),require("./page-snapshot-tests.js")(),require("./treasury-tests.js")(),require("./inventory-tests.js")(),require("./operation-log-tests.js")(),require("./keyboard-shortcuts-tests.js")(),require("./workspace-tabs-tests.js")(),require("./dashboard-tests.js")(),verifyFeatureStateLifecycle(),verifyAccessStateLifecycle(),verifySettingsStateLifecycle(),verifyCustomerManagementCreateAndEdit(),verifyEntityListSearchKeepsFocus(),verifySalesSearchMountedInput(),verifySearchInputFocusSurvival(),verifySupplierPagedSearch()]).then(function () { return require("./product-units-tests.js")(); }).then(function () { console.log("Frontend registry, settings/feature/access lifecycle, user management, customer management, entity-list and settings/catalog search focus, and calculation tests passed (" + routes.length + " screens)."); }).catch(function (error) { console.error(error); process.exitCode = 1; });

require("./numeric-format-tests.js");
require("./design-system-tests.js");
