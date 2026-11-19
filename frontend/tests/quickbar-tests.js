"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");

module.exports = function () {
  var root = path.resolve(__dirname, ".."), registry = JSON.parse(fs.readFileSync(path.join(root, "data", "screens.json"), "utf8"));
  var app = fs.readFileSync(path.join(root, "js", "app.js"), "utf8");
  var css = fs.readFileSync(path.join(root, "css", "layout.css"), "utf8");
  var html = fs.readFileSync(path.join(root, "index.html"), "utf8");
  var context = { window: {} };
  vm.runInNewContext(fs.readFileSync(path.join(root, "js", "components", "quickbar-layout.js"), "utf8"), context);
  var fit = context.window.AlKhayalQuickbarLayout.visibleCount;
  assert.strictEqual(fit([130, 150, 105, 120], 520, 80, 4), 4, "wide rows show every permitted shortcut without More");
  assert.strictEqual(fit([130, 150, 105, 120], 400, 80, 4), 2, "lower priority shortcuts move to More in order");
  assert.strictEqual(fit([130, 150, 105, 120], 220, 80, 4), 1, "the cashier stays direct when it fits");
  assert.strictEqual(fit([130, 150, 105, 120], 80, 80, 4), 0, "all actions remain in More when space is extremely narrow");
  assert.strictEqual(fit([], 80, 80, 4), 0, "no permitted actions leave no overflow control");
  assert.strictEqual(new Set(registry.quickAccess.map(function (item) { return item.route; })).size, registry.quickAccess.length, "shortcuts have unique routes");
  registry.quickAccess.forEach(function (item) {
    var screen = registry.screens.find(function (entry) { return entry.route === item.route; });
    assert.ok(screen && screen.implemented && !screen.redirectTo, "shortcut uses an implemented destination: " + item.route);
  });
  var state = { window: { dispatchEvent: function () {} }, CustomEvent: function () {} };
  vm.runInNewContext(fs.readFileSync(path.join(root, "js", "services", "access-state.js"), "utf8"), state);
  vm.runInNewContext(fs.readFileSync(path.join(root, "js", "services", "feature-state.js"), "utf8"), state);
  var access = state.window.AlKhayalAccess, features = state.window.AlKhayalFeatures;
  function permitted() { return registry.quickAccess.filter(function (item) { var screen = registry.screens.find(function (entry) { return entry.route === item.route; }); return access.routeEnabled(screen) && features.routeEnabled(screen); }).map(function (item) { return item.route; }); }
  access.setSnapshot({ role: "cashier", screenIds: ["pos", "product-quick-add", "purchase-management", "alerts-list"] }, { notify: false });
  features.setSnapshot({ modules: [{ key: "product-catalog", isEnabled: true }, { key: "basic-inventory", isEnabled: true }], capabilities: [] }, { notify: false });
  assert.deepStrictEqual(permitted(), ["pos", "product-quick-add", "purchase-management", "alerts-list"], "cashier sees only granted shortcuts in priority order");
  features.setSnapshot({ modules: [{ key: "product-catalog", isEnabled: false }, { key: "basic-inventory", isEnabled: true }], capabilities: [] }, { notify: false });
  assert.deepStrictEqual(permitted(), ["pos", "purchase-management", "alerts-list"], "disabled module removes its shortcut without a placeholder");
  access.setSnapshot({ role: "administrator", screenIds: registry.quickAccess.map(function (item) { return item.route; }) }, { notify: false });
  features.setSnapshot({ modules: [{ key: "product-catalog", isEnabled: true }, { key: "basic-inventory", isEnabled: true }], capabilities: [] }, { notify: false });
  assert.strictEqual(permitted().length, registry.quickAccess.length, "all authorized shortcuts are available to administrators");
  assert.ok(html.indexOf('js/components/quickbar-layout.js') >= 0 && html.indexOf('js/components/quickbar-layout.js') < html.indexOf('js/app.js'), "layout helper loads before the shell");
  assert.ok(app.indexOf('localStorage.removeItem("alkhayal_quickbar_collapsed")') >= 0 && app.indexOf("data-quickbar-toggle") < 0, "old hidden preference is cleared and no hide control is rendered");
  assert.ok(app.indexOf('registry.quickAccess.filter(function (item) { return routeEnabled(screensByRoute[item.route]); })') >= 0, "both direct and overflow actions use effective route access");
  assert.ok(app.indexOf('new ResizeObserver(layoutQuickbar)') >= 0 && app.indexOf('row.clientWidth') >= 0, "overflow responds to the actual toolbar width");
  assert.ok(app.indexOf('menu.innerHTML = registry.quickAccess.filter') >= 0 && app.indexOf('.slice(count)') >= 0, "overflow contains only the remaining permitted shortcuts");
  assert.ok(app.indexOf('if (tile) { closeQuickbarMenu(false); navigateFromShortcut(tile.getAttribute("data-quick-route")); }') >= 0 && app.indexOf("function navigateFromShortcut(route, routeState)") >= 0, "quick access refreshes an open page through the shared freshness-aware navigation path");
  assert.ok(app.indexOf('updateQuickbarActive();') >= 0 && app.indexOf('aria-current') >= 0, "active routes are marked in direct and overflow placements");
  assert.ok(app.indexOf('event.key === "Escape"') >= 0 && app.indexOf('event.target.closest("#quickbarMore")') >= 0, "Escape and outside click close More");
  assert.ok(css.indexOf('.quick-tile[hidden]') >= 0 && css.indexOf('.quickbar-menu[hidden]') >= 0 && !/\.quickbar-tiles[^}]*overflow-x:\s*auto/.test(css), "hidden duplicates are inaccessible and the toolbar does not scroll horizontally");
  console.log("Quick-access priority, overflow, route, access, and interaction checks passed.");
};
