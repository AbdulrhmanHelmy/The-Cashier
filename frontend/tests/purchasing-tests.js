"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");
var Element = require("./inventory-tests.js").Element;
Object.defineProperty(Element.prototype, "id", { get: function () { return this.attrs.id || ""; }, configurable: true });
var source = fs.readFileSync(path.join(__dirname, "../js/modules/purchasing.js"), "utf8");
function settle() { return new Promise(function (resolve) { setImmediate(resolve); }); }
function delay(ms) { return new Promise(function (resolve) { setTimeout(resolve, ms); }); }
function product(id, name) { return { id: id, name: name, productType: "stock", isActive: true, isArchived: false }; }
function harness(initialState) {
  var calls = [], searches = [], supplierSearches = [], writes = [], pending = [], untrackedSnapshots = [], active = true;
  var container = new Element("section");
  container.contains = function (node) { for (; node; node = node.parent) if (node === container) return true; return false; };
  var api = { untrackPageSnapshot:function(route,path){untrackedSnapshots.push({route:route,path:path});}, request: function (url, config) {
    calls.push({ url: url, config: config });
    if (config) { writes.push(JSON.parse(config.body)); return new Promise(function (resolve, reject) { pending.push({resolve:resolve,reject:reject}); }); }
    if (url.indexOf("/api/purchases/") === 0) return new Promise(function (resolve, reject) { pending.push({url:url,resolve:resolve,reject:reject}); });
    if (url.indexOf("/api/suppliers?") === 0) return new Promise(function (resolve, reject) { supplierSearches.push({ url: url, resolve: resolve, reject: reject }); });
    if (url === "/api/suppliers/2") return Promise.resolve({id:2,name:"مورد",phone:"01012345678",payable:0});
    if (url === "/api/inventory/warehouses") return Promise.resolve([{ id: 1, name: "مخزن", isActive: true }]);
    if (url === "/api/treasury/accounts") return Promise.resolve([{id:9,name:"الخزنة",type:"cash",balance:"100.00"},{id:10,name:"البنك",type:"bank",balance:"200.00"}]);
    if (url.indexOf("/api/inventory/products?") === 0) return new Promise(function (resolve, reject) { searches.push({ url: url, resolve: resolve, reject: reject }); });
    if (url === "/api/inventory/products/7") return Promise.resolve({ id: 7, name: "صنف فعلي", purchasePrice: "10.10", salePrice: "12.20", capabilities: [], variants: [{ id: 71, name: "افتراضي", isActive: true }], units: [{ id: 72, name: "قطعة", isActive: true, isBase: true, isPurchaseUnit: true }] });
    if (url === "/api/inventory/products/8") return Promise.resolve({ id: 8, name: "حليب", purchasePrice: "20.00", salePrice: "30.00", capabilities: [{key:"batch-tracking",isAssigned:true,state:"assigned-but-disabled"},{key:"expiry-dates",isAssigned:true,state:"assigned-but-disabled"}], variants: [{ id: 81, name: "افتراضي", isActive: true }], units: [{ id: 82, name: "قطعة", isActive: true, isBase: true, isPurchaseUnit: true }] });
    throw new Error("Unexpected URL: " + url);
  } };
  var win = { AlKhayalUtils: { escape: function (x) { return String(x == null ? "" : x); }, money: String, debounce: function (fn) { return fn; } }, AlKhayalUI: { pageHeader: function () { return ""; }, toast: function () {} }, AlKhayalRenderers: {} };
  vm.runInNewContext(source, { window: win, AlKhayalApi: api, BigInt: BigInt, setTimeout: setTimeout, clearTimeout: clearTimeout }, { filename: "purchasing.js" });
  win.AlKhayalRenderers["purchase-management"]({route:"purchase-management"}, container, { state: initialState || {}, isActive: function () { return active; }, navigate:function(){} });
  return { node: container, calls: calls, searches: searches, supplierSearches: supplierSearches, writes: writes, pending: pending, untrackedSnapshots:untrackedSnapshots, leave: function () { active = false; }, click: function (selector) { container.onclick({ target: container.querySelector(selector) }); } };
}
async function browseAndSelect() {
  var h = harness(); await settle();
  var search = h.node.querySelector("#purchaseSearch"), results = h.node.querySelector("#purchaseResults");
  assert.equal(h.searches.length, 0, "opening the page must not download or expand the catalog");
  assert.equal(results.hidden, true);assert.ok(h.node.innerHTML.includes("لم تُضف أصناف بعد"));
  search.value="صنف";search.oninput();
  assert.ok(h.searches[0].url.includes("search=%D8%B5%D9%86%D9%81&page=1&pageSize=20"));
  h.searches[0].resolve({ items: [product(7, "صنف فعلي"), { id: 8, name: "مؤرشف", productType: "stock", isActive: true, isArchived: true }], totalCount: 21 }); await settle();
  assert.equal(results.hidden, false); assert.ok(results.innerHTML.includes("صنف فعلي")); assert.ok(!results.innerHTML.includes("مؤرشف"));
  h.click('[data-purchase-products-page="2"]'); assert.ok(h.searches[1].url.includes("page=2"));
  h.searches[1].resolve({ items: [product(7, "صنف فعلي")], totalCount: 21 }); await settle();
  assert.strictEqual(h.node.querySelector("#purchaseSearch"), search, "pagination must preserve the mounted search");
  h.click('[data-add-product="7"]'); await settle();
  assert.ok(h.node.querySelector("#purchaseLines").querySelector('[data-line="0"]'), "selection must create an invoice line");
  assert.equal(h.node.querySelector('[data-line-field="purchaseUnitPrice"]').value, "10.10");
  assert.equal(h.node.querySelector('[data-line-field="saleUnitPrice"]').value, "12.20");
  h.click("[data-purchase-save]");
  assert.equal(h.writes[0].warehouseId, 1);
  assert.equal(h.writes[0].lines[0].variantId, 71); assert.equal(h.writes[0].lines[0].productUnitId, 72);
  assert.equal(h.writes[0].lines[0].purchaseUnitPrice, "10.10");
}
async function searchAndRetry() {
  var h = harness(); await settle();
  var input = h.node.querySelector("#purchaseSearch");
  input.value = "ق"; input.oninput(); assert.equal(h.searches.length, 1, "single-character search must run");
  h.searches[0].resolve({ items: [product(1, "نتيجة قديمة")], totalCount: 1 }); await settle();
  assert.ok(h.node.querySelector("#purchaseResults").innerHTML.includes("نتيجة قديمة"));
  input.value = "قميص"; input.oninput();
  input.value = "قميص حديث"; input.oninput();
  h.searches[2].resolve({ items: [product(2, "قميص حديث")], totalCount: 1 }); await settle();
  h.searches[1].resolve({ items: [product(3, "رد متأخر")], totalCount: 1 }); await settle();
  assert.ok(h.node.querySelector("#purchaseResults").innerHTML.includes("قميص حديث"));
  assert.ok(!h.node.querySelector("#purchaseResults").innerHTML.includes("رد متأخر"));
  assert.strictEqual(h.node.querySelector("#purchaseSearch"), input);
  input.value = ""; input.oninput(); assert.equal(h.searches.length,3);assert.equal(h.node.querySelector("#purchaseResults").hidden,true,"clearing search closes results without fetching the full catalog");
  input.value = "خطأ"; input.oninput();h.searches[3].reject({ message: "تعذر الاتصال" }); await settle();
  h.click("[data-purchase-products-retry]"); assert.ok(h.searches[4].url.includes("search=%D8%AE%D8%B7%D8%A3&page=1"));
  h.searches[4].resolve({ items: [], totalCount: 0 }); await settle();
  assert.ok(h.node.querySelector("#purchaseResults").innerHTML.includes("لا توجد أصناف مطابقة"));
  input.value = "جديد"; input.oninput(); h.leave();
  var html = h.node.querySelector("#purchaseResults").innerHTML;
  h.searches[5].resolve({ items: [product(4, "رد بعد المغادرة")], totalCount: 1 }); await settle();
  assert.equal(h.node.querySelector("#purchaseResults").innerHTML, html, "navigation must discard late product responses");
}
async function cashSelection() {
  var h=harness();await settle();
  assert.ok(h.untrackedSnapshots.some(function(x){return x.route==="purchase-management"&&x.path==="/api/treasury/accounts";}),"cash account balances are excluded from freshness checks while the purchase form is in credit mode");
  var productSearch=h.node.querySelector("#purchaseSearch");productSearch.value="صنف";productSearch.oninput();
  h.searches[0].resolve({items:[product(7,"صنف فعلي")],totalCount:1});await settle();
  h.click('[data-add-product="7"]');await settle();
  assert.ok(h.node.querySelector("#purchaseTreasuryField").hidden);
  var mode=h.node.querySelector("#purchaseMode");mode.value="cash";h.node.onchange({target:mode});
  await settle();assert.ok(h.calls.filter(function(x){return x.url==="/api/treasury/accounts";}).length>=2,"switching to cash reloads accounts and establishes a relevant balance snapshot");
  assert.ok(!h.node.querySelector("#purchaseTreasuryField").hidden);
  assert.ok(h.node.querySelector("#purchaseTreasury").required);
  h.click("[data-purchase-save]");assert.equal(h.writes.length,0,"cash requires explicit account selection");
  var account=h.node.querySelector("#purchaseTreasury");account.value="9";h.node.onchange({target:account});
  h.click("[data-purchase-save]");h.click("[data-purchase-save]");assert.equal(h.writes.length,1);
  assert.equal(h.writes[0].treasuryAccountId,9);assert.equal(h.writes[0].paymentMode,"cash");
  h.pending[0].resolve({id:5,status:"draft",paymentMode:"cash",treasuryAccountId:9,lines:h.writes[0].lines});await settle();
  assert.equal(h.node.querySelector("#purchaseTreasury").value,"9","saved account must survive redraw");
  assert.ok(!h.node.querySelector("[data-purchase-save]").disabled,"successful save must release busy state");
  account=h.node.querySelector("#purchaseTreasury");account.value="10";h.node.onchange({target:account});
  h.click("[data-purchase-post]");assert.equal(h.writes.length,1,"changed account must be saved before posting");
  mode=h.node.querySelector("#purchaseMode");mode.value="credit";h.node.onchange({target:mode});
  assert.ok(h.untrackedSnapshots.length>=2,"switching back to credit removes treasury balances from the page freshness snapshot");
  assert.ok(h.node.querySelector("#purchaseTreasuryField").hidden);assert.equal(h.node.querySelector("#purchaseTreasury").value,"");
  h.click("[data-purchase-save]");assert.equal(h.writes[1].treasuryAccountId,null);assert.equal(h.writes[1].paymentMode,"credit");
}
async function supplierSearch(){var h=harness();await settle();var search=h.node.querySelector("#purchaseSupplierSearch"),results=h.node.querySelector("#purchaseSupplierResults");assert.equal(search.value,"");assert.ok(!h.calls.some(function(call){return call.url==="/api/suppliers";}),"opening a purchase must not download the full supplier list");search.value="010123";search.oninput();assert.ok(results.innerHTML.includes("جارٍ البحث عن الموردين"),"supplier search immediately displays its loading state");await delay(270);assert.equal(h.supplierSearches.length,1);assert.ok(h.supplierSearches[0].url.includes("search=010123&page=1&pageSize=20"),"supplier search must request a bounded server-side result page");h.supplierSearches[0].resolve({items:[{id:2,name:"مورد",phone:"01012345678",payable:0}],totalCount:21});await settle();assert.ok(results.innerHTML.includes("مورد")&&results.innerHTML.includes("عرض المزيد (1 من 21)"),"matching suppliers render with a progressive more-results action");h.click("[data-more-suppliers]");assert.ok(results.innerHTML.includes("جارٍ البحث عن الموردين"));assert.ok(h.supplierSearches[1].url.includes("page=2&pageSize=20"));h.supplierSearches[1].resolve({items:[{id:21,name:"مورد إضافي",phone:"01012399999",payable:25}],totalCount:21});await settle();assert.ok(results.innerHTML.includes("مورد إضافي"),"next page appends to the current supplier matches");h.click('[data-select-supplier="21"]');assert.equal(h.node.querySelector("#purchaseSupplier").value,"21");assert.equal(h.node.querySelector("#purchaseSupplierSearch").value,"مورد إضافي");assert.ok(h.node.innerHTML.includes("المورد المختار"),"selection is presented explicitly");h.click("[data-purchase-save]");assert.equal(h.writes[0].supplierId,21,"selected supplier id must be saved with the invoice");}
async function trackingFields(){var h=harness();await settle();var search=h.node.querySelector("#purchaseSearch");search.value="حليب";search.oninput();h.searches[0].resolve({items:[product(8,"حليب")],totalCount:1});await settle();h.click('[data-add-product="8"]');await settle();assert.equal(h.node.querySelector('[data-line-field="lotNumber"]'),null,"lot numbers are internal only");assert.ok(h.node.querySelector('[data-line-field="expirationDate"]'),"expiry assignment shows an expiry field");h.click("[data-purchase-save]");assert.equal(h.writes.length,0,"missing expiry must be rejected");var expiry=h.node.querySelector('[data-line-field="expirationDate"]'),quantity=h.node.querySelector('[data-line-field="quantity"]');expiry.value="2027-06-15";expiry.onchange({target:expiry});quantity.value="20";quantity.onchange({target:quantity});h.click("[data-purchase-save]");assert.equal(h.writes[0].lines[0].lotNumber,null);assert.equal(h.writes[0].lines[0].expirationDate,"2027-06-15");}
async function productKeyboardAndDuplicates(){var h=harness();await settle();var input=h.node.querySelector("#purchaseSearch"),prevented=0;input.value="باركود";input.oninput();h.searches[0].resolve({items:[product(8,"حليب"),product(7,"صنف فعلي")],totalCount:2});await settle();input.onkeydown({key:"ArrowDown",preventDefault:function(){prevented++;}});input.onkeydown({key:"Enter",preventDefault:function(){prevented++;}});await settle();await delay(1);assert.equal(h.node.querySelectorAll('[data-line-field="quantity"]').length,1,"Enter adds the highlighted result");assert.ok(h.node.innerHTML.includes("صنف فعلي"));assert.equal(prevented,2,"search navigation prevents accidental form submission");input=h.node.querySelector("#purchaseSearch");input.value="صنف";input.oninput();h.searches[1].resolve({items:[product(7,"صنف فعلي")],totalCount:1});await settle();h.click('[data-add-product="7"]');await settle();await delay(1);assert.equal(h.node.querySelectorAll('[data-line-field="quantity"]').length,2,"adding the same product preserves the existing duplicate-line behavior");input=h.node.querySelector("#purchaseSearch");input.value="إغلاق";input.oninput();h.searches[2].resolve({items:[product(7,"صنف فعلي")],totalCount:1});await settle();input.onkeydown({key:"Escape",preventDefault:function(){prevented++;}});assert.equal(h.node.querySelector("#purchaseResults").hidden,true,"Escape closes only the result panel");assert.equal(h.node.querySelectorAll('[data-line-field="quantity"]').length,2,"Escape never clears invoice lines");}
async function resumeDraft(){var h=harness({invoiceId:11});await settle();var read=h.pending.filter(function(x){return x.url==="/api/purchases/11";})[0];assert.ok(read,"the exact route-state invoice id must be requested");read.resolve({id:11,status:"draft",supplierId:2,warehouseId:1,invoiceNumber:"SUP-11",invoiceDate:"2026-11-03",paymentMode:"cash",treasuryAccountId:10,notes:"ملاحظات محفوظة",lines:[{id:101,productId:7,variantId:71,productUnitId:72,quantity:"2.5",purchaseUnitPrice:"10.10",saleUnitPrice:"12.20",lotNumber:null,expirationDate:null,physicalUnits:[],lineTotalScaled:"252500"},{id:102,productId:8,variantId:81,productUnitId:82,quantity:"3",purchaseUnitPrice:"20.00",saleUnitPrice:"30.00",lotNumber:"LOT-11",expirationDate:"2027-06-15",physicalUnits:[{serial:"SERIAL-11",imeis:["352099001761481"]}],lineTotalScaled:"600000"}]});await settle();await settle();assert.equal(h.node.querySelector("#purchaseSupplier").value,"2");assert.equal(h.node.querySelector("#purchaseWarehouse").value,"1");assert.equal(h.node.querySelector("#purchaseNumber").value,"SUP-11");assert.equal(h.node.querySelector("#purchaseDate").value,"2026-11-03");assert.equal(h.node.querySelector("#purchaseMode").value,"cash");assert.equal(h.node.querySelector("#purchaseTreasury").value,"10");assert.equal(h.node.querySelector("#purchaseNotes").value,"ملاحظات محفوظة");var quantities=h.node.querySelectorAll('[data-line-field="quantity"]'),units=h.node.querySelectorAll('[data-line-field="unit"]');assert.equal(quantities.length,2,"all saved lines must be restored in order");assert.equal(quantities[0].value,"2.5");assert.equal(units[1].value,"82");assert.equal(h.node.querySelector('[data-line-field="lotNumber"]'),null);assert.equal(h.node.querySelector('[data-line-field="expirationDate"]').value,"2027-06-15");assert.ok(h.node.innerHTML.includes("SERIAL-11|352099001761481"),"persisted serial and IMEI values must remain editable even if capabilities later change");var notes=h.node.querySelector("#purchaseNotes");notes.value="بعد التعديل";h.node.oninput({target:notes});h.click("[data-purchase-save]");assert.equal(h.calls[h.calls.length-1].url,"/api/purchases/11","saving a resumed draft must update the same id");assert.equal(h.calls[h.calls.length-1].config.method,"PUT");assert.equal(h.writes[0].lines.length,2);assert.equal(h.writes[0].notes,"بعد التعديل");assert.equal(h.writes[0].lines[1].physicalUnits[0].imeis[0],"352099001761481");}
function draft(id,number,status){return{id:id,status:status||"draft",supplierId:2,warehouseId:1,invoiceNumber:number,invoiceDate:"2026-11-03",paymentMode:"credit",treasuryAccountId:null,notes:number,lines:[{id:id*10,productId:7,variantId:71,productUnitId:72,quantity:"1",purchaseUnitPrice:"10.10",saleUnitPrice:"12.20",physicalUnits:[],lineTotalScaled:"101000"}]};}
async function switchDraftsAndRejectLateLoads(){var first=harness({invoiceId:21});await settle();var firstRead=first.pending.filter(function(x){return x.url==="/api/purchases/21";})[0];first.leave();var second=harness({invoiceId:22});await settle();var secondRead=second.pending.filter(function(x){return x.url==="/api/purchases/22";})[0];secondRead.resolve(draft(22,"DRAFT-22"));await settle();await settle();assert.equal(second.node.querySelector("#purchaseNumber").value,"DRAFT-22","the newly selected draft must win");var oldHtml=first.node.innerHTML;firstRead.resolve(draft(21,"DRAFT-21"));await settle();await settle();assert.equal(first.node.innerHTML,oldHtml,"a late response from the replaced draft view must be discarded");assert.equal(second.node.querySelector("#purchaseNumber").value,"DRAFT-22");}
async function rejectNonDraftAndMissingIds(){var posted=harness({invoiceId:31});await settle();posted.pending.filter(function(x){return x.url==="/api/purchases/31";})[0].resolve(draft(31,"POSTED-31","posted"));await settle();assert.ok(posted.node.innerHTML.includes("لم تعد مسودة"),"posted invoices must not reopen as editable drafts");var missing=harness({invoiceId:32});await settle();missing.pending.filter(function(x){return x.url==="/api/purchases/32";})[0].reject({message:"الفاتورة غير موجودة."});await settle();assert.ok(missing.node.innerHTML.includes("الفاتورة غير موجودة"),"missing draft ids must fail safely");var invalid=harness({invoiceId:"bad"});await settle();assert.ok(invalid.node.innerHTML.includes("معرّف مسودة الشراء غير صالح"),"invalid draft ids must fail safely without an API request");}
function navigationContract(){assert.ok(source.indexOf('context.navigate("purchase-management",{invoiceId:Number(edit.getAttribute("data-edit-purchase"))})')>=0,"resume must pass the clicked id through route state");assert.ok(source.indexOf('context.navigate("purchase-details",{invoiceId:Number(view.getAttribute("data-view-purchase"))})')>=0,"view action must pass the exact invoice id");assert.ok(source.indexOf('global.AlKhayalRenderers["purchase-details"] = renderPurchaseDetails')>=0,"one read-only renderer handles all invoice references");assert.ok(source.indexOf("if(edit)edit.onclick=function(){context.navigate('purchase-management',{invoiceId:id});}")>=0,"only eligible drafts expose editing from details");assert.ok(source.indexOf('context.navigate("purchase-management",{invoiceId:null})')>=0,"new purchase must explicitly clear an old draft id");assert.ok(source.indexOf("token!==state.loadToken")>=0,"late draft responses must be rejected");assert.ok(source.indexOf("beforeRouteReplace")>=0,"draft replacement must expose unsaved-change protection");}
module.exports = async function () { await browseAndSelect(); await searchAndRetry(); await cashSelection(); await supplierSearch(); await trackingFields(); await productKeyboardAndDuplicates(); await resumeDraft(); await switchDraftsAndRejectLateLoads(); await rejectNonDraftAndMissingIds(); navigationContract(); console.log("Purchasing compact search, keyboard selection, duplicate behavior, exact draft resume/update, stale-response rejection, complete field restoration and browse tests passed."); };
if (require.main === module) module.exports().catch(function (error) { console.error(error); process.exitCode = 1; });
