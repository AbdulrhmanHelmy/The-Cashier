(function (global) {
  "use strict";
  var snapshot = { role: "", screenIds: [], canEditSalePrices: false, canViewPosStockQuantities: false }, loadPromise = null;
  function signature(value) { return (value.screenIds || []).slice().sort().join("|") + ":" + (value.role || "") + ":" + !!value.canEditSalePrices + ":" + !!value.canViewPosStockQuantities; }
  function setSnapshot(value, options) { var next = value || { role: "", screenIds: [] }; next.canEditSalePrices = next.role === "administrator" || !!next.canEditSalePrices; next.canViewPosStockQuantities = next.role === "administrator" || !!next.canViewPosStockQuantities; var changed = signature(snapshot) !== signature(next); snapshot = next; if (changed && (!options || options.notify !== false)) global.dispatchEvent(new CustomEvent("alkhayal:access-changed", { detail: snapshot })); return snapshot; }
  function load(options) { if (loadPromise) return loadPromise; loadPromise = global.AlKhayalApi.request("/api/auth/me/screen-access").then(function (value) { return setSnapshot(value, options); }); loadPromise = loadPromise.then(function (value) { loadPromise = null; return value; }, function (reason) { loadPromise = null; throw reason; }); return loadPromise; }
  function routeEnabled(screen) { return !!screen && (snapshot.screenIds.indexOf(screen.permissionRoute || screen.route) >= 0 || screen.route === "alerts-list" && snapshot.screenIds.some(function(id){return id === "reorder-alert" || id === "low-stock-alert";})); }
  if (global.addEventListener) {
    global.addEventListener("focus", function () { load().catch(function () {}); });
    global.addEventListener("pageshow", function () { load().catch(function () {}); });
  }
  global.AlKhayalAccess = { load: load, current: function () { return snapshot; }, refresh: function () { loadPromise = null; return load(); }, routeEnabled: routeEnabled, setSnapshot: setSnapshot };
}(window));
