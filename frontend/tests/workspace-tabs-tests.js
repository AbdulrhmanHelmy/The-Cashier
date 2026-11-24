"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");

module.exports = function () {
  var appJs = fs.readFileSync(path.join(__dirname, "..", "js", "app.js"), "utf8");
  assert.ok(appJs.indexOf('var HOME_ROUTE = "dashboard"') >= 0, "Home must have an explicit stable route");
  assert.ok(appJs.indexOf("function ensureHomeTab()") >= 0 && appJs.indexOf("tabs.unshift({ route: HOME_ROUTE") >= 0, "Home must be restored as a permanent tab");
  assert.ok(appJs.indexOf("route === HOME_ROUTE) return") >= 0, "closeTab must reject every attempt to close Home");
  assert.ok(appJs.indexOf('tab.route !== HOME_ROUTE ? \'<button class="tab-close"') >= 0, "Home must not render a close control");
  assert.ok(appJs.indexOf("ensureHomeTab();") >= 0 && appJs.indexOf("activeRoute = tabs.length ? tabs[Math.max(0, index - 1)].route : null") >= 0, "closing the final tab selects Home when authorized, or clears the workspace");
  assert.ok(appJs.indexOf("var initial=HOME_ROUTE") >= 0, "Home must be the post-login default");
  assert.ok(appJs.indexOf("if (!activeRoute) { workspace.innerHTML") >= 0, "the existing empty-state fallback must remain available for permission failures");
  assert.ok(appJs.indexOf('existing.state=Object.assign({},route==="pos"&&existing.state.posCart?{posCart:existing.state.posCart}:{},routeState)') >= 0 && appJs.indexOf("existing.view=null") >= 0, "route state must replace an already-open tab view and clear stale contextual ids");
  assert.ok(appJs.indexOf("existing.view.beforeRouteReplace") >= 0 && appJs.indexOf("closing.view.beforeRouteReplace") >= 0, "replacing or closing a mounted editor must honor its unsaved-change guard");
  assert.ok(appJs.indexOf('tab.view.beforeRouteReplace(tab.state)') >= 0, "manual data refresh must honor the same unsaved-change guard");
  // Execute the production navigation function for a previously mounted POS tab.
  var cart={lines:[{quantity:"2"}]},allowed=false,view={beforeRouteReplace:function(){return allowed;}},tab={route:"pos",state:{posCart:cart,saleLoadedInvoiceId:21},view:view,initialized:true};
  var sandbox={tabs:[tab],screensByRoute:{pos:{title:"الكاشير"}},resolveRoute:function(r){return r;},routeEnabled:function(){return true;},UI:{toast:function(){throw new Error("unexpected navigation error");}},activeRoute:"sales-list",restoringNavigation:false,navigationHistory:[],activateTab:function(){},localStorage:{setItem:function(){}}};
  vm.runInNewContext(appJs.match(/  function navigate\(route, routeState\) \{[^\n]+/)[0],sandbox);
  sandbox.navigate("pos",{saleInvoiceId:22});assert.strictEqual(tab.view,view,"declined replacement preserves mounted DOM");assert.equal(tab.state.saleLoadedInvoiceId,21);
  allowed=true;sandbox.navigate("pos",{saleInvoiceId:22});
  assert.strictEqual(tab.state.posCart,cart,"contextual navigation retains suspended new cart");assert.equal(tab.state.saleInvoiceId,22);assert.equal(tab.state.saleLoadedInvoiceId,undefined,"old selected ID cannot leak into next request");assert.equal(tab.view,null);
  sandbox.navigate("pos",{saleInvoiceId:23});assert.equal(tab.state.saleInvoiceId,23);assert.strictEqual(tab.state.posCart,cart);

  var mounted = { route: "sales-list", state: { filter: "active" }, scrollTop: 80, view: {}, initialized: true };
  var activated = [], sidebar = { tabs: [mounted], screensByRoute: { "sales-list": { title: "المبيعات" } }, resolveRoute: function (r) { return r; }, routeEnabled: function () { return true; },
    activeRoute: "pos", restoringNavigation: false, navigationHistory: [], activateTab: function (route) { activated.push(route); sidebar.activeRoute = route; } };
  vm.runInNewContext(appJs.match(/  function navigate\(route, routeState\) \{[^\n]+/)[0] + "\n" + appJs.match(/  function navigateFromSidebar\(route\) \{[^\n]+/)[0], sidebar);
  sidebar.navigateFromSidebar("sales-list");
  sidebar.navigateFromSidebar("sales-list");
  assert.deepStrictEqual(activated, ["sales-list"], "sidebar focuses the existing workspace without remounting the active page");
  assert.deepStrictEqual(sidebar.tabs, [mounted]);
  assert.equal(mounted.state.filter, "active");
  assert.equal(mounted.scrollTop, 80);

  console.log("Workspace tab regression tests passed.");
};
