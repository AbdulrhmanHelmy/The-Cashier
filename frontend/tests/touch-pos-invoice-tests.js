"use strict";
var assert=require("assert"),fs=require("fs"),path=require("path"),vm=require("vm"),Element=require("./inventory-tests.js").Element;
var source=fs.readFileSync(path.join(__dirname,"../js/modules/sales.js"),"utf8");
function copy(x){return JSON.parse(JSON.stringify(x));}
function invoice(status){return {id:21,invoiceNumber:"SAL-ORIGINAL",invoiceDate:"2026-09-01",status:status||"draft",version:3,customerId:8,customerName:"العميل الأصلي",warehouseId:7,warehouseName:"مخزن متوقف",paymentMode:"credit",immediateMethod:"credit",subtotalScaled:240000,discountScaled:20000,taxScaled:22000,totalScaled:242000,paidScaled:50000,returnedScaled:0,outstandingScaled:192000,canEditPosted:false,editRestrictionMessage:"الفاتورة للعرض فقط وفق سياسة الوردية أو يوم العمل.",lines:[{id:101,variantId:11,productUnitId:21,productName:"الصنف الأصلي",variantName:"افتراضي",unitName:"قطعة",quantity:"2",unitPrice:"12.00",discount:"0.00",lineTotalScaled:242000,physicalUnits:[]}]};}
async function settle(){for(var i=0;i<8;i++)await new Promise(function(resolve){setImmediate(resolve);});}
function event(target){return {target:{closest:function(selector){return selector.split(",").map(function(s){return target.closest(s);}).find(Boolean)||null;}},preventDefault:function(){this.prevented=true;}};}
async function verifyResumeChoices(){
  var layer=new Element("div"),toast=new Element("div"),query=layer.querySelector.bind(layer);
  layer.querySelector=function(selector){return selector===".modal"?query("section"):query(selector);};
  var doc={activeElement:new Element("input"),getElementById:function(id){return id==="modalLayer"?layer:toast;}},win={AlKhayalUtils:{escape:String}};
  vm.runInNewContext(fs.readFileSync(path.join(__dirname,"../js/components/ui.js"),"utf8"),{window:win,document:doc,setTimeout:setTimeout});
  for(var choice of ["confirm","view-only","cancel","escape","backdrop"]){
    var result=win.AlKhayalUI.confirm({message:"هل تريد استكمال وتعديل هذه الفاتورة المعلقة؟",confirmText:"استكمال وتعديل",viewOnlyText:"عرض فقط"});
    assert.ok(layer.querySelector("[data-view-only]"),"view-only choice is explicit");
    if(choice==="confirm"||choice==="view-only")layer.querySelector("[data-"+choice+"]").onclick();
    else if(choice==="escape")layer.onkeydown({key:"Escape",preventDefault:function(){}});
    else layer.onclick({target:choice==="backdrop"?layer:layer.querySelector("[data-modal-close]")});
    assert.equal(await result,choice==="confirm",choice+" resolves consent correctly");assert.equal(layer.hidden,true);
  }
}
async function harness(saved,route,tabState){
  var host=new Element("section"),calls=[],confirmations=[],messages=[],navigations=[],answer=true,pendingSave,loadResolvers=[],modalNode=null,modalOptions=null;
  var win={confirm:function(){return answer;},AlKhayalRenderers:{},AlKhayalSettings:{current:function(){return {taxEnabled:true,defaultTaxRate:50,pricesIncludeTax:false};}},AlKhayalUtils:{escape:String,money:String,number:String,normalize:String,debounce:function(fn){return fn;}},AlKhayalUI:{pageHeader:function(){return "";},toast:function(x){messages.push(x);},confirm:function(o){confirmations.push(o);return Promise.resolve(answer);},modal:function(o){modalOptions=o;modalNode=new Element("div");modalNode.innerHTML=o.body+o.actions;if(o.onOpen)o.onOpen(modalNode);},closeModal:function(){if(modalOptions&&modalOptions.beforeClose)modalOptions.beforeClose();modalNode=null;modalOptions=null;}},AlKhayalApi:{request:function(url,options){calls.push({url:url,options:options});if(options)return new Promise(function(resolve,reject){pendingSave={resolve:resolve,reject:reject,body:JSON.parse(options.body)};});if(url==="/api/sales/21")return Promise.resolve(copy(saved));if(url.indexOf("/api/sales/")===0)return new Promise(function(resolve){loadResolvers.push(resolve);});if(url.indexOf("/api/sales?")===0)return Promise.resolve([copy(saved)]);if(url==="/api/inventory/warehouses")return Promise.resolve([{id:1,name:"الحالي",isActive:true,isDefault:true}]);if(url==="/api/settings/company")return Promise.resolve({});if(url==="/api/payment-methods/active")return Promise.resolve([]);if(url==="/api/inventory/categories")return Promise.resolve([{id:5,name:"تصنيف"}]);if(url.indexOf("/api/inventory/products?")===0)return Promise.resolve({items:[],totalCount:0});if(url==="/api/inventory/products/1")return Promise.resolve({id:1,name:"إضافة",salePrice:"5.00",variants:[{id:12,name:"افتراضي",isActive:true,isDefault:true,effectiveSalePriceExact:"5.0000"}],units:[{id:22,name:"قطعة",isActive:true,isBase:true,isSaleUnit:true}],capabilities:[]});return Promise.resolve([]);}}};
  var query=host.querySelector.bind(host);host.querySelector=function(selector){var node=query(selector);if(node&&node.tag==="input"){node.validity=node.validity||{customError:false};node.setCustomValidity=function(message){this.validity.customError=!!message;this.validationMessage=message;};}return node;};
  var values={};var store={getItem:function(key){return values[key]||null;},setItem:function(key,value){values[key]=value;},removeItem:function(key){delete values[key];}};
  vm.runInNewContext(source.replace("    init();","    global.__invoiceTest={state:state,draw:draw,loadInvoice:loadInvoice,saveDraft:saveDraft,postSale:postSale,newTouchInvoice:newTouchInvoice};init();"),{window:win,BigInt:BigInt,Date:Date,setTimeout:setTimeout,clearTimeout:clearTimeout,sessionStorage:store});
  win.AlKhayalRenderers[route==="sales-list"?"sales-invoices":"pos"]({route:route||"pos"},host,{state:tabState||(route==="sales-list"?{}:{saleInvoiceId:21}),isActive:function(){return true;},navigate:function(r,s){navigations.push({route:r,state:s});},canNavigate:function(){return true;}});
  await settle();
  return {saved:function(next){saved=copy(next);},session:values,win:win,host:host,state:win.__invoiceTest.state,test:win.__invoiceTest,calls:calls,confirmations:confirmations,messages:messages,navigations:navigations,answer:function(a){answer=a;},pending:function(){return pendingSave;},loads:loadResolvers,modal:function(){return modalNode;},clickModal:async function(selector){var target=modalNode&&modalNode.querySelector(selector);assert.ok(target,selector);await target.onclick();await settle();},click:async function(selector){var target=host.querySelector(selector);assert.ok(target,selector);await host.onclick(event(target));await settle();},writes:function(){return calls.filter(function(c){return !!c.options;});}};
}
module.exports=async function(){
  assert.ok(source.indexOf('e.target.closest("[data-back]")&&state.mode==="touch"')>=0&&source.indexOf('context.canNavigate("sales-management")')>=0&&source.indexOf('context.navigate("sales-management")')>=0,"Touch POS invoices button uses the canonical permission-aware workspace route");
  assert.ok(source.indexOf('data-suspended-count')>=0,"Touch POS invoices button retains the suspended-invoice badge");
  await verifyResumeChoices();
  var layout=require("./touch-pos-layout-tests.js");
  var empty=await harness(invoice(),"pos",{});layout(empty.host,false);
  assert.equal(empty.state.lines.length,0,"empty invoice keeps shared actions");
  assert.equal(empty.host.querySelector("[data-save]"),null,"cashier has no Save Draft button");
  var warnLine=copy(invoice().lines[0]);warnLine._product={id:1,purchasePrice:"2.00",units:[{id:21,numerator:1,denominator:1}]};warnLine._variant={id:11};
  empty.state.products=[{id:1,availableQuantityScaled:0,allowSaleWhenOutOfStock:true}];empty.state.lines=[warnLine];empty.test.draw();
  assert.ok(empty.host.innerHTML.includes("سيتم احتساب تكلفة الكمية غير المتوفرة تقديريًا"),"shortfall shows one inline estimated-cost warning");
  warnLine._product.purchasePrice=null;empty.test.draw();
  assert.ok(empty.host.innerHTML.includes("قد لا يمكن حساب الربح بدقة"),"missing reference cost uses the distinct incomplete-profit warning");
  empty.state.products[0].availableQuantityScaled=3000000;empty.test.draw();
  assert.ok(!empty.host.innerHTML.includes("sale-cost-warning"),"sufficient stock has no shortfall warning");
  empty.state.lines=[copy(invoice().lines[0])];empty.state.customerId="8";empty.state.customerName="customer";empty.test.draw();
  var suspension=empty.test.newTouchInvoice();await settle();assert.ok(empty.modal(),"dirty invoice requires a choice");
  var snapshot=JSON.stringify(empty.state.lines);var suspendClick=empty.modal().querySelector("[data-suspend-invoice]").onclick();await settle();
  assert.equal(empty.writes()[0].url,"/api/sales/drafts");assert.equal(empty.pending().body.customerId,8);
  empty.pending().reject(new Error("save failed"));await suspendClick;await settle();
  assert.equal(JSON.stringify(empty.state.lines),snapshot,"save failure preserves invoice lines");assert.ok(empty.modal(),"save failure keeps dialog open");
  suspendClick=empty.modal().querySelector("[data-suspend-invoice]").onclick();await settle();empty.pending().resolve(invoice());await suspendClick;await suspension;await settle();
  assert.equal(empty.state.lines.length,0,"successful suspension starts a new invoice");
  assert.equal(empty.writes().length,2,"retry is the only second create request");

  var original=invoice(),h=await harness(original);h.win.AlKhayalAccess={current:function(){return{canEditSalePrices:true};}};
  layout(h.host,"draft-view");
  assert.ok(h.host.innerHTML.includes('class="touch-pos"'),"existing invoice uses main Touch POS layout");
  assert.ok(!h.host.innerHTML.includes('class="card sale-editor"'),"legacy editor is absent from View");
  assert.ok(h.host.innerHTML.includes("SAL-ORIGINAL")&&h.host.innerHTML.includes("فاتورة معلقة")&&h.host.innerHTML.includes("العميل الأصلي")&&h.host.innerHTML.includes("مخزن متوقف"));
  assert.ok(h.host.innerHTML.includes("data-total>24.2</strong>")&&h.host.innerHTML.includes("data-tax>2.2</strong>"),"saved tax/total survives today's 50% tax setting");
  assert.ok(h.host.innerHTML.includes("data-invoice-payment"));assert.equal(h.writes().length,0);assert.equal(h.state.editingExisting,false);
  for(var field of ["[data-quantity]","[data-line-price]","[data-discount]","[data-discount-mode]","[data-payment]","[data-customer-search]"]){
    var viewed=await harness(original),before=JSON.stringify(viewed.state.lines),control=viewed.host.querySelector(field);
    assert.ok(control,field+" is present in draft view");viewed.answer(false);
    viewed.host.onfocusin(event(control));await settle();
    assert.equal(viewed.confirmations.length,1,field+" requests the shared confirmation");
    assert.equal(viewed.state.editingExisting,false);assert.equal(JSON.stringify(viewed.state.lines),before);assert.equal(viewed.writes().length,0);
    viewed.answer(true);viewed.host.onfocusin(event(control));await settle();
    assert.equal(viewed.state.editingExisting,true,field+" opens editing after consent");
    viewed.host.onfocusin(event(viewed.host.querySelector(field)));await settle();assert.equal(viewed.confirmations.length,2,"editing does not request consent again");
  }
  h.answer(false);await h.click("[data-qty]");assert.equal(h.state.lines[0].quantity,"2");assert.equal(h.confirmations.length,1);assert.equal(h.state.editingExisting,false);
  h.answer(true);await h.click("[data-qty]");assert.equal(h.confirmations.length,2);assert.equal(h.state.editingExisting,true);assert.equal(h.state.lines[0].quantity,"1");layout(h.host,"draft-edit");
  await h.click("[data-qty]");assert.equal(h.confirmations.length,2,"confirm only once per edit session");
  assert.equal(h.state.invoice.lines[0].quantity,"2","original snapshot stays isolated");
  await h.click("[data-cancel-changes]");assert.equal(h.state.lines[0].quantity,"2");assert.equal(h.state.editingExisting,false);assert.equal(h.writes().length,0);
  // Change handlers also gate edits from keyboard/paste/accessibility input.
  h.answer(false);var quantity=h.host.querySelector("[data-quantity]");quantity.value="6";await quantity.onchange();assert.equal(h.state.lines[0].quantity,"2");
  h.answer(true);quantity=h.host.querySelector("[data-quantity]");quantity.value="3";await quantity.onchange();assert.equal(h.state.lines[0].quantity,"3");
  assert.ok(h.host.innerHTML.includes('class="sale-line-total" dir="ltr">36</strong>'),"three units at 12 retain the correct isolated line total");
  assert.equal(h.host.querySelector("[data-line-discount]"),null,"Touch POS exposes invoice discounts only");assert.equal(h.state.lines[0].discount,"0.00");
  var product=new Element("button",{"data-product":"1"});await h.host.onclick(event(product));await settle();assert.equal(h.state.lines.length,2,"normal product selection adds lines");
  var addedRemove=h.host.querySelectorAll("[data-remove]")[1];await h.host.onclick(event(addedRemove));await settle();assert.equal(h.state.lines.length,1,"remove uses the normal cart action");await h.host.onclick(event(product));await settle();assert.equal(h.state.lines.length,2);
  var invoiceDiscount=h.host.querySelector("[data-discount]");invoiceDiscount.value="1.00";await invoiceDiscount.oninput();assert.equal(h.state.discountInput,"1.00");
  assert.equal(h.state.discount,"1.00");assert.equal(h.host.querySelector("[data-total]").textContent,"60","fixed invoice discount precedes tax");
  var save=h.test.saveDraft();await settle();assert.equal(h.writes().length,1);assert.equal(h.writes()[0].url,"/api/sales/21");assert.equal(h.writes()[0].options.method,"PUT");assert.equal(h.pending().body.expectedVersion,3);assert.equal(h.pending().body.lines.length,2);
  await h.test.saveDraft();assert.equal(h.writes().length,1,"double submit is blocked");await h.click("[data-cancel-changes]");assert.equal(h.state.editingExisting,true,"cancel blocked while save in flight");
  var updated=copy(original);updated.version=4;updated.lines=copy(h.state.lines);h.pending().resolve(updated);await save;assert.equal(h.state.editingExisting,true);assert.equal(h.state.invoice.version,4);
  assert.ok(h.host.querySelector("[data-post]"),"saved resumed draft remains eligible for checkout");
  h=await harness(invoice(),"sales-list");await h.click("[data-open]");assert.equal(h.navigations[0].route,"pos");assert.equal(h.navigations[0].state.saleInvoiceId,21);
  for(var status of ["posted","canceled","unknown"]){h=await harness(invoice(status));assert.equal(h.host.querySelector("[data-quantity]"),null);assert.equal(h.host.querySelector("[data-remove]"),null);await h.test.saveDraft();await h.test.postSale();assert.equal(h.state.lines[0].quantity,"2");assert.equal(h.writes().length,0);assert.ok(h.host.innerHTML.includes(status==="posted"?"فاتورة منشورة — عرض فقط":"الفاتورة للعرض فقط"));}
  var eligible=invoice("posted");eligible.canEditPosted=true;eligible.editRestrictionMessage=null;eligible.settlementAccounts=[{id:4,name:"خزنة المبيعات",type:"cash"}];h=await harness(eligible);layout(h.host,"posted-view");assert.equal(h.host.querySelector("[data-save-changes]"),null);assert.equal(h.host.querySelector("[data-cancel-changes]"),null);h.answer(true);await h.click("[data-start-correction]");await h.click("[data-qty]");assert.equal(h.state.editingExisting,true);assert.ok(h.host.querySelector("[data-line-price]"),"posted correction exposes price editing after confirmation");var correctionSave=h.test.saveDraft();await settle();assert.ok(h.modal(),"total difference requires explicit settlement");await h.clickModal("[data-confirm-correction]");assert.equal(h.writes()[0].url,"/api/sales/21/corrections");assert.equal(h.pending().body.settlementKind,"customer_credit");assert.equal(h.pending().body.expectedVersion,3);var revised=copy(eligible);revised.id=22;revised.revision=1;revised.lines=copy(h.state.lines);h.pending().resolve({succeeded:true,invoice:revised});await correctionSave;assert.equal(h.state.invoice.id,22);await h.click("[data-start-correction]");await h.click("[data-qty]");await h.click("[data-cancel-changes]");assert.equal(h.state.lines[0].quantity,revised.lines[0].quantity);assert.equal(h.writes().length,1);
  var tabState={},newSale=await harness(invoice(),"pos",tabState);newSale.state.lines=[{variantId:11,productUnitId:21,productName:"فاتورة سابقة",variantName:"افتراضي",unitName:"قطعة",quantity:"4",unitPrice:"12.00",discount:"0.00"}];
  assert.strictEqual(tabState.posCart,newSale.state,"new cart belongs to persistent tab memory");
  tabState.saleInvoiceId=21;h=await harness(invoice(),"pos",tabState);assert.equal(tabState.posCart.lines[0].quantity,"4");await h.click("[data-resume-cart]");assert.ok(h.host.innerHTML.includes("فاتورة سابقة"));assert.ok(h.host.querySelector("[data-post]"),"resuming restores the normal checkout");layout(h.host,false);
  assert.equal(tabState.posCart.lines[0].quantity,"4","layout preserves resumed cart content");
  // A header-only change must enable Save without requiring a quantity edit.
  h=await harness(invoice());var payment=h.host.querySelector("[data-payment]");payment.value="cash";await payment.onchange();assert.equal(h.state.editingExisting,true);assert.equal(h.host.querySelector("[data-save]"),null);
  // A stale request cannot replace the most recently requested invoice.
  h=await harness(invoice());var first=h.test.loadInvoice(22),second=h.test.loadInvoice(23),a=invoice(),b=invoice();a.id=22;b.id=23;b.invoiceNumber="LATEST";h.loads[1](b);await second;h.loads[0](a);await first;assert.equal(h.state.invoice.id,23);
  // Saved draft: safe view -> one consent -> normal cashier -> same-ID save -> reopen.
  h=await harness(invoice());h.win.AlKhayalAccess={current:function(){return{canEditSalePrices:true};}};
  await h.test.postSale();assert.equal(h.writes().length,0,"view-only draft cannot post");
  h.answer(false);
  var card=new Element("button",{"data-product":"1"});
  await h.host.onclick(event(card));await settle();assert.equal(h.state.lines.length,1,"declined product add does not mutate");
  assert.ok(!h.calls.some(function(c){return c.url==="/api/inventory/products/1";}),"declined add performs no product mutation lookup");
  h.answer(true);await h.click("[data-resume-draft]");layout(h.host,"draft-edit");
  assert.equal(h.confirmations[h.confirmations.length-1].message,"هل تريد استكمال وتعديل هذه الفاتورة المعلقة؟");
  assert.equal(h.confirmations[h.confirmations.length-1].viewOnlyText,"عرض فقط");
  var consentCount=h.confirmations.length;h.win.AlKhayalAccess={current:function(){return{canEditSalePrices:true};}};
  await h.host.onclick(event(card));await settle();assert.equal(h.state.lines.length,2,"one product activation adds one line");
  var price=h.host.querySelector("[data-line-price]");price.value="13.00";await price.onchange();assert.equal(h.state.lines[0].unitPrice,"13.00");
  var customerOption=new Element("button",{"data-customer-option":"9","data-customer-name":"عميل آخر"});
  await h.host.onclick(event(customerOption));await settle();assert.equal(h.state.customerId,"9");
  assert.equal(h.confirmations.length,consentCount,"editing does not repeat consent");
  h.answer(false);assert.equal(h.host.beforeRouteReplace({saleInvoiceId:22}),false,"dirty draft replacement/close is guarded");
  var beforeSwitch=h.calls.length;await h.test.loadInvoice(22);assert.equal(h.calls.length,beforeSwitch,"declined internal replacement retains current draft");
  h.answer(true);
  var saving=h.test.saveDraft();await settle();var savedDraft=copy(invoice());
  Object.assign(savedDraft,{version:4,customerId:9,customerName:"عميل آخر",lines:copy(h.state.lines)});
  assert.equal(h.writes()[0].url,"/api/sales/21");assert.equal(h.writes()[0].options.method,"PUT");
  h.pending().resolve(savedDraft);await saving;h.saved(savedDraft);
  assert.equal(h.host.beforeRouteReplace(null),true,"saved draft no longer has unsaved changes");
  await h.test.loadInvoice(21);layout(h.host,"draft-view");assert.equal(h.state.lines.length,2);
  assert.equal(h.state.lines[0].unitPrice,"13.00");assert.equal(h.state.customerId,9);
  assert.deepStrictEqual(Array.from(h.state.lines,function(x){return x.productName;}),Array.from(savedDraft.lines,function(x){return x.productName;}),"reopen preserves ordering");
  // Complete the SAME draft; double click and interrupted-response replay retain the key.
  await h.click("[data-resume-draft]");var posting=h.test.postSale();await settle();
  var writesBeforeDouble=h.writes().length;await h.test.postSale();assert.equal(h.writes().length,writesBeforeDouble);
  assert.equal(h.pending().body.expectedVersion,4);h.pending().resolve(savedDraft);await settle();
  var postWrite=h.writes()[h.writes().length-1];assert.equal(postWrite.url,"/api/sales/21/post");
  var operationKey=JSON.parse(postWrite.options.body).operationKey;assert.ok(operationKey);
  h.pending().reject(new Error("انقطع الاتصال"));await posting;
  assert.equal(h.state.invoice.id,21);assert.equal(h.state.lines.length,2,"interrupted posting retains recoverable draft");
  var posted=copy(savedDraft);posted.status="posted";posted.canEditPosted=false;h.saved(posted);
  var retry=h.test.postSale();await settle();
  assert.equal(h.writes()[h.writes().length-1].url,"/api/sales/21/post","posted authoritative result replays without another PUT");
  assert.equal(h.pending().body.operationKey,operationKey,"retry uses original operation key");
  h.pending().resolve(posted);await retry;await settle();
  assert.equal(h.state.lines.length,0,"successful checkout resets normal cart");
  assert.ok(!h.calls.some(function(c){return c.url==="/api/sales/drafts";}),"resumed workflow never creates a duplicate invoice");
  assert.equal(Object.keys(h.session).length,0,"successful replay clears pending key");
  h=await harness(invoice("posted"));await h.test.postSale();assert.equal(h.writes().length,0,"posted history never reposts");
  // Mounted workspace identity survives refresh, but New Invoice clears it.
  var selectedState={saleInvoiceId:21};h=await harness(invoice(),"pos",selectedState);
  assert.equal(selectedState.saleLoadedInvoiceId,21);assert.equal(selectedState.saleInvoiceId,undefined);
  var refreshed=await harness(invoice(),"pos",selectedState);layout(refreshed.host,"draft-view");
  assert.equal(refreshed.state.invoice.id,21,"refresh reloads selected invoice rather than an empty cart");
  await refreshed.click("[data-new-touch-invoice]");assert.equal(selectedState.saleLoadedInvoiceId,undefined);layout(refreshed.host,false);
  // Missing/invalid documents leave the current draft intact and report the failure.
  h=await harness(invoice());h.saved(null);await h.test.loadInvoice(21);
  assert.equal(h.state.invoice.id,21);assert.equal(h.state.loadingInvoiceId,null);assert.ok(h.messages.length);
  var reads=h.calls.length;await h.test.loadInvoice(0);assert.equal(h.calls.length,reads);assert.equal(h.state.invoice.id,21);
  // A long cart uses identical shared panels/actions; no resizing script replaces it.
  h=await harness(invoice());h.state.lines=Array.from({length:40},function(){return copy(invoice().lines[0]);});h.test.draw();layout(h.host,"draft-view");
  assert.equal(h.host.querySelectorAll("[data-line]").length,40);await h.click("[data-resume-draft]");layout(h.host,"draft-edit");
  assert.equal(h.state.lines.length,40);assert.equal(h.host.querySelectorAll("[data-line-price]").length,40);
  await h.click("[data-clear]");assert.equal(h.state.invoice.id,21,"Clear cannot detach a resumed draft from its identity");
  assert.equal(h.state.lines.length,0);await h.click("[data-cancel-changes]");assert.equal(h.state.lines.length,1,"Cancel restores the saved draft after Clear");
  // Historical discounts remain immutable and explained in every Touch view.
  for(var legacyStatus of ["draft","posted"]){
    var legacy=invoice(legacyStatus);legacy.canEditPosted=true;
    legacy.lines[0].discount="1.00";legacy.lines[0].lineTotalScaled=231000;
    legacy.subtotalScaled=230000;legacy.taxScaled=21000;legacy.totalScaled=231000;
    h=await harness(legacy);var snapshot=JSON.stringify(h.state.invoice);
    assert.ok(h.host.querySelector("[data-legacy-line-discount]"));
    assert.ok(h.host.innerHTML.includes("خصم صنف محفوظ: 1"));
    assert.equal(h.host.querySelector("[data-resume-draft]"),null);
    assert.equal(h.host.querySelector("[data-line-discount]"),null);
    var legacyQty=h.host.querySelector("[data-quantity]");
    if(legacyStatus==="posted")assert.equal(legacyQty,null,"posted history has no quantity editor");
    else {assert.ok(Object.prototype.hasOwnProperty.call(legacyQty.attrs,"readonly"));legacyQty.value="9";await legacyQty.onchange();}
    var legacyDiscount=h.host.querySelector("[data-discount]");legacyDiscount.value="0";await legacyDiscount.oninput();
    await h.test.saveDraft();await h.test.postSale();
    assert.equal(h.writes().length,0);assert.equal(h.state.lines[0].discount,"1.00");
    assert.equal(h.state.lines[0].quantity,"2");assert.equal(JSON.stringify(h.state.invoice),snapshot);
    assert.ok(h.host.innerHTML.includes("data-total>23.1</strong>"));
    await h.test.loadInvoice(21);assert.equal(JSON.stringify(h.state.invoice),snapshot,"reopen preserves exact saved totals");
    await h.click("[data-new-touch-invoice]");assert.equal(h.state.lines.length,0);
    assert.equal(JSON.stringify(legacy.lines[0].discount),'"1.00"',"new invoice leaves saved history untouched");
    await h.host.onclick(event(new Element("button",{"data-product":"1"})));await settle();
    assert.equal(h.state.lines[0].discount,"0.00");layout(h.host,false);
    var newDiscount=h.host.querySelector("[data-discount]"),discountMode=h.host.querySelector("[data-discount-mode]");
    discountMode.value="percent";await discountMode.onchange();newDiscount.value="10";await newDiscount.oninput();
    assert.equal(h.state.discount,"0.50");assert.equal(h.host.querySelector("[data-total]").textContent,"6.75");
    newDiscount.value="101";await newDiscount.oninput();assert.equal(newDiscount.validity.customError,true);
    newDiscount.value="10";await newDiscount.oninput();
    var freshSave=h.test.saveDraft();await settle();
    assert.equal(h.pending().body.discount,"0.50");assert.equal(h.pending().body.lines[0].discount,"0.00");
    var fresh=invoice();fresh.id=99;fresh.lines=copy(h.state.lines);h.pending().resolve(fresh);await freshSave;
  }
  // Long names and complete tracking remain inside the one details cell.
  h=await harness(invoice());var tracked=h.state.lines[0];
  tracked.productName="اسم عربي طويل جدًا ".repeat(15);tracked.batchLotNumber="LOT-123";tracked.expirationDate="2027-01-01";
  tracked.physicalUnits=[{serial:"SERIAL-1",imeis:["123456789012345","987654321098765"]}];
  h.test.draw();layout(h.host,"draft-view");
  assert.ok(h.host.querySelector("summary").getAttribute("title").includes("987654321098765"));
  assert.ok(h.host.querySelector("summary").getAttribute("title").includes("2027-01-01"));
  console.log("Touch POS drafts/view/correction: three-way consent, shared DOM, edits, same-ID save/reopen/post, interrupted-post replay, duplicate guards, cart retention, refresh identity, missing/stale loads and posted restrictions passed.");
};
if(require.main===module)module.exports().catch(function(error){console.error(error);process.exitCode=1;});
