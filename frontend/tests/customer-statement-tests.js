"use strict";
var assert=require("assert"),fs=require("fs"),path=require("path"),vm=require("vm"),Element=require("./inventory-tests.js").Element;
var source=fs.readFileSync(path.join(__dirname,"../js/modules/renderers.js"),"utf8"),registry=JSON.parse(fs.readFileSync(path.join(__dirname,"../data/screens.json"),"utf8"));
function settle(){return new Promise(function(resolve){setImmediate(resolve);});}
function statement(id,balance){return{customerId:id,customerName:"عميل "+id,currentBalance:balance,openingBalance:0,totalDebit:balance,totalCredit:0,closingBalance:balance,movements:[{date:"2026-10-28",description:"إضافة مديونية",notes:"تفاصيل محفوظة",debit:balance,credit:0,balance:balance}]};}
function harness(initialState){
  var reads=[],writes=[],pending=[],modal,options,active=true,accountResponse;
  var api={request:function(url,config){if(config){writes.push({url:url,data:JSON.parse(config.body)});return new Promise(function(resolve,reject){pending.push({resolve:resolve,reject:reject});});}
    if(url==="/api/customers")return Promise.resolve([{id:1,name:"عميل أول",code:"C1"},{id:2,name:"عميل ثان",code:"C2"}]);
    if(url==="/api/treasury/accounts")return accountResponse||Promise.resolve([{id:7,name:"الخزنة",type:"cash"},{id:8,name:"البنك",type:"bank"}]);
    return new Promise(function(resolve,reject){reads.push({url:url,resolve:resolve,reject:reject});});}};
  var win={AlKhayalUtils:{escape:function(x){return String(x==null?"":x);},money:String,date:String},AlKhayalServices:{},AlKhayalCalculations:{},AlKhayalUI:{pageHeader:function(){return "";},toast:function(){},closeModal:function(){},modal:function(value){options=value;modal=new Element("section");modal.innerHTML=value.body+value.actions;value.onOpen(modal);}}};
  vm.runInNewContext(source,{window:win,AlKhayalApi:api},{filename:"customer-statement.js"});
  var node=new Element("section"),state=initialState||{};win.AlKhayalRenderers.statement({party:"customer"},node,{state:state,isActive:function(){return active;}});
  return{node:node,reads:reads,writes:writes,pending:pending,modal:function(){return modal;},options:function(){return options;},leave:function(){active=false;},setAccounts:function(response){accountResponse=response;},submit:function(){modal.querySelector("#customerAccountForm").onsubmit({preventDefault:function(){}});}};
}
async function selectedCustomerAndRapidReopen(){
  var state={customerStatementId:2},h=harness(state);await settle();
  assert.equal(h.reads[0].url,"/api/customers/2/statement?page=1","an explicit navigation customer must win over list order and defaults");
  h.reads[0].resolve(statement(2,20));await settle();assert.ok(h.node.querySelector("#statementBody").innerHTML.includes("عميل 2"));
  var customer=h.node.querySelector("#statementCustomer");customer.value="1";customer.onchange();customer.value="2";customer.onchange();
  assert.equal(h.reads[1].url,"/api/customers/1/statement?page=1");assert.equal(h.reads[2].url,"/api/customers/2/statement?page=1");
  h.reads[2].resolve(statement(2,22));await settle();h.reads[1].resolve(statement(1,99));await settle();
  assert.ok(h.node.querySelector("#statementBody").innerHTML.includes("عميل 2"),"a late response for a rapidly clicked prior customer must not repaint the statement");
  h.leave();state.customerStatementId=1;var reopened=harness(state);await settle();assert.equal(reopened.reads[0].url,"/api/customers/1/statement?page=1","reopening an existing statement tab with new route state must load the newly selected customer");
}
async function operationsAndRetry(){
  var h=harness();await settle();assert.equal(h.reads[0].url,"/api/customers/1/statement?page=1");h.reads[0].resolve(statement(1,100));await settle();
  assert.ok(h.node.querySelector("#statementBody").innerHTML.includes("تفاصيل محفوظة"));
  assert.ok(h.node.innerHTML.includes("customer-statement-filters"),"the active statement renderer must expose the scoped filter treatment");
  assert.ok(h.node.querySelector("#statementBody").innerHTML.includes("customer-current-balance"),"the current balance must use the emphasized summary card");
  assert.equal((h.node.querySelector("#statementBody").innerHTML.match(/customer-statement-stat/g)||[]).length,5,"all five authoritative statement totals must remain rendered");
  assert.ok(h.node.querySelector("#statementBody").innerHTML.includes("customer-statement-table"),"the persisted movement table must use the compact scoped style");
  assert.ok(!h.node.querySelector("[data-customer-collect]").disabled);
  h.node.querySelector("[data-customer-increase]").onclick();var modal=h.modal();modal.querySelector("#customerAccountAmount").value="150.50";modal.querySelector("#customerAccountNotes").value="  دين جديد  ";
  h.submit();h.submit();assert.equal(h.writes.length,1,"double submit must dispatch once");assert.equal(h.writes[0].data.type,"increase");assert.equal(h.writes[0].data.amount,"150.50","increase may exceed the existing debt");assert.equal(h.writes[0].data.notes,"  دين جديد  ");assert.ok(h.writes[0].data.operationKey);
  var original=JSON.stringify(h.writes[0].data);h.pending[0].reject({status:0,message:"انقطع الاتصال"});await settle();
  assert.ok(modal.querySelector("#customerAccountAmount").disabled);assert.equal(h.options().beforeClose(),false);h.submit();assert.equal(JSON.stringify(h.writes[1].data),original);
  h.pending[1].resolve({replayed:true});await settle();h.reads[1].resolve(statement(1,250.5));await settle();assert.ok(h.node.querySelector("#statementBody").innerHTML.includes("250.5"));
  h.node.querySelector("[data-customer-collect]").onclick();await settle();modal=h.modal();modal.querySelector("#customerAccountAmount").value="300";modal.querySelector("#customerCollectionMethod").value="cash";modal.querySelector("#customerCollectionAccount").value="7";h.submit();assert.equal(h.writes[2].data.type,"collection");assert.equal(h.writes[2].data.treasuryAccountId,7);
  h.pending[2].reject({status:400,message:"المبلغ أكبر من رصيد العميل الحالي."});await settle();assert.equal(modal.querySelector("#customerAccountAmount").value,"300");assert.ok(!modal.querySelector("#customerAccountAmount").disabled);
  modal.querySelector("#customerAccountAmount").value="0.001";h.submit();assert.equal(h.writes.length,3,"sub-piastre inputs must not submit");
}
async function filtersAndStale(){
  var h=harness();await settle();var customer=h.node.querySelector("#statementCustomer"),from=h.node.querySelector("#statementFrom"),to=h.node.querySelector("#statementTo");
  customer.value="2";customer.onchange();h.reads[1].resolve(statement(2,0));await settle();h.reads[0].resolve(statement(1,100));await settle();
  assert.ok(h.node.querySelector("[data-customer-collect]").disabled);assert.ok(h.node.querySelector("#statementBody").innerHTML.includes("عميل 2"));
  from.value="2026-10-01";from.onchange();assert.ok(h.reads[2].url.includes("from=2026-10-01"));assert.strictEqual(h.node.querySelector("#statementCustomer"),customer);
  to.value="2026-09-01";to.onchange();assert.equal(h.reads.length,3);assert.ok(!h.node.querySelector("#statementError").hidden);
  h.reads[2].reject({message:"فشل التحميل"});await settle();h.node.querySelector("[data-statement-retry]").onclick();assert.equal(h.reads.length,4);
  h.leave();var before=h.node.querySelector("#statementBody").innerHTML;h.reads[3].resolve(statement(2,50));await settle();assert.equal(h.node.querySelector("#statementBody").innerHTML,before);
}
async function collectionDialogContext(){
  var h=harness(), resolveAccounts;
  h.setAccounts(new Promise(function(resolve){resolveAccounts=resolve;}));
  await settle();
  h.reads[0].resolve(statement(1,100));await settle();
  h.node.querySelector("[data-customer-collect]").onclick();
  var customer=h.node.querySelector("#statementCustomer");
  customer.value="2";customer.onchange();
  h.reads[1].resolve(statement(2,25));await settle();
  h.node.querySelector("[data-customer-increase]").onclick();
  var currentModal=h.modal();
  resolveAccounts([{id:7,name:"الخزنة",type:"cash"}]);await settle();
  assert.strictEqual(h.modal(),currentModal,"a delayed collection must not replace the newly selected customer's debt dialog");
  assert.equal(h.writes.length,0);
}
async function missingRequestedCustomer(){
  var h=harness({customerStatementId:99});await settle();
  assert.equal(h.reads.length,0,"an unavailable route ID must not load a different customer's statement");
  assert.ok(h.node.querySelector("[data-customer-increase]").disabled);
  assert.ok(h.node.querySelector("[data-customer-collect]").disabled);
  var customer=h.node.querySelector("#statementCustomer");
  customer.value="2";customer.onchange();
  assert.equal(h.reads[0].url,"/api/customers/2/statement?page=1");
}
module.exports=async function(){
  assert.ok(source.includes("customer-directory-toolbar")&&source.includes("customer-directory-table"),"the active customer directory renderer must use its scoped visual hooks");
  assert.ok(source.includes("customer-record-form"),"the active customer add/edit modal must use its scoped two-column form hook");
  assert.deepStrictEqual(registry.modules.find(function(x){return x.id==="customers";}).items,["customers-list","customer-statement"]);
  var old=registry.screens.find(function(x){return x.route==="customer-credit-payment";});assert.ok(old.navigationHidden);assert.equal(old.redirectTo,"customer-statement");
  await operationsAndRetry();await filtersAndStale();await selectedCustomerAndRapidReopen();await collectionDialogContext();await missingRequestedCustomer();console.log("Customer statement persistence wiring, collection/increase, retry identity, filters, navigation and stale-response tests passed.");
};
if(require.main===module)module.exports().catch(function(error){console.error(error);process.exitCode=1;});
