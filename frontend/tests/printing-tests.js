"use strict";
var assert = require('assert'), fs = require('fs'), path = require('path'), vm = require('vm');
var Element = require('./inventory-tests.js').Element;
Element.prototype.hasAttribute = function (name) { return Object.prototype.hasOwnProperty.call(this.attrs, name); };
Element.prototype.removeAttribute = function (name) { delete this.attrs[name]; };
Element.prototype.addEventListener = function (name, handler) { this['on' + name] = handler; };
var source = fs.readFileSync(path.join(__dirname, '../js/modules/printing.js'), 'utf8');
assert.ok(source.includes('data-reference-barcode') && source.includes('showReferenceBarcode'), 'receipt barcode setting must reach the saved native template');
var modules = path.join(__dirname, '../js/modules');
assert.ok(fs.readFileSync(path.join(modules, 'purchasing.js'), 'utf8').includes("AlKhayalPrinting.mount(toolbar, 'purchase', id)"), 'saved purchase details must print the displayed invoice');
assert.ok(fs.readFileSync(path.join(modules, 'delivery.js'), 'utf8').includes('g.AlKhayalPrinting.submit("delivery", Number(ticket.getAttribute("data-print-delivery"))'), 'delivery rows must route their saved sale ID as a delivery document');
assert.ok(fs.readFileSync(path.join(modules, 'renderers.js'), 'utf8').includes('"sale",invoice.id'), 'installment invoice view must print the linked posted sale');
function settle() { return new Promise(function (resolve) { setImmediate(resolve); }); }
var sections = ['company','logo','address','phone','taxInfo','header','title','reference','date','cashier','party','items','variant','unitPrice','discount','tax','quantity','lineTotal','subtotal','discountTotal','taxTotal','grandTotal','paid','remaining','footer','policy'];
function defaults() {
  return { version: 0, autoPrintSale: false, printers: [], profiles: ['sale','delivery','sales-return','purchase','purchase-return','customer-payment','supplier-payment','shift-summary','shift-closing'].map(function (t) { return { documentType:t, printerName:'', templateId:'builtin-sale', copies:1 }; }), templates:[{ id:'builtin-sale', name:'قالب', paper:{preset:'80mm',widthMm:80,heightMm:297,marginLeftMm:3,marginRightMm:3,marginTopMm:3,marginBottomMm:3,scale:1,continuous:false,landscape:false},nameColumnPercent:52,quantityColumnPercent:16,logoWidthMm:25,logoHeightMm:15,sections:sections.map(function(k){return {key:k,visible:true,bold:false,fontSize:10,spacingMm:1,alignment:'right'};}),footerText:'شكرًا',headerText:'',returnPolicy:'',dividers:true}] };
}
function harness() {
  var calls = [], pending = [], toasts = [], active = true, sequence = 0, timerId = 0, timers = {};
  var currentModal, modalOptions, win = { setTimeout:function(fn,delay){var id=++timerId;timers[id]={fn:fn,delay:delay};return id;},clearTimeout:function(id){delete timers[id];},crypto:{getRandomValues:function(bytes){bytes.fill(++sequence);}},document:{createElement:function(tag){return new Element(tag);}},AlKhayalRenderers:{},AlKhayalUtils:{escape:function(v){return String(v==null?'':v);}},AlKhayalUI:{pageHeader:function(){return '';},toast:function(message,tone){toasts.push({message:message,tone:tone});},modal:function(options){modalOptions=options;currentModal=new Element('section');currentModal.innerHTML=options.body+options.actions;options.onOpen(currentModal);return currentModal;},closeModal:function(){currentModal=null;},confirm:function(){return Promise.resolve(true);}},AlKhayalApi:{request:function(url,config){
    calls.push({url:url,config:config});
    if(config)return new Promise(function(resolve,reject){pending.push({resolve:resolve,reject:reject});});
    if(url==='/api/printing/printers')return Promise.resolve([{name:'Recording',papers:[],capabilityNote:'تعريف اختبار'}]);
    if(url.indexOf('/api/printing/documents?')===0)return Promise.resolve([{id:42,reference:'SALE-42',party:'عميل محفوظ',date:'2026-11-12',amount:'125.50'}]);
    if(url==='/api/settings/company')return Promise.resolve({businessName:'متجر الاختبار',address:'القاهرة',phone:'123'});
    return Promise.resolve(defaults());
  }}};
  vm.runInNewContext(source,{window:win,Uint8Array:Uint8Array,FileReader:function(){this.readAsDataURL=function(){this.result='data:image/png;base64,iVBORw0KGgo=';this.onload();};}});
  var context={state:{},isActive:function(){return active;}};
  return {win:win,calls:calls,pending:pending,toasts:toasts,context:context,modal:function(){return currentModal;},close:function(){if(modalOptions.beforeClose)modalOptions.beforeClose();currentModal=null;},tick:async function(maxDelay){Object.keys(timers).forEach(function(id){if(timers[id]&&timers[id].delay<=maxDelay){var task=timers[id];delete timers[id];task.fn();}});await settle();},leave:function(){active=false;},render:function(){var node=new Element('section');win.AlKhayalRenderers['native-printing']({},node,context);return node;}};
}
async function directPrinting() {
  var h=harness(),button=new Element('button');
  var a=h.win.AlKhayalPrinting.submit('sale',42,false,button),b=h.win.AlKhayalPrinting.submit('sale',42,false,button);
  assert.strictEqual(a,b);assert.equal(h.calls.length,1);assert.equal(button.disabled,true);
  var first=JSON.parse(h.calls[0].config.body);assert.equal(first.documentId,42);assert.equal(first.documentType,'sale');assert.equal(first.automatic,false);assert.equal(first.reprint,true);assert.equal(first.totalScaled,undefined);
  h.pending[0].reject({status:0,message:'فقد الرد'});await a;assert.equal(button.disabled,false);
  a=h.win.AlKhayalPrinting.submit('sale',42,false,button);assert.equal(JSON.parse(h.calls[1].config.body).operationKey,first.operationKey);
  h.pending[1].resolve({status:'submitted_unknown',message:'خروج الورق غير مؤكد'});await a;
  a=h.win.AlKhayalPrinting.submit('sale',42,false,button);assert.notEqual(JSON.parse(h.calls[2].config.body).operationKey,first.operationKey);h.pending[2].resolve({status:'submitted_unknown',message:'أُرسلت'});await a;
  a=h.win.AlKhayalPrinting.submit('sale',42,true);assert.equal(JSON.parse(h.calls[3].config.body).automatic,true);h.pending[3].resolve({status:'failed',message:'الطابعة غير متصلة'});await a;assert.equal(h.toasts[h.toasts.length-1].tone,'warning');assert.ok(h.toasts[h.toasts.length-1].message.includes('تم حفظ البيع؛ تعذر طباعة الفاتورة'));
  var count=h.toasts.length;a=h.win.AlKhayalPrinting.submit('sale',43,true);h.pending[4].resolve({status:'disabled'});await a;assert.equal(h.toasts.length,count);
  assert.ok(!source.includes('window.print')&&!source.includes('createObjectURL')&&!source.includes('.pdf'));
}
async function designerAndPersistence() {
  var h=harness(),node=h.render();await settle();await settle();var host=node.querySelector('[data-printer-manager]');
  assert.ok(host.innerHTML.includes('لا توجد طابعات مضافة'),host.innerHTML);assert.ok(host.innerHTML.includes('إيصال توصيل'));
  await host.onclick({target:host.querySelector('[data-add-printer]')});var modal=h.modal();
  assert.ok(modal.innerHTML.includes('متجر الاختبار'));assert.ok(modal.innerHTML.includes('اختر المقاس'));
  modal.querySelector('[data-friendly]').value='طابعة الكاشير';modal.querySelector('[data-windows-printer]').value='Recording';
  var paper=modal.querySelector('[data-paper]');paper.value='58mm';paper.onchange();assert.equal(modal.querySelector('[data-width]').value,58);
  var logo=modal.querySelector('[data-logo]');logo.files=[{type:'image/png',size:12}];logo.onchange();assert.equal(modal.querySelector('[data-logo-preview]').hidden,false);
  await h.tick(320);assert.equal(h.calls[h.calls.length-1].url,'/api/printing/preview/branding');
  assert.equal(JSON.parse(h.calls[h.calls.length-1].config.body).template.logoPngBase64,'iVBORw0KGgo=');
  h.pending[0].resolve({pngBase64:'abc'});await settle();assert.equal(modal.querySelector('[data-preview-image]').src,'data:image/png;base64,abc');
  var task=modal.querySelector('[data-modal-save]').onclick();var saved=JSON.parse(h.calls[h.calls.length-1].config.body);
  assert.equal(saved.printers[0].windowsPrinterName,'Recording');assert.equal(saved.templates[1].paper.widthMm,58);
  h.pending[1].resolve(Object.assign({version:1},saved));await task;assert.ok(host.innerHTML.includes('طابعة الكاشير'));
  var assignment=host.querySelector('[data-assignment="1"]');assignment.value=saved.printers[0].id;host.onchange({target:assignment});
  task=host.onclick({target:host.querySelector('[data-save-assignments]')});saved=JSON.parse(h.calls[h.calls.length-1].config.body);assert.equal(saved.profiles[1].documentType,'delivery');assert.equal(saved.profiles[1].printerId,saved.printers[0].id);h.pending[2].resolve(Object.assign({version:2},saved));await task;
  task=host.onclick({target:host.querySelector('[data-remove-printer]')});await settle();saved=JSON.parse(h.calls[h.calls.length-1].config.body);assert.equal(saved.printers.length,0);assert.equal(saved.profiles[1].printerId,null);h.pending[3].resolve(Object.assign({version:3},saved));await task;
  var css=fs.readFileSync(path.join(__dirname,'../css/printing.css'),'utf8');assert.ok(css.includes('var(--surface)')&&css.includes('var(--border)'));assert.ok(fs.readFileSync(path.join(__dirname,'../index.html'),'utf8').includes('dir="rtl"'));
}
async function livePreview() {
  var h=harness(),node=h.render();await settle();await settle();var host=node.querySelector('[data-printer-manager]');
  await host.onclick({target:host.querySelector('[data-add-printer]')});var modal=h.modal(),image=modal.querySelector('[data-preview-image]');
  var requests=function(){return h.calls.filter(function(x){return x.url==='/api/printing/preview/branding';});};
  var paper=modal.querySelector('[data-paper]');paper.value='58mm';paper.onchange();
  assert.equal(requests().length,0,'preview is debounced');await h.tick(320);
  assert.equal(requests().length,1);assert.equal(JSON.parse(requests()[0].config.body).template.paper.widthMm,58);
  h.pending[0].resolve({pngBase64:'first'});await settle();assert.equal(image.src,'data:image/png;base64,first');
  var logo=modal.querySelector('[data-logo]');logo.files=[{type:'image/png',size:12}];logo.onchange();await h.tick(320);
  assert.equal(JSON.parse(requests()[1].config.body).template.logoPngBase64,'iVBORw0KGgo=','new unsaved PNG is sent immediately');
  h.pending[1].resolve({pngBase64:'uploaded'});await settle();assert.equal(image.src,'data:image/png;base64,uploaded');
  modal.querySelector('[data-remove-logo]').onclick();await h.tick(320);
  assert.equal(JSON.parse(requests()[2].config.body).template.logoPngBase64,null);
  h.pending[2].resolve({pngBase64:'fallback'});await settle();assert.equal(image.src,'data:image/png;base64,fallback');
  var show=modal.querySelector('[data-show-logo]');show.checked=false;show.onchange();await h.tick(320);
  assert.equal(JSON.parse(requests()[3].config.body).template.sections.find(function(s){return s.key==='logo';}).visible,false);
  h.pending[3].resolve({pngBase64:'hidden-logo'});await settle();
  paper.value='80mm';paper.onchange();await h.tick(320);
  assert.equal(JSON.parse(requests()[4].config.body).template.paper.widthMm,80);
  h.pending[4].resolve({pngBase64:'wide'});await settle();assert.equal(image.src,'data:image/png;base64,wide');
  var type=modal.querySelector('[data-preview-type]');type.value='delivery';type.onchange();await h.tick(320);
  assert.equal(JSON.parse(requests()[5].config.body).documentType,'delivery');
  h.pending[5].resolve({pngBase64:'delivery'});await settle();
  var footer=modal.querySelector('[data-footer]');footer.value='نص جديد';footer.oninput();footer.value='نص أحدث';footer.oninput();
  await h.tick(320);assert.equal(requests().length,7,'rapid edits share a single request');
  assert.equal(JSON.parse(requests()[6].config.body).template.footerText,'نص أحدث');
  footer.value='الأحدث';footer.oninput();await h.tick(320);assert.equal(requests().length,8);
  h.pending[7].resolve({pngBase64:'newest'});await settle();h.pending[6].resolve({pngBase64:'stale'});await settle();
  assert.equal(image.src,'data:image/png;base64,newest','late response cannot replace newest preview');
  footer.value='فشل المعاينة';footer.oninput();await h.tick(320);h.pending[8].reject(new Error('مقاس غير صالح'));await settle();
  assert.equal(image.src,'data:image/png;base64,newest');assert.equal(image.getAttribute('data-stale'),'true');
  assert.ok(modal.querySelector('[data-preview-status]').textContent.includes('الصورة السابقة'));
  assert.ok(modal.querySelector('[data-modal-error]').textContent.includes('مقاس غير صالح'));
  assert.equal(h.calls.filter(function(x){return x.url==='/api/printing/settings'&&x.config;}).length,0,'preview never saves');
  assert.equal(h.calls.filter(function(x){return x.url==='/api/printing/jobs';}).length,0,'preview never prints');
  footer.value='بعد الإغلاق';footer.oninput();await h.tick(320);h.close();h.pending[9].resolve({pngBase64:'after-close'});await settle();
  assert.notEqual(image.src,'data:image/png;base64,after-close');
}
async function otherPreviewControls() {
  var h=harness(),node=h.render();await settle();await settle();var host=node.querySelector('[data-printer-manager]');
  await host.onclick({target:host.querySelector('[data-add-printer]')});var modal=h.modal(),paper=modal.querySelector('[data-paper]');
  async function changed(node,event,expected) { node[event]();await h.tick(320);var call=h.calls.filter(function(x){return x.url==='/api/printing/preview/branding';}).pop();var body=JSON.parse(call.config.body);expected(body);h.pending[h.pending.length-1].resolve({pngBase64:'updated'});await settle(); }
  paper.value='A4';await changed(paper,'onchange',function(body){assert.equal(body.template.paper.widthMm,210);});
  paper.value='A5';await changed(paper,'onchange',function(body){assert.equal(body.template.paper.widthMm,148);});
  paper.value='custom';await changed(paper,'onchange',function(body){assert.equal(body.template.paper.preset,'custom');});
  var width=modal.querySelector('[data-width]');width.value='92';await changed(width,'oninput',function(body){assert.equal(body.template.paper.widthMm,92);});
  var header=modal.querySelector('[data-header]');header.value='رأس جديد';await changed(header,'oninput',function(body){assert.equal(body.template.headerText,'رأس جديد');});
  var margin=modal.querySelector('[data-margin]');margin.value='5';await changed(margin,'oninput',function(body){assert.equal(body.template.paper.marginRightMm,5);});
  var scale=modal.querySelector('[data-scale]');scale.value='1.2';await changed(scale,'oninput',function(body){assert.equal(body.template.paper.scale,1.2);});
  var continuous=modal.querySelector('[data-continuous]');continuous.checked=true;await changed(continuous,'onchange',function(body){assert.equal(body.template.paper.continuous,true);});
  var mode=modal.querySelector('[data-preview-mode]'), actual=modal.querySelector('[data-actual-document]');
  mode.value='actual';mode.onchange();actual.value='42';actual.oninput();await h.tick(320);
  var documentCall=h.calls.filter(function(x){return x.url==='/api/printing/preview/document';}).pop();
  assert.ok(documentCall,'saved document preview uses the actual-document endpoint');
  assert.equal(JSON.parse(documentCall.config.body).documentId,42);
  h.pending[h.pending.length-1].resolve({pngBase64:'actual'});await settle();
  assert.ok(modal.querySelector('[data-preview-status]').textContent.includes('المستند المحفوظ'));
  assert.equal(h.calls.filter(function(x){return x.url==='/api/printing/settings'&&x.config;}).length,0);
}
async function documentSpecificDesigner() {
  var h=harness(),node=h.render();await settle();await settle();var host=node.querySelector('[data-printer-manager]');
  await host.onclick({target:host.querySelector('[data-design-document="sale"]')});var modal=h.modal();
  assert.ok(modal.innerHTML.includes('هوية المنشأة والرأس'));
  await h.tick(0);var sample=h.calls.filter(function(x){return x.url==='/api/printing/preview/branding';}).pop();
  assert.equal(JSON.parse(sample.config.body).documentType,'sale');h.pending[0].resolve({pngBase64:'sample'});await settle();
  var section=modal.querySelector('[data-doc-key="cashier"]');section.checked=false;
  modal.querySelector('[class="receipt-designer-grid"]').oninput({target:section});await h.tick(320);
  var changed=h.calls.filter(function(x){return x.url==='/api/printing/preview/branding';}).pop();
  assert.equal(JSON.parse(changed.config.body).template.sections.find(function(s){return s.key==='cashier';}).visible,false);
  h.pending[1].resolve({pngBase64:'changed'});await settle();
  var mode=modal.querySelector('[data-doc-mode]');mode.value='actual';mode.onchange();await h.tick(0);
  var pickerCall=h.calls.filter(function(x){return x.url.indexOf('/api/printing/documents?type=sale')===0;}).pop();assert.ok(pickerCall);
  var choice=modal.querySelector('[data-doc-choice="42"]');assert.ok(choice);modal.querySelector('[data-doc-results]').onclick({target:choice});await h.tick(0);
  var actual=h.calls.filter(function(x){return x.url==='/api/printing/preview/document';}).pop();
  assert.equal(JSON.parse(actual.config.body).documentId,42);h.pending[2].resolve({pngBase64:'actual'});await settle();
  var save=modal.querySelector('[data-doc-save]').onclick();var call=h.calls.filter(function(x){return x.url==='/api/printing/settings'&&x.config;}).pop();
  var data=JSON.parse(call.config.body),profile=data.profiles.find(function(p){return p.documentType==='sale';});
  assert.equal(profile.useDocumentTemplate,true);assert.equal(profile.templateId,'document-template-sale');
  assert.equal(data.templates.find(function(t){return t.id===profile.templateId;}).sections.find(function(s){return s.key==='cashier';}).visible,false);
  h.pending[3].resolve(Object.assign({version:1},data));await save;
}
async function run(){await directPrinting();await designerAndPersistence();await livePreview();await otherPreviewControls();await documentSpecificDesigner();console.log('Native print transport, document-specific templates, live unsaved preview, logo/paper/type/branding changes, debounce, stale response, failure and cleanup tests passed.');}
if(require.main===module)run().catch(function(e){console.error(e);process.exitCode=1;});
module.exports=run;
