"use strict";
var assert=require("assert"),fs=require("fs"),path=require("path"),vm=require("vm"),Element=require("./inventory-tests.js").Element;
var source=fs.readFileSync(path.join(__dirname,"../js/modules/renderers.js"),"utf8");
var localization=fs.readFileSync(path.join(__dirname,"../js/core/shift-localization.js"),"utf8");
function settle(){return new Promise(function(resolve){setImmediate(resolve);});}
module.exports=async function(){
  var calls=[],container=new Element("section"),payload={totalCount:2,page:1,pageSize:50,items:[{httpMethod:"POST",resource:"/api/customers/<script>x</script>",actorUserId:1,actorUsername:"admin",actorDisplayName:"مسؤول النظام",occurredUtc:"2026-11-01T10:00:00.0000000Z",statusCode:201,outcome:"success"},{httpMethod:"PUT",resource:"/api/users/2",actorUserId:2,actorUsername:"cashier",actorDisplayName:"الكاشير الثاني",occurredUtc:"2026-10-31T10:00:00.0000000Z",statusCode:200,outcome:"success"}]};
  var utils={escape:function(value){return String(value==null?"":value).replace(/[&<>"']/g,function(c){return {"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c];});},number:String,dateTime:String};
  var window={AlKhayalUtils:utils,AlKhayalUI:{pageHeader:function(){return "";}},AlKhayalServices:{},AlKhayalCalculations:{},AlKhayalApi:{request:function(url){calls.push(url);return Promise.resolve(payload);}}};
  vm.runInNewContext(localization,{window:window,Intl:Intl},{filename:"shift-localization.js"});
  vm.runInNewContext(source,{window:window,encodeURIComponent:encodeURIComponent},{filename:"renderers.js"});
  window.AlKhayalRenderers["operation-log"]({route:"operation-log"},container,{state:{},isActive:function(){return true;}});await settle();
  assert.ok(calls[0].indexOf("/api/operation-logs?page=1&pageSize=50")===0,"page loads persisted operation history");
  assert.ok(container.innerHTML.indexOf("مسؤول النظام")>=0&&container.innerHTML.indexOf("الكاشير الثاني")>=0&&container.innerHTML.indexOf("إضافة أو تنفيذ")>=0&&container.innerHTML.indexOf("/api/users/2")<0,"renderer displays each original actor and hides technical resource values");
  assert.ok(container.querySelector("#operationLogFilters")&&container.querySelector("[data-log-page=next]"),"renderer provides filters and paging controls");
  console.log("Operation log renderer loading, attribution and filter controls tests passed.");
};
