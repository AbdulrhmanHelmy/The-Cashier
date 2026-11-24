"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");

module.exports = function () {
  var root = path.join(__dirname, ".."), source = fs.readFileSync(path.join(root, "js/modules/dashboard.js"), "utf8"), css = fs.readFileSync(path.join(root, "css/home.css"), "utf8"), app = fs.readFileSync(path.join(root, "js/app.js"), "utf8");
  var registry = JSON.parse(fs.readFileSync(path.join(root, "data/screens.json"), "utf8"));
  var events = {}, requests = 0, navigated = [], modalOptions, alertCount = 2, badge = { hidden: false, textContent: "", setAttribute: function (key, value) { this[key] = value; } }, modalList = { innerHTML: "", onclick: null };
  var modalInput = { value: "", oninput: null, focus: function () {} };
  var sandbox = { window: {
    addEventListener: function (name, fn) { (events[name] = events[name] || []).push(fn); },
    AlKhayalRenderers: {},
    AlKhayalUtils: { escape: function (value) { return String(value).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;"); }, normalize: function (value) { return String(value).toLowerCase(); } },
    AlKhayalUI: { modal: function (options) { modalOptions = options; options.onOpen({ querySelector: function (selector) { return selector.indexOf("search") >= 0 ? modalInput : modalList; } }); }, closeModal: function () {} },
    AlKhayalApi: { request: function () { requests++; return Promise.resolve({}); } },
    AlKhayalAlerts: { count: function () { return alertCount; } }
  }, Intl: Intl, Date: Date };
  vm.runInNewContext(source, sandbox, { filename: "dashboard.js" });
  var render = sandbox.window.AlKhayalRenderers.dashboard;
  var host = { innerHTML: "", onclick: null, querySelector: function (selector) { return selector === "[data-home-alert-count]" && this.innerHTML.indexOf("data-home-alert-count") >= 0 ? badge : null; } };
  var allowed = { dashboard: true, pos: true, "sales-management": true, "purchase-management": true, "products-list": true, "warehouses-list": true, "customers-list": true, "report-overview": true, "settings-program": true, "delivery-orders": true, "alerts-list": true };
  var context = { registry: registry, user: { displayName: "<مدير>", role: "administrator" }, canNavigate: function (route) { return !!allowed[route]; }, navigate: function (route) { navigated.push(route); } };
  function clickRoute(route) {
    host.onclick({ target: { closest: function (selector) { return selector === "[data-dashboard-route]" ? { getAttribute: function () { return route; } } : null; } } });
  }
  render({ route: "dashboard" }, host, context);
  assert.ok(host.innerHTML.indexOf('assets/the-cashier-mark.png') >= 0 && host.innerHTML.indexOf('assets/the-cashier-wordmark.png') >= 0 && host.innerHTML.indexOf("نظام نقاط البيع وإدارة الأعمال") >= 0, "official emblem, uncropped wordmark derivative and exact identity copy render");
  assert.ok(host.innerHTML.indexOf("&lt;مدير&gt;") < 0 && host.innerHTML.indexOf("<مدير>") < 0 && host.innerHTML.indexOf("مسؤول النظام") < 0 && host.innerHTML.indexOf("home-welcome") < 0, "home omits user welcome card and account details");
  context.user.branchName = "فرع القاهرة";
  host.rerenderHome();
  assert.ok(host.innerHTML.indexOf("فرع القاهرة") < 0, "home omits branch even when supplied by session state");
  delete context.user.branchName;
  host.rerenderHome();
  assert.ok(host.innerHTML.indexOf('data-dashboard-route="pos"') >= 0 && host.innerHTML.indexOf('data-dashboard-route="sales-management"') >= 0 && host.innerHTML.indexOf('data-dashboard-route="purchase-management"') >= 0, "primary actions use canonical routes");
  assert.ok(host.innerHTML.indexOf('data-dashboard-route="report-overview"') >= 0 && host.innerHTML.indexOf('data-dashboard-route="settings-program"') >= 0, "admin shortcuts are visible");
  assert.ok(host.innerHTML.indexOf('data-home-alert-count') >= 0 && host.innerHTML.indexOf('>2</span>') >= 0, "home uses the shared alert count");
  alertCount = 1;
  (events["alkhayal:alerts-changed"] || []).forEach(function (listener) { listener(); });
  assert.strictEqual(badge.textContent, "1", "home badge updates when the shared alert count changes");
  alertCount = 0;
  (events["alkhayal:alerts-changed"] || []).forEach(function (listener) { listener(); });
  assert.strictEqual(badge.hidden, true, "zero alert count hides the badge");
  assert.ok(host.innerHTML.indexOf('data-dashboard-route="manufacturing"') < 0 && host.innerHTML.indexOf('data-dashboard-route="maintenance-intake"') < 0, "hidden and unavailable module routes do not appear");
  allowed["maintenance-intake"] = true;
  (events["alkhayal:features-changed"] || []).forEach(function (listener) { listener(); });
  assert.ok(host.innerHTML.indexOf('data-dashboard-route="maintenance-intake"') >= 0, "enabled accessible maintenance appears");
  allowed["maintenance-intake"] = false;
  (events["alkhayal:features-changed"] || []).forEach(function (listener) { listener(); });
  assert.ok(host.innerHTML.indexOf('data-dashboard-route="maintenance-intake"') < 0, "disabled maintenance disappears");
  assert.ok(host.innerHTML.indexOf("dashboard-kpi") < 0 && host.innerHTML.indexOf("آخر فواتير") < 0 && requests === 0, "home no longer renders or requests dashboard statistics");
  clickRoute("pos"); clickRoute("pos");
  assert.deepStrictEqual(navigated, ["pos", "pos"], "home uses tab-preserving navigation for existing POS tab");
  allowed.pos = false; allowed["report-overview"] = false; allowed["settings-program"] = false; allowed["delivery-orders"] = false; allowed["alerts-list"] = false;
  context.user.role = "cashier";
  (events["alkhayal:access-changed"] || []).forEach(function (listener) { listener(); });
  assert.ok(host.innerHTML.indexOf("كاشير") < 0 && host.innerHTML.indexOf('data-dashboard-route="pos"') < 0 && host.innerHTML.indexOf('data-dashboard-route="report-overview"') < 0 && host.innerHTML.indexOf('data-dashboard-route="settings-program"') < 0 && host.innerHTML.indexOf('data-home-alert-count') < 0, "cashier grant changes remove inaccessible cards and badge");
  clickRoute("pos");
  assert.deepStrictEqual(navigated, ["pos", "pos"], "stale inaccessible button cannot navigate");
  host.onclick({ target: { closest: function (selector) { return selector === "[data-dashboard-all]" ? {} : null; } } });
  assert.ok(modalOptions && modalList.innerHTML.indexOf('data-dashboard-menu-route="report-overview"') < 0 && modalList.innerHTML.indexOf('data-dashboard-menu-route="manufacturing"') < 0, "all screens picker respects access and hidden routes");
  allowed["report-overview"] = true;
  (events["alkhayal:features-changed"] || []).forEach(function (listener) { listener(); });
  assert.ok(host.innerHTML.indexOf('data-dashboard-route="report-overview"') >= 0, "feature updates refresh the mounted home");
  assert.ok(css.indexOf("home-shortcut-grid-left") >= 0 && css.indexOf("home-shortcut-grid-right") >= 0 && css.indexOf("prefers-reduced-motion: reduce") >= 0 && css.indexOf("var(--tc-bg-surface)") >= 0, "balanced shortcut panels, motion and dark theme styles exist");
  assert.ok(css.indexOf("100vh") < 0 && css.indexOf(".workspace.home-active { overflow: hidden") >= 0 && css.indexOf("pointer-events: none") >= 0, "home fills the existing workspace without a second scroll owner");
  assert.ok(css.indexOf(".home-wordmark img { display: block; width: 100%; height: auto; object-fit: contain;") >= 0 && css.indexOf("var(--tc-home-frame)") >= 0 && css.indexOf(".home-emblem") >= 0, "wordmark uses intrinsic ratio without an offset crop and frame is scalable");
  assert.ok(host.innerHTML.indexOf("الوصول السريع") < 0, "old heading is absent");
  assert.ok(host.innerHTML.indexOf("تحديث البيانات") < 0 && app.indexOf('if (route === HOME_ROUTE) { if (fallback) fallback.remove(); return; }') >= 0 && app.indexOf('tab.route !== HOME_ROUTE') >= 0, "home has no refresh control or stale data prompt");
  console.log("Dashboard home regression tests passed.");
};
