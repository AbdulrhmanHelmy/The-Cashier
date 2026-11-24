"use strict";
var assert=require("assert"),fs=require("fs"),path=require("path"),vm=require("vm");
var root=path.join(__dirname,"..");
module.exports=async function(){
  var registry=JSON.parse(fs.readFileSync(path.join(root,"data/screens.json"),"utf8"));
  assert.deepStrictEqual(registry.modules.find(function(x){return x.id==="alerts";}).items,["alerts-list"]);
  ["reorder-alert","low-stock-alert"].forEach(function(route){var s=registry.screens.find(function(x){return x.route===route;});assert.ok(s.navigationHidden);assert.equal(s.redirectTo,"alerts-list");});
  var listeners={},events=[],calls=[],pending=[],permissions=["alerts-list"];
  var win={AlKhayalApi:{request:function(url){calls.push(url);return new Promise(function(resolve,reject){pending.push({resolve:resolve,reject:reject});});}},
    AlKhayalAccess:{current:function(){return{screenIds:permissions};},routeEnabled:function(){return permissions.some(function(x){return ["alerts-list","reorder-alert","low-stock-alert"].indexOf(x)>=0;});}},
    addEventListener:function(name,cb){(listeners[name]||(listeners[name]=[])).push(cb);},dispatchEvent:function(event){events.push(event.type);(listeners[event.type]||[]).forEach(function(cb){cb(event);});},setTimeout:setTimeout,clearTimeout:clearTimeout};
  var context={window:win,CustomEvent:function(type,options){this.type=type;this.detail=options&&options.detail;}};
  vm.runInNewContext(fs.readFileSync(path.join(root,"js/services/alerts-state.js"),"utf8"),context);
  var A=win.AlKhayalAlerts,first=A.load(),second=A.load();assert.strictEqual(first,second);assert.equal(calls.length,1);
  pending.shift().resolve([{id:"stock:1",type:"low-stock",productName:"سكر",totalQuantity:"10",unit:"قطعة",reorderPoint:"10"}]);await first;
  assert.equal(A.count(),1);assert.equal(A.rows().length,1);await A.load();assert.equal(calls.length,1,"cached reads make no request");
  // Badge is rendered on the dedicated header notification button and follows the unified count.
  var app=fs.readFileSync(path.join(root,"js/app.js"),"utf8"),badge={setAttribute:function(k,v){this[k]=v;}},alertButton={setAttribute:function(k,v){this[k]=v;}};
  assert.ok(app.indexOf('data-alert-count')>=0);
  vm.runInNewContext(app.slice(app.indexOf('  function updateAlertBadge()'),app.indexOf('  window.addEventListener("alkhayal:alerts-changed"'))+'updateAlertBadge();',{document:{querySelectorAll:function(){return [badge];},getElementById:function(){return alertButton;}},window:win});
  assert.equal(badge.textContent,"1");assert.equal(badge.hidden,false);assert.ok(alertButton["aria-label"].includes("1"),"bell accessible name includes the unread count");
  function emit(type,detail){win.dispatchEvent({type:type,detail:detail});}
  ["/api/settings/company","/api/sales/drafts","/api/sales/1","/api/sales/1/payments","/api/catalog/categories","/api/inventory/warehouses"].forEach(function(path){emit("alkhayal:data-changed",{path:path});});await new Promise(function(r){setTimeout(r,5);});assert.equal(calls.length,1,"unrelated writes do not query alerts");
  emit("alkhayal:data-changed",{path:"/api/inventory/post"});emit("alkhayal:data-changed",{path:"/api/sales/1/post"});await new Promise(function(r){setTimeout(r,5);});assert.equal(calls.length,2,"same tick mutations coalesce");
  pending.shift().resolve([]);await A.load();assert.equal(A.rows().length,0);
  A.invalidate();await new Promise(function(r){setTimeout(r,5);});  pending.shift().resolve([{id:"stock:1",type:"low-stock",title:"انتهى المخزون",detail:"0",severity:"خطر",read:false}]);await A.load();assert.equal(A.count(),1,"resolved and returning stock alert is unread again");
  var marked=A.markAll();assert.equal(calls[calls.length-1],"/api/alerts/read-all");pending.shift().resolve(null);await new Promise(function(r){setTimeout(r,5);});pending.shift().resolve([{id:"stock:1",type:"low-stock",read:true}]);await marked;assert.equal(A.count(),0,"mark all reloads persisted state before settling");
  vm.runInNewContext(app.slice(app.indexOf('  function updateAlertBadge()'),app.indexOf('  window.addEventListener("alkhayal:alerts-changed"'))+'updateAlertBadge();',{document:{querySelectorAll:function(){return [badge];},getElementById:function(){return alertButton;}},window:win});assert.equal(badge.hidden,true,"badge hides when every current alert is read");assert.equal(alertButton["aria-label"],"التنبيهات");
  permissions=["reorder-alert"];emit("alkhayal:access-changed");await new Promise(function(r){setTimeout(r,5);});pending.shift().resolve([{id:"stock:2",type:"low-stock",productName:"صنف",totalQuantity:"0",unit:"قطعة",reorderPoint:"1"}]);await A.load();
  assert.equal(A.rows().length,1,"legacy stock-only grant cannot expose expiry or other notifications");
  permissions=[];emit("alkhayal:access-changed");await new Promise(function(r){setTimeout(r,5);});assert.equal(A.rows().length,0);
  // Access changes during an outstanding request must discard the old payload.
  permissions=["alerts-list"];emit("alkhayal:access-changed");await new Promise(function(r){setTimeout(r,5);});var old=pending.shift();permissions=[];emit("alkhayal:access-changed");old.resolve([{id:"stock:secret"}]);await A.load();assert.equal(A.count(),0);
  // The filter updates only its results host and preserves the mounted input.
  var nodes={"#alertType":{value:""},"[data-alert-status]":{textContent:""},"[data-alert-rows]":{innerHTML:""}},writes=0;
  var box={querySelector:function(s){return nodes[s];},set innerHTML(x){writes++;this.html=x;}};
  win.AlKhayalUtils={escape:function(x){return String(x||"");},dateTime:String};win.AlKhayalUI={pageHeader:function(){return "";},badge:String};
  var categoryRows=[{id:"1",type:"low-stock",title:"zero",detail:"low-stock-detail",read:false,relatedRoute:"product-edit",relatedId:11},{id:"2",type:"reorder",title:"reorder",detail:"reorder-detail",read:false},{id:"3",type:"expiry",title:"expiry",detail:"expiry-detail",read:false},{id:"4",type:"installment",title:"installment",detail:"installment-detail",read:false},{id:"5",type:"other",title:"other",detail:"other-detail",read:false}],navigated=null;
  win.AlKhayalAlerts={types:{"low-stock":"نقص",expiry:"صلاحية",reorder:"طلب",installment:"أقساط",other:"أخرى"},rows:function(){return categoryRows;},error:function(){return null;},loading:function(){return false;}};
  vm.runInNewContext(fs.readFileSync(path.join(root,"js/modules/alerts.js"),"utf8"),{window:win});win.AlKhayalAlertsPage({},box,{state:{},canNavigate:function(){return true;},navigate:function(route,state){navigated={route:route,state:state};}});
  Object.keys(win.AlKhayalAlerts.types).forEach(function(type){nodes["#alertType"].value=type;nodes["#alertType"].onchange();assert.ok(nodes["[data-alert-rows]"].innerHTML.indexOf(type+"-detail")>=0,type+" filter includes its category");categoryRows.filter(function(row){return row.type!==type;}).forEach(function(row){assert.equal(nodes["[data-alert-rows]"].innerHTML.indexOf(row.detail),-1,type+" filter excludes "+row.type);});});
  nodes["#alertType"].value="low-stock";nodes["#alertType"].onchange();var item={getAttribute:function(){return "1";}};box.onclick({target:{closest:function(selector){return selector==="[data-alert]"?item:selector==="[data-open-related]"?{}:null;}}});assert.equal(navigated.route,"product-edit");assert.equal(navigated.state.editProductId,11);
  categoryRows[3].relatedRoute="installment-payment";categoryRows[3].relatedId=22;categoryRows[3].relatedParentId=33;nodes["#alertType"].value="installment";nodes["#alertType"].onchange();item={getAttribute:function(){return "4";}};box.onclick({target:{closest:function(selector){return selector==="[data-alert]"?item:selector==="[data-open-related]"?{}:null;}}});assert.equal(navigated.route,"installment-payment");assert.equal(navigated.state.installmentContractId,22);assert.equal(navigated.state.installmentCustomerId,33);
  // Successful transport writes emit invalidation; reads and rejected writes do not.
  var transportEvents=[],responseOk=true;
  var transport={window:{dispatchEvent:function(e){transportEvents.push(e);}},CustomEvent:function(type,options){this.type=type;this.detail=options.detail;},sessionStorage:{getItem:function(){return null;},setItem:function(){},removeItem:function(){}},
    fetch:async function(){return{ok:responseOk,status:responseOk?200:409,json:async function(){return responseOk?{success:true,data:{}}:{success:false,error:{code:"conflict"}};}};}};
  vm.runInNewContext(fs.readFileSync(path.join(root,"js/api-client.js"),"utf8"),transport);
  await transport.window.AlKhayalApi.request("/api/alerts");assert.equal(transportEvents.length,0);
  await transport.window.AlKhayalApi.request("/api/returns",{method:"POST"});assert.equal(transportEvents[0].detail.path,"/api/returns");
  responseOk=false;await assert.rejects(transport.window.AlKhayalApi.request("/api/returns",{method:"POST"}));assert.equal(transportEvents.length,1);
  // Product Add/Edit share the same field and exact string payload.
  var catalog=fs.readFileSync(path.join(root,"js/modules/catalog.js"),"utf8"),formSource=catalog.slice(catalog.indexOf("  function readProductForm("),catalog.indexOf("  function bindInlineCategory(")),form={};
  ["catalogName","catalogPrice","catalogPurchasePrice","catalogReorderPoint","catalogDescription","catalogCategoryInput","catalogUnit","catalogActive"].forEach(function(id){form["#"+id]={value:({catalogName:"صنف",catalogPrice:"1",catalogReorderPoint:"1.000001",catalogUnit:"1",catalogActive:"true"})[id]||""};});form["#catalogAllowOutOfStock"]={checked:true};
  ["name","price","purchasePrice","reorderPoint"].forEach(function(key){form['[data-error-for="'+key+'"]']={textContent:""};});
  var formContext={};vm.runInNewContext(formSource+'this.read=readProductForm;',formContext);var host={querySelector:function(s){return form[s];}};
  assert.equal(formContext.read(host,{}).data.reorderPoint,"1.000001");assert.equal(formContext.read(host,{}).data.allowSaleWhenOutOfStock,true);assert.ok(formContext.read(host,{}).valid);form["#catalogAllowOutOfStock"].checked=false;assert.equal(formContext.read(host,{}).data.allowSaleWhenOutOfStock,false);form["#catalogReorderPoint"].value="-1";assert.equal(formContext.read(host,{}).valid,false);
  console.log("Unified alerts: total-stock feed, caching, mutation coalescing, unread badge, legacy permissions, filters and reorder form passed.");
};
