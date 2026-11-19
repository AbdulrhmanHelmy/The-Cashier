"use strict";

var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");

module.exports = async function () {
  var root = path.resolve(__dirname, "..", ".."), registry = JSON.parse(fs.readFileSync(path.join(root, "frontend", "data", "screens.json"), "utf8"));
  function screen(route) { return registry.screens.find(function (item) { return item.route === route; }); }

  ["product-categories", "unit-add"].forEach(function (route) {
    assert.ok(screen(route).implemented, route + " must remain an implemented catalog-management route");
    assert.ok(!screen(route).settingsGroup, route + " must not be duplicated in the Settings hub");
  });
  assert.strictEqual(screen("product-categories").renderer, "catalog-categories");
  assert.strictEqual(screen("unit-add").renderer, "catalog-units");
  assert.ok(screen("warehouse-settings").navigationHidden && screen("warehouse-settings").redirectTo === "warehouses-list", "the obsolete warehouse Settings route must be a hidden compatibility redirect");

  var featureSource = fs.readFileSync(path.join(root, "frontend", "js", "modules", "feature-management.js"), "utf8"), settingsSource = fs.readFileSync(path.join(root, "frontend", "js", "modules", "settings-hub.js"), "utf8");
  var snapshot = {
    modules: [
      feature("sales", "المبيعات", "module", true), feature("maintenance-management", "إدارة الصيانة", "module"),
      feature("installments", "البيع بالتقسيط", "module"), feature("multiple-warehouses", "المخازن المتعددة", "module"),
      feature("employee-management", "إدارة الموظفين", "module"), feature("employee-attendance", "حضور الموظفين", "module", false, ["employee-management"]),
      feature("advanced-shifts", "إدارة الورديات المتقدمة", "module"), feature("barcode-label-printing", "طباعة ملصقات الباركود", "module"),
      feature("manufacturing", "التصنيع", "module"), feature("multiple-branches", "الفروع المتعددة", "module"),
      feature("cash-services", "خدمات الكاش", "module"), feature("advanced-reports", "التقارير المتقدمة", "module")
    ],
    capabilities: [
      feature("product-variants", "متغيرات المنتج", "capability"), feature("serial-numbers", "الأرقام التسلسلية", "capability"),
      feature("imei-tracking", "تتبع IMEI", "capability", false, ["serial-numbers"]), feature("expiry-dates", "تواريخ الصلاحية", "capability"),
      feature("batch-tracking", "تتبع داخلي للمخزون", "capability"), feature("wholesale-pricing", "أسعار الجملة", "capability")
    ]
  };
  function feature(key, name, kind, core, dependencies) { return { key: key, arabicName: name, description: name, kind: kind, isCore: !!core, isEnabled: true, source: core ? "core" : "administrator", isRecommended: false, dependencies: dependencies || [] }; }
  function container() { return { innerHTML: "", querySelector: function () { return null; }, querySelectorAll: function () { return []; } }; }

  var context = { window: {}, console: console, setTimeout: setTimeout, clearTimeout: clearTimeout };
  context.window.window = context.window;
  context.window.AlKhayalRenderers = {};
  context.window.AlKhayalUtils = { escape: function (value) { return String(value == null ? "" : value); }, normalize: function (value) { return String(value || "").toLowerCase(); } };
  context.window.AlKhayalUI = { pageHeader: function () { return ""; }, toast: function () {}, confirm: function () { return Promise.resolve(false); } };
  context.window.AlKhayalApi = { request: function () { return Promise.reject(new Error("unexpected request")); } };
  context.window.AlKhayalFeatures = { load: function () { return Promise.resolve(snapshot); }, setSnapshot: function () {}, routeEnabled: function () { return true; }, find: function (key) { return snapshot.modules.concat(snapshot.capabilities).find(function (item) { return item.key === key; }); } };
  vm.runInNewContext(featureSource, context, { filename: "feature-management.js" });
  vm.runInNewContext(settingsSource, context, { filename: "settings-hub.js" });

  var featureHost = container();
  context.window.AlKhayalRenderers["feature-management"](screen("feature-management"), featureHost, { user: { role: "administrator" }, isActive: function () { return true; } });
  await Promise.resolve(); await Promise.resolve();
  assert.ok(featureHost.innerHTML.includes("الوحدات الاختيارية") && featureHost.innerHTML.includes("خصائص المنتجات"), "feature management must render its two administrator-facing groups");
  assert.ok(!featureHost.innerHTML.includes("الوحدات الأساسية") && !featureHost.innerHTML.includes("مفعلة دائمًا") && !featureHost.innerHTML.includes("المبيعات"), "core modules must remain internal and absent from the page");
  ["إدارة الصيانة", "البيع بالتقسيط", "المخازن المتعددة", "إدارة الموظفين", "حضور الموظفين", "إدارة الورديات المتقدمة", "طباعة ملصقات الباركود"].forEach(function (name) { assert.ok(featureHost.innerHTML.includes(name), "implemented optional module must remain visible: " + name); });
  ["التصنيع", "الفروع المتعددة", "خدمات الكاش", "التقارير المتقدمة"].forEach(function (name) { assert.ok(!featureHost.innerHTML.includes(name), "unsupported optional module must stay hidden: " + name); });
  ["متغيرات المنتج", "الأرقام التسلسلية", "تتبع IMEI", "تواريخ الصلاحية"].forEach(function (name) { assert.ok(featureHost.innerHTML.includes(name), "supported product property must remain visible: " + name); });
  ["تتبع داخلي للمخزون", "أسعار الجملة"].forEach(function (name) { assert.ok(!featureHost.innerHTML.includes(name), "legacy or disconnected property must stay hidden: " + name); });

  var settingsHost = container();
  context.window.AlKhayalRenderers["settings-hub"](screen("settings-program"), settingsHost, { registry: registry, user: { role: "administrator" }, canNavigate: function () { return true; }, navigate: function () {} });
  assert.ok(!settingsHost.innerHTML.includes("تصنيفات المنتجات") && !settingsHost.innerHTML.includes("وحدات القياس") && !settingsHost.innerHTML.includes("إعدادات المخزن"), "duplicate and obsolete cards must be absent from the Settings hub");
  assert.ok(settingsHost.innerHTML.includes("الإعدادات العامة") && settingsHost.innerHTML.includes("الطابعات والطباعة") && settingsHost.innerHTML.includes("المستخدمون والصلاحيات"), "operational Settings cards must remain available");

  console.log("Settings cleanup and administrator-facing feature registry tests passed.");
};
