"use strict";
// Execute the production handlers in a small deterministic DOM. There is no network,
// database, timing workaround, or production-only test hook.
var assert=require("assert"),fs=require("fs"),path=require("path"),vm=require("vm");
var source=fs.readFileSync(path.join(__dirname,"../js/modules/inventory.js"),"utf8");
function Element(tag,attributes){this.tag=tag;this.attrs=attributes||{};this.children=[];this.value=this.attrs.value||"";this.hidden=Object.prototype.hasOwnProperty.call(this.attrs,"hidden");this.disabled=false;this.isConnected=true;this.textContent="";}
Element.prototype.setAttribute=function(name,value){this.attrs[name]=value;};
Element.prototype.getAttribute=function(name){return this.attrs[name];};
Element.prototype.appendChild=function(child){child.parent=this;this.children.push(child);return child;};
Element.prototype.remove=function(){if(this.parent)this.parent.children=this.parent.children.filter(function(x){return x!==this;},this);this.isConnected=false;};
Element.prototype.focus=function(){};
Element.prototype.closest=function(selector){for(var node=this;node;node=node.parent){if(node.matches(selector))return node;}return null;};
Element.prototype.matches=function(selector){if(selector[0]==="#")return this.attrs.id===selector.slice(1);if(selector[0]==="["){var match=selector.match(/^\[([^=\]]+)(?:="([^"]*)")?\]$/);return !!match&&Object.prototype.hasOwnProperty.call(this.attrs,match[1])&&(match[2]===undefined||this.attrs[match[1]]===match[2]);}return this.tag===selector;};
Element.prototype.querySelectorAll=function(selector){var selectors=selector.split(","),result=[];function visit(node){node.children.forEach(function(child){if(selectors.some(function(s){return child.matches(s);}))result.push(child);visit(child);});}visit(this);return result;};
Element.prototype.querySelector=function(selector){return this.querySelectorAll(selector)[0]||null;};
Object.defineProperty(Element.prototype,"innerHTML",{get:function(){return this.html||"";},set:function(html){
  this.html=html;this.children=[];var stack=[this],token,pattern=/<\/?[^>]+>|[^<]+/g;
  while((token=pattern.exec(html))){var text=token[0];if(text.slice(0,2)==="</"){if(stack.length>1)stack.pop();continue;}if(text[0]!=="<")continue;
    var tag=text.match(/^<([^\s/>]+)/)[1],attrs={},attr,attrPattern=/([^\s=<>]+)(?:="([^"]*)")?/g;attrPattern.lastIndex=tag.length+1;
    while((attr=attrPattern.exec(text))){if(attr[1]!=="/")attrs[attr[1]]=attr[2]===undefined?"":attr[2];}
    var node=new Element(tag,attrs);stack[stack.length-1].appendChild(node);if(["input","br","hr","img"].indexOf(tag)<0)stack.push(node);
  }
  var selects=this.querySelectorAll("select");if(this.tag==="select")selects.unshift(this);selects.forEach(function(select){var options=select.querySelectorAll("option"),selected=options.find(function(x){return Object.prototype.hasOwnProperty.call(x.attrs,"selected");})||options[0];select.value=selected?selected.value:"";});
}});
function flush(){return new Promise(function(resolve){setImmediate(resolve);});}
async function settle(){await flush();await flush();}
function harness(detail,products){
  var modal,modalOptions,calls=[],closed=0,sequence=0,pending=[];
  var api={request:function(url,options){calls.push({url:url,options:options});if(options&&options.method==="POST")return new Promise(function(resolve,reject){pending.push({resolve:resolve,reject:reject});});if(url.indexOf("/api/inventory/products/")===0)return Promise.resolve(detail);if(url.indexOf("/api/inventory/products?")===0)return Promise.resolve({items:products||[]});if(url.indexOf("/api/inventory/balances")===0)return Promise.resolve([]);if(url==="/api/features/effective")return Promise.resolve({modules:[]});return Promise.resolve([]);}};
  var win={crypto:{getRandomValues:function(bytes){bytes.fill(++sequence);}},AlKhayalUtils:{escape:function(x){return String(x==null?"":x);},debounce:function(fn){return fn;}},AlKhayalUI:{modal:function(options){modalOptions=options;modal=new Element("section");modal.innerHTML=options.body+options.actions;var form=modal.querySelector("#receiptForm");form.elements={notes:form.querySelector('[name="notes"]'),destination:form.querySelector('[name="destination"]')};options.onOpen(modal);},closeModal:function(){closed++;},toast:function(){},pageHeader:function(){return "";}},AlKhayalApi:api,AlKhayalRenderers:{}};
  var hook='state.view=state.view||"balances";load();';assert.ok(source.includes(hook));
  vm.runInNewContext(source.replace(hook,'state.view=state.view||"balances";global.__inventoryTest={receiptModal:receiptModal,count:count,historyV2:historyV2,valuation:inventoryValuation,formatExactValue:formatExactValue,formatQty:formatQty,movementNote:movementNote};'),{window:win,document:{createElement:function(tag){return new Element(tag);}},Uint8Array:Uint8Array,BigInt:BigInt},{filename:"inventory.js"});
  win.AlKhayalRenderers["warehouse-directory"]({},new Element("div"),{state:{view:"history"},user:{role:"administrator"},isActive:function(){return true;},canNavigate:function(route){return route==="purchase-details"||route==="pos";},navigate:function(route,state){win.lastNavigation={route:route,state:state};}});
  return {win:win,calls:calls,pending:pending,open:async function(type){win.__inventoryTest.receiptModal({id:1,name:"الرئيسي"},[{id:1,name:"الرئيسي",isActive:true},{id:2,name:"الفرعي",isActive:true}],type);var row=modal.querySelector("[data-stock-line]"),product=row.querySelector("[data-product]");product.value=String(detail.id);product.onchange();await settle();return row;},modal:function(){return modal;},options:function(){return modalOptions;},closed:function(){return closed;},posts:function(){return calls.filter(function(x){return x.options&&x.options.method==="POST";});}};
}
function simple(){return {id:7,capabilities:[],variants:[{id:11,isActive:true,name:"افتراضي",effectiveSalePrice:"12.0000"}],units:[{id:21,isActive:true,isBase:true,numerator:1,denominator:1,name:"قطعة"}]};}
function set(row,selector,value){row.querySelector(selector).value=value;}
function fill(row){set(row,"[data-quantity]","2");set(row,"[data-cost]","100");set(row,"[data-sale]","150");}
async function verifyReceipt(){
  var h=harness(simple()),row=await h.open(),button=h.modal().querySelector("[data-save-receipt]");
  assert.ok(row.querySelector("[data-variant-field]").hidden&&row.querySelector("[data-unit-field]").hidden,"single unit/variant must be automatic");
  assert.ok(row.querySelector("[data-expiry-field]").hidden&&row.querySelector("[data-serial-field]").hidden,"ordinary products must not request physical identifiers");
  fill(row);set(row,"[data-cost]","");button.onclick();assert.equal(h.posts().length,0,"missing actual cost cannot post");
  set(row,"[data-cost]","0.00001");button.onclick();assert.equal(h.posts().length,0,"excess money precision cannot post");
  fill(row);set(row,"[data-quantity]","0");button.onclick();assert.equal(h.posts().length,0);
  fill(row);button.onclick();button.onclick();assert.equal(h.posts().length,1,"repeated Save must dispatch once");assert.ok(button.disabled);assert.equal(h.options().beforeClose(),false,"pending request cannot lose its retry state by closing");
  var first=JSON.parse(h.posts()[0].options.body);assert.equal(first.lines[0].unitCost,"100");assert.equal(first.lines[0].salePrice,"150");
  h.pending[0].reject(new Error("فُقد الرد"));await settle();assert.equal(button.disabled,false);assert.equal(row.querySelector("[data-cost]").value,"100");assert.equal(h.closed(),0,"error keeps the mounted form");
  button.onclick();var retry=JSON.parse(h.posts()[1].options.body);assert.equal(first.operationKey,retry.operationKey,"lost-response retry retains its original identity");
  h.pending[1].reject(new Error("فُقد الرد"));await settle();set(row,"[data-sale]","151");button.onclick();var changed=JSON.parse(h.posts()[2].options.body);assert.notEqual(changed.operationKey,first.operationKey,"changed retail price creates a new request identity");
  h.pending[2].resolve({documentId:1});await settle();assert.equal(h.closed(),1);assert.ok(h.calls.some(function(x){return x.url==="/api/inventory/warehouses";}),"success refreshes the warehouse screen");
}
async function verifyConditionalFields(){
  var detail=simple();detail.variants.push({id:12,isActive:true,name:"أزرق / L",effectiveSalePrice:"25.0000"});detail.units.push({id:22,isActive:true,isBase:false,numerator:12,denominator:1,name:"كرتونة"});detail.capabilities=[{key:"batch-tracking",isAssigned:true},{key:"expiry-dates",isAssigned:true},{key:"serial-numbers",isAssigned:true},{key:"imei-tracking",isAssigned:true}];
  var h=harness(detail),row=await h.open(),button=h.modal().querySelector("[data-save-receipt]");
  assert.equal(row.querySelector("[data-variant-field]").hidden,false);assert.equal(row.querySelector("[data-unit-field]").hidden,false);assert.equal(row.querySelector("[data-expiry-field]").hidden,false);assert.equal(row.querySelector("[data-serial-field]").hidden,false);
  set(row,"[data-variant]","12");row.querySelector("[data-variant]").onchange();assert.ok(row.querySelector("[data-sale]").placeholder.includes("25.00"),"price guidance must follow selected variant");
  set(row,"[data-unit]","22");row.querySelector("[data-unit]").onchange();assert.ok(row.querySelector("[data-sale]").placeholder.includes("300.00"),"price guidance converts selected unit");
  fill(row);button.onclick();assert.equal(h.posts().length,0,"expiry and serial tracking cannot be omitted");set(row,"[data-expiry]","2030-01-01");set(row,"[data-quantity]","0.083333");button.onclick();assert.equal(h.posts().length,0,"fractional serial base-unit conversion is rejected");
  set(row,"[data-unit]","21");set(row,"[data-quantity]","1");set(row,"[data-serials]","SN1|490154203237518");button.onclick();assert.equal(h.posts().length,1);var input=JSON.parse(h.posts()[0].options.body).lines[0];assert.equal(input.variantId,12);assert.equal(input.physicalUnits[0].serial,"SN1");assert.equal(input.expirationDate,"2030-01-01");
}
async function verifyUnitSalePriceGuidance(){
  var detail=simple();detail.units.push({id:22,isActive:true,isBase:false,numerator:12,denominator:1,name:"كرتونة",salePrice:"130.00",purchasePrice:"90.00"});
  var h=harness(detail),row=await h.open(),sale=row.querySelector("[data-sale]");
  assert.equal(sale.placeholder,"السعر الحالي: 12.00");
  set(row,"[data-unit]","22");row.querySelector("[data-unit]").onchange();
  assert.equal(sale.placeholder,"السعر الحالي: 130.00","saved carton sale price takes precedence over conversion");
  assert.equal(sale.value,"","guidance does not silently fill the new sale price");
  set(row,"[data-unit]","21");row.querySelector("[data-unit]").onchange();
  assert.equal(sale.placeholder,"السعر الحالي: 12.00","switching back restores the base-unit price");
}
async function verifyLiveProductPicker(){
  for(var type of ["manual_receipt","manual_issue","adjustment"]){
    var detail=simple(),h=harness(detail,[{id:7,name:"شاي",productType:"stock",isActive:true,isArchived:false},{id:8,name:"موقوف",productType:"stock",isActive:false,isArchived:false}]),row=await h.open(type),search=row.querySelector("[data-search]");
    search.value="شا";search.oninput();await settle();
    var choices=row.querySelectorAll("[data-product-choice]");assert.equal(choices.length,1,"only active stock products appear while typing");
    assert.equal(search.getAttribute("aria-expanded"),"true");choices[0].onclick();await settle();
    assert.equal(row.querySelector("[data-product]").value,"7");assert.equal(search.value,"شاي");assert.equal(search.getAttribute("aria-expanded"),"false");
    search.value="غير موجود";search.oninput();assert.equal(row.querySelector("[data-product]").value,"","editing search invalidates the previous selection");
  }
  var h=harness(simple(),[{id:7,name:"شاي",productType:"stock",isActive:true,isArchived:false},{id:9,name:"شاي أخضر",productType:"stock",isActive:true,isArchived:false}]),row=await h.open(),search=row.querySelector("[data-search]");
  search.value="شاي";search.oninput();await settle();
  var prevented=false;search.onkeydown({key:"ArrowDown",preventDefault:function(){prevented=true;}});
  assert.equal(row.querySelectorAll("[data-product-choice]")[1].getAttribute("aria-selected"),"true");
  search.onkeydown({key:"Enter",preventDefault:function(){prevented=true;}});await settle();
  assert.ok(prevented);assert.equal(row.querySelector("[data-product]").value,"9","keyboard selection loads the highlighted product");
}
async function verifyOtherOperationsAndValuation(){
  var h=harness(simple()),row=await h.open("adjustment");set(row,"[data-direction]","minus");row.querySelector("[data-direction]").onchange();assert.equal(row.getAttribute("data-adjustment-direction"),"minus");assert.ok(row.querySelector("[data-cost-field]").hidden);set(row,"[data-quantity]","1");h.modal().querySelector("[data-save-receipt]").onclick();assert.equal(JSON.parse(h.posts()[0].options.body).lines[0].quantity,"-1");
  h=harness(simple());row=await h.open("transfer");assert.equal(row.querySelector("[data-cost]"),null,"transfer does not request an invented purchase cost");set(row,"[data-quantity]","1");h.modal().querySelector("[data-save-receipt]").onclick();var transfer=JSON.parse(h.posts()[0].options.body);assert.equal(transfer.destinationWarehouseId,2);assert.equal(transfer.lines[0].unitCost,null);
  var value=h.win.__inventoryTest.valuation([{quantityBaseScaled:8000000,valueScaled:9600000,salePriceScaled:1500000,costBasisComplete:true}]);assert.equal(value.cost,9600000n);assert.equal(value.sales,12000000n);assert.ok(value.complete);
  assert.equal(h.win.__inventoryTest.valuation([{quantityBaseScaled:1,valueScaled:0,salePriceScaled:1,costBasisComplete:false}]).complete,false);
  assert.equal(h.win.__inventoryTest.valuation([{quantityBaseScaled:1,valueScaled:0,salePriceScaled:null,costBasisComplete:true}]).complete,false);
  assert.equal(h.win.__inventoryTest.valuation([{quantityBaseScaled:9007199254740992,valueScaled:1,salePriceScaled:1,costBasisComplete:true}]).complete,false,"unsafe JSON integers cannot produce false totals");
}
async function verifyCountRetry(){
  var h=harness(simple()),target=new Element("section");h.win.__inventoryTest.count(target,{id:1,name:"الرئيسي"});
  assert.ok(target.innerHTML.includes("نطاق المنتجات")&&target.innerHTML.includes("تركيبة المنتج")&&!target.innerHTML.includes("كل المخزن"),"stock-count filters name their distinct scopes");
  target.querySelector("[data-start-count]").onclick();assert.equal(h.posts().length,1);
  h.pending[0].resolve({id:9,lines:[{variantId:11,batchId:null,productName:"شاي",variantName:"افتراضي",recordedQuantityScaled:2000000,balanceVersion:1,serialTracked:false}]});await settle();
  var row=target.querySelector("[data-count-row]"),counted=row.querySelector("[data-counted]"),save=target.querySelector("[data-post-count]");
  assert.ok(target.querySelector("#countDraft").innerHTML.includes("inventory-count-table")&&row.querySelector("[data-recorded]"),"recorded and editable counted quantities use the aligned count table");
  counted.value="1";counted.oninput();assert.equal(row.querySelector("[data-cost]").hidden,true);assert.equal(row.querySelector("[data-identifiers]").hidden,true);
  save.onclick();save.onclick();assert.equal(h.posts().length,2);var first=JSON.parse(h.posts()[1].options.body);assert.equal(first.lines[0].countedQuantity,"1");assert.ok(save.disabled);
  h.pending[1].reject(new Error("فقد الرد"));await settle();save.onclick();var retry=JSON.parse(h.posts()[2].options.body);assert.equal(retry.operationKey,first.operationKey);
  h.pending[2].reject(new Error("فقد الرد"));await settle();counted.value="3";counted.oninput();assert.equal(row.querySelector("[data-cost]").hidden,false);row.querySelector("[data-cost]").value="100";save.onclick();var changed=JSON.parse(h.posts()[3].options.body);assert.notEqual(changed.operationKey,first.operationKey);assert.equal(changed.lines[0].unitCost,"100");
}
async function verifyHistoryPresentation(){
  var h=harness(simple()),target=new Element("section");
  h.win.AlKhayalApi.request=function(url){assert.ok(url.indexOf("/api/inventory/movements?")===0);return Promise.resolve({items:[{documentId:5,documentType:"manual_receipt",createdUtc:"2026-11-14",warehouseName:"الرئيسي",productName:"شاي",variantName:"أساسي",quantityBaseScaled:1000000,notes:"purchaseInvoiceId=9",sourceInvoiceType:"purchase",sourceInvoiceId:9,sourceInvoiceNumber:"PO-2026-17"},{documentId:6,documentType:"manual_issue",createdUtc:"2026-11-14",warehouseName:"الرئيسي",productName:"شاي",variantName:"أساسي",quantityBaseScaled:-1000000,notes:"salesInvoiceId=53",sourceInvoiceType:"sale",sourceInvoiceId:53,sourceInvoiceNumber:"SAL-2026-53"}],totalCount:2});};
  h.win.__inventoryTest.historyV2(target,{id:1});await settle();
  assert.ok(target.innerHTML.includes("فاتورة مشتريات رقم PO-2026-17"));
  assert.ok(target.innerHTML.includes("فاتورة بيع رقم SAL-2026-53"));
  assert.ok(!target.innerHTML.includes("purchaseInvoiceId=")&&!target.innerHTML.includes("salesInvoiceId="));
  target.querySelector('[data-source-type="purchase"]').onclick();assert.equal(h.win.lastNavigation.route,"purchase-details");assert.equal(h.win.lastNavigation.state.invoiceId,9);
  target.querySelector('[data-source-type="sale"]').onclick();assert.equal(h.win.lastNavigation.route,"pos");assert.equal(h.win.lastNavigation.state.saleInvoiceId,53);
}
module.exports=async function(){await verifyReceipt();await verifyConditionalFields();await verifyUnitSalePriceGuidance();await verifyLiveProductPicker();await verifyOtherOperationsAndValuation();await verifyCountRetry();await verifyHistoryPresentation();var note=harness(simple()).win.__inventoryTest.movementNote;assert.ok(note({notes:'purchaseInvoiceId=9',sourceInvoiceType:'purchase',sourceInvoiceId:9,sourceInvoiceNumber:'PO-2026-17'}).includes('فاتورة مشتريات رقم PO-2026-17'));assert.ok(note({notes:'salesInvoiceId=53',sourceInvoiceType:'sale',sourceInvoiceNumber:null}).includes('المرجع غير متاح'));assert.equal(note({notes:'ملاحظة العميل'}),'ملاحظة العميل');console.log("Warehouse handler, count, tracking, unit/variant, retry, source references, direct-save and exact valuation tests passed.");};
// Reuse the deterministic DOM for other persisted workflow handler tests.
module.exports.Element=Element;
if(require.main===module)module.exports().catch(function(error){console.error(error);process.exitCode=1;});

(function numericRegression(){var h=harness(simple()),t=h.win.__inventoryTest;assert.equal(t.formatExactValue(12345678900n),"1,234,567.89");assert.equal(t.formatExactValue(-1234500n),"−123.45");assert.equal(t.formatQty(1234567),"1.234567");assert.ok(t.formatQty(-1234567).includes("-1.234567"));assert.equal(t.formatExactValue(49n),"0.00");assert.equal(t.formatExactValue(50n),"0.01");assert.equal(t.formatExactValue(-50n),"−0.01");var row={quantityBaseScaled:1000000,valueScaled:1234500,costBasisComplete:true,salePriceScaled:1234500};var result=t.valuation([row]);assert.equal(result.cost,1234500n);assert.equal(result.sales,1234500n);row.quantityBaseScaled=1100000;row.salePriceScaled=45;assert.equal(t.valuation([row]).sales,0n,"match server cent rounding at 0.00495");})();
