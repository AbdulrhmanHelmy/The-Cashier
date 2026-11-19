"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");

module.exports = function () {
  var appJs = fs.readFileSync(path.join(__dirname, "..", "js", "app.js"), "utf8");
  assert.ok(appJs.indexOf('var HOME_ROUTE = "dashboard"') >= 0, "Home must have an explicit stable route");
  assert.ok(appJs.indexOf("function ensureHomeTab()") >= 0 && appJs.indexOf("tabs.unshift({ route: HOME_ROUTE") >= 0, "Home must be restored as a permanent tab");
  assert.ok(appJs.indexOf("route === HOME_ROUTE) return") >= 0, "closeTab must reject every attempt to close Home");
  assert.ok(appJs.indexOf('tab.route !== HOME_ROUTE ? \'<span class="tab-close"') >= 0, "Home must not render a close control");
  assert.ok(appJs.indexOf("ensureHomeTab(); if (activeRoute === route)") >= 0 && appJs.indexOf("activeRoute = tabs.length ? tabs[Math.max(0, index - 1)].route : HOME_ROUTE") >= 0, "closing the final non-Home tab must activate Home");
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

  // Sidebar navigation refreshes an existing tab by removing its mounted view before navigating again.
  var mounted={route:"sales-list",title:"دليل المنتجات",state:{filter:"active"},scrollTop:80,view:{beforeRouteReplace:function(){return true;}},initialized:true},sidebarCalls=[],sidebarTabs=[mounted],sidebarSandbox={HOME_ROUTE:"dashboard",tabs:sidebarTabs,screensByRoute:{"sales-list":{title:"دليل المنتجات"}},resolveRoute:function(r){return r;},routeEnabled:function(){return true;},activeRoute:"sales-list",workspace:{scrollTop:80},saveActiveScroll:function(){mounted.scrollTop=80;},navigate:function(route){sidebarCalls.push({route:route,oldTabStillMounted:sidebarTabs.indexOf(mounted)>=0});sidebarTabs.push({route:route,title:"دليل المنتجات",state:{},scrollTop:0});}};
  vm.runInNewContext(appJs.match(/  function navigateFromSidebar\(route\) \{[^\n]+/)[0],sidebarSandbox);
  sidebarSandbox.navigateFromSidebar("sales-list");assert.deepStrictEqual(sidebarCalls,[{route:"sales-list",oldTabStillMounted:false}],"reselecting an open sidebar page must replace the mounted tab before navigation");assert.equal(sidebarSandbox.tabs.length,1);assert.equal(sidebarSandbox.tabs[0].route,"sales-list");

  var declined={route:"sales-list",state:{},view:{beforeRouteReplace:function(){return false;}}},declinedCalls=[],declinedSandbox={HOME_ROUTE:"dashboard",tabs:[declined],screensByRoute:{"sales-list":{title:"دليل المنتجات"}},resolveRoute:function(r){return r;},routeEnabled:function(){return true;},activeRoute:"sales-list",navigate:function(route){declinedCalls.push(route);}};
  vm.runInNewContext(appJs.match(/  function navigateFromSidebar\(route\) \{[^\n]+/)[0],declinedSandbox);
  declinedSandbox.navigateFromSidebar("sales-list");assert.deepStrictEqual(declinedCalls,[],"sidebar refresh must respect a mounted editor's unsaved-change guard");assert.strictEqual(declinedSandbox.tabs[0],declined);
  console.log("Workspace tab regression tests passed.");
};
