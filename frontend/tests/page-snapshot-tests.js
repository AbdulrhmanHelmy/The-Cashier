"use strict";
var assert=require("assert"),fs=require("fs"),path=require("path"),vm=require("vm");
module.exports=async function(){
  var source=fs.readFileSync(path.join(__dirname,"../js/api-client.js"),"utf8"),data={"/api/treasury/accounts":[{id:1,name:"خزنة",balance:"100.00"}],"/api/purchases":[{id:1,invoiceNumber:"P-1"}]},calls=[];
  var session={getItem:function(){return null;},setItem:function(){},removeItem:function(){}},window={};
  var context={window:window,sessionStorage:session,CustomEvent:function(name,options){this.type=name;this.detail=options&&options.detail;},dispatchEvent:function(){},fetch:function(url){calls.push(url);return Promise.resolve({ok:true,status:200,json:function(){return Promise.resolve({success:true,data:data[url]});}});}};
  vm.runInNewContext(source,context,{filename:"api-client.js"});
  var api=window.AlKhayalApi;api.setActivePage("purchase-management");
  assert.strictEqual(api.errorMessage("توجد وردية مفتوحة للكاشير أو لهذا الجهاز. أغلقها أولًا.","conflict",409),"توجد وردية مفتوحة للكاشير أو لهذا الجهاز. أغلقها أولًا.","known Arabic conflict reasons must remain actionable");
  assert.strictEqual(api.errorMessage("", "conflict",409),"تعارضت العملية مع تغيير أحدث. حدّث الشاشة وأعد المحاولة.","empty conflict responses keep the generic fallback");
  await api.request("/api/treasury/accounts");api.untrackPageSnapshot("purchase-management","/api/treasury/accounts");
  data["/api/treasury/accounts"][0].balance="110.00";
  assert.strictEqual(await api.checkPageSnapshot("purchase-management"),false,"hidden treasury balances must not create a purchase-page stale notice");
  await api.request("/api/purchases");data["/api/purchases"][0].invoiceNumber="P-2";
  assert.strictEqual(await api.checkPageSnapshot("purchase-management"),true,"actual purchase changes must still be detected");
  for(var i=0,routes=["customer-credit-payment","customer-statement","employee-advances","employee-payroll"];i<routes.length;i++){
    var route=routes[i];api.setActivePage(route);await api.request("/api/treasury/accounts");data["/api/treasury/accounts"][0].balance=String(130+i)+".00";
    assert.strictEqual(await api.checkPageSnapshot(route),false,"balance-only changes must not stale "+route+" while it uses account names/types");
    data["/api/treasury/accounts"][0].name="خزنة "+i;
    assert.strictEqual(await api.checkPageSnapshot(route),true,"account identity changes must still be detected where they change "+route+" choices");
  }
  assert.strictEqual(calls.length,15,"unrelated balance data is omitted while relevant purchase and account data is rechecked");
  console.log("Page snapshot relevance tests passed.");
};
if(require.main===module)module.exports().catch(function(error){console.error(error);process.exitCode=1;});
