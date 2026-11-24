'use strict';
var assert=require('assert'),fs=require('fs'),path=require('path'),vm=require('vm'),Element=require('./inventory-tests.js').Element;
Element.prototype.hasAttribute=function(n){return Object.prototype.hasOwnProperty.call(this.attrs,n);};Element.prototype.removeAttribute=function(n){delete this.attrs[n];};
if(!Object.getOwnPropertyDescriptor(Element.prototype,'style'))Object.defineProperty(Element.prototype,'style',{get:function(){return this._style||(this._style={});}});
var source=fs.readFileSync(path.join(__dirname,'../js/modules/labels.js'),'utf8');
function settle(){return new Promise(function(resolve){setImmediate(resolve);});}
function template(){return {id:'label-basic',name:'ملصق',stock:{labelWidthMm:50,labelHeightMm:30,pageWidthMm:50,pageHeightMm:30,leftMm:0,rightMm:0,topMm:0,bottomMm:0,horizontalGapMm:0,verticalGapMm:0,columns:1,rows:1,landscape:false,continuous:false},elements:['productName','company','productCode','sku','barcode','barcodeText','price','unit','category','size','color','static','logo'].map(function(k){return {key:k,visible:k==='productName'||k==='barcode',x:1,y:k==='barcode'?8:1,width:48,height:k==='barcode'?10:6,fontSize:8,alignment:'right',moduleMm:.2};})};}
function settings(){return {version:0,defaultProfileId:'labels-default',profiles:[{id:'labels-default',name:'طابعة',printerName:'Labels',templateId:'label-basic',copies:1,paper:{pageWidthMm:50,pageHeightMm:30}}],templates:[template()]};}
function product(id){return {id:id,name:'منتج '+id,version:1,units:[{id:100+id,name:'قطعة',isBase:true,isActive:true,isSaleUnit:true}],variants:[{id:200+id,name:'XL / أزرق',isActive:true,isDefault:false,barcodes:[{id:300+id,code:'2912345678',format:'code128',productUnitId:100+id,isRetired:false}]}]};}
function harness(){var calls=[],pending=[],searches=[],toasts=[],seq=0,isActive=true,timers=[];
 var win={document:{createElement:function(tag){return new Element(tag);}},crypto:{getRandomValues:function(a){a.fill(++seq);}},AlKhayalRenderers:{},AlKhayalUtils:{escape:function(v){return String(v==null?'':v);}},AlKhayalUI:{pageHeader:function(){return '';},toast:function(m){toasts.push(m);}},AlKhayalApi:{request:function(url,config){calls.push({url:url,config:config});if(config)return new Promise(function(resolve,reject){pending.push({resolve:resolve,reject:reject});});if(url==='/api/labels/settings'||url==='/api/labels/defaults')return Promise.resolve(settings());if(url==='/api/labels/options')return Promise.resolve(settings());if(url.indexOf('/api/labels/product-info/')===0)return Promise.resolve({fields:{price:'12.50 ج.م',unit:'قطعة'}});if(url.indexOf('/api/catalog/products?')===0)return new Promise(function(resolve,reject){searches.push({resolve:resolve,reject:reject});});if(url.indexOf('/api/catalog/products/')===0)return Promise.resolve(product(Number(url.split('/').pop())));throw new Error('Unexpected '+url);}}};
 win.setTimeout=function(f){timers.push(f);return timers.length;};win.clearTimeout=function(){};
 vm.runInNewContext(source,{window:win,Uint8Array:Uint8Array,FileReader:function(){},setTimeout:win.setTimeout,clearTimeout:win.clearTimeout});
 return {win:win,calls:calls,pending:pending,searches:searches,toasts:toasts,context:{state:{},isActive:function(){return isActive;}},leave:function(){isActive=false;},runTimer:function(){timers.pop()();}};
}
async function transport(){var h=harness(),s={items:[{barcodeId:301,quantity:3,format:'code128'}],profileId:'labels-default'},button=new Element('button');var a=h.win.AlKhayalLabels.sendBatch(s,button);h.win.AlKhayalLabels.sendBatch(s,button);assert.equal(h.calls.length,1);var first=JSON.parse(h.calls[0].config.body);assert.equal(first.items[0].barcodeId,301);assert.equal(first.items[0].quantity,3);assert.equal(first.items[0].price,undefined);assert.equal(button.disabled,true);
 h.pending[0].reject({status:0,message:'فقد الرد'});await a;assert.ok(s.pending);s.items[0].quantity=9;a=h.win.AlKhayalLabels.sendBatch(s,button);assert.deepStrictEqual(JSON.parse(h.calls[1].config.body),first);h.pending[1].resolve({id:1,status:'submitted_unknown',message:'خروج الورق غير مؤكد'});await a;assert.equal(s.pending,null);a=h.win.AlKhayalLabels.sendBatch(s,button);var intentional=JSON.parse(h.calls[2].config.body);assert.notEqual(intentional.operationKey,first.operationKey);assert.equal(intentional.reprint,true);assert.equal(intentional.items[0].quantity,9);h.pending[2].resolve({id:2,status:'failed',message:'طابعة غير متاحة'});await a;assert.equal(s.busy,false);assert.equal(button.disabled,false);assert.equal(s.lastJob.id,1);assert.ok(h.toasts.some(function(message){return message.indexOf('طابعة غير متاحة')>=0;}));
}
async function inlinePreservesDraft(){var h=harness(),host=new Element('section');host.innerHTML='<input id="unsavedProduct" value="مسودة لم تحفظ">';var original=host.querySelector('#unsavedProduct');h.win.AlKhayalLabels.mountProductPrint(host,product(1));var details=host.querySelector('details');details.open=true;details.ontoggle();await settle();assert.strictEqual(host.querySelector('#unsavedProduct'),original);var button=host.querySelector('[data-print-single]'),task=button.onclick();h.pending[0].resolve({status:'submitted_unknown',message:'أرسلت'});await task;assert.strictEqual(host.querySelector('#unsavedProduct'),original);assert.equal(original.value,'مسودة لم تحفظ');var request=JSON.parse(h.calls.find(function(c){return c.config;}).config.body);assert.equal(request.items[0].barcodeId,301);assert.equal(request.items[0].code,undefined);assert.equal(request.exactQuantity,true);assert.equal(request.profileId,null);assert.equal(request.templateId,null);}
function openProductPrint(h){
 h.win.AlKhayalUI.displayError=function(message){return message;};
 var modal;
 h.win.AlKhayalUI.modal=function(options){modal=new Element('section');modal.innerHTML=options.body+options.actions;options.onOpen(modal);};
 h.win.AlKhayalLabels.productPrintDialog(1);
 return modal;
}
Element.prototype.decode=function(){return Promise.resolve();};
function preview(overrides){return Object.assign({profileId:'custom-profile',templateId:'custom-stock',pngBase64:'nativeFixture',printerName:'Labels',printerConfigured:true,pageWidthMm:43,pageHeightMm:27,price:'12.50 ج.م',unit:'قطعة'},overrides);}
async function directProductPrint(){
 var h=harness(),area=openProductPrint(h);await settle();
 var button=area.querySelector('[data-direct-print]');
 assert.equal(button.disabled,true);
 assert.equal(area.querySelector('[data-direct-barcode]'),null);
 assert.deepStrictEqual(JSON.parse(h.calls.find(function(c){return c.config;}).config.body),{barcodeId:301,format:'code128'});
 h.pending[0].resolve(preview());await settle();
 assert.equal(area.querySelector('[data-direct-image]').src,'data:image/png;base64,nativeFixture');
 assert.equal(area.querySelector('[data-direct-image]').hidden,false);
 assert.match(area.querySelector('[data-direct-paper]').textContent,/Labels.*43 × 27/);
 assert.match(area.querySelector('[data-direct-price]').textContent,/12.50/);
 area.querySelector('[data-direct-quantity]').value='3';
 var task=button.onclick();await button.onclick();
 assert.equal(h.pending.length,2,'double click cannot submit twice');
 var sent=JSON.parse(h.calls.find(function(c){return c.url==='/api/labels/jobs';}).config.body);
 assert.deepStrictEqual(sent.items,[{barcodeId:301,quantity:3,format:'code128'}]);
 assert.equal(sent.profileId,'custom-profile');
 assert.equal(sent.templateId,'custom-stock');
 assert.equal(sent.exactQuantity,true);
 h.pending[1].resolve({status:'submitted_unknown',message:'Windows accepted'});await task;
 assert.equal(button.disabled,true);
}
async function previewConfigurationFailures(){
 var h=harness(),area=openProductPrint(h);await settle();
 h.pending[0].resolve(preview({printerConfigured:false,printerMessage:'الطابعة غير متاحة'}));await settle();
 assert.equal(area.querySelector('[data-direct-image]').hidden,false);
 assert.match(area.querySelector('[data-direct-error]').textContent,/الطابعة غير متاحة/);
 await area.querySelector('[data-direct-print]').onclick();
 assert.equal(h.pending.length,1);
 h=harness();area=openProductPrint(h);await settle();
 h.pending[0].resolve(preview({pageWidthMm:null}));await settle();
 assert.equal(area.querySelector('[data-direct-image]').hidden,true);
 assert.match(area.querySelector('[data-direct-error]').textContent,/مقاس الورق/);
 await area.querySelector('[data-direct-print]').onclick();
 assert.equal(h.pending.length,1);
}
async function directAdditionalUnit(){
 var h=harness(),original=h.win.AlKhayalApi.request;
 h.win.AlKhayalApi.request=function(url,config){
   if(url==='/api/catalog/products/1'){
     var d=product(1);
     d.units.push({id:401,name:'علبة',isActive:true,isSaleUnit:true});
     d.variants[0].barcodes.push({id:999,code:'4006381333931',format:'ean13',productUnitId:401,isRetired:false});
     return Promise.resolve(d);
   }
   return original(url,config);
 };
 var area=openProductPrint(h);await settle();
 var choice=area.querySelector('[data-direct-barcode]');
 choice.value='999';var update=choice.onchange();
 h.pending[1].resolve(preview({unit:'علبة',pngBase64:'current'}));await update;
 h.pending[0].resolve(preview({pngBase64:'stale'}));await settle();
 assert.equal(area.querySelector('[data-direct-image]').src,'data:image/png;base64,current');
 assert.match(area.querySelector('[data-direct-price]').textContent,/علبة/);
 area.querySelector('[data-direct-quantity]').value='1';
 var task=area.querySelector('[data-direct-print]').onclick();
 var sent=JSON.parse(h.calls.find(function(c){return c.url==='/api/labels/jobs';}).config.body);
 assert.deepStrictEqual(sent.items,[{barcodeId:999,quantity:1,format:'ean13'}]);
 h.pending[2].resolve({status:'submitted_unknown'});await task;
}
async function barcodeDialog(){var h=harness(),modal,dialogOptions,copied,saved=0;
 h.win.navigator={clipboard:{writeText:function(code){copied=code;return Promise.resolve();}}};
 h.win.AlKhayalUI.modal=function(options){dialogOptions=options;modal=new Element('section');modal.innerHTML=options.body+options.actions;options.onOpen(modal);return modal;};
 h.win.AlKhayalLabels.barcodeManager(1,function(){saved++;});await settle();var host=modal.querySelector('[data-barcode-manager]');
 host.onclick({target:host.querySelector('[data-copy="301"]')});await settle();assert.equal(copied,'2912345678');
 host.onclick({target:host.querySelector('[data-barcode-edit="301"]')});host.querySelector('[data-code]').value='MANUAL-X';
 host.onclick({target:host.querySelector('[data-generate-code]')});assert.equal(h.pending.length,0);assert.equal(host.querySelector('[data-code]').value,'MANUAL-X');
 host.onclick({target:host.querySelector('[data-save-code]')});assert.equal(dialogOptions.beforeClose(),false);var sent=JSON.parse(h.calls.find(function(c){return c.config;}).config.body);assert.equal(sent.replaceBarcodeId,301);assert.equal(sent.variantId,201);assert.equal(sent.productUnitId,101);assert.equal(sent.code,'MANUAL-X');
 h.pending[0].reject({message:'lost response'});await settle();host.onclick({target:host.querySelector('[data-save-code]')});assert.deepStrictEqual(JSON.parse(h.calls.filter(function(c){return c.config;})[1].config.body),sent);h.pending[1].resolve({code:'MANUAL-X'});await settle();assert.equal(saved,1);
 host.onclick({target:host.querySelector('[data-barcode-print="301"]')});assert.equal(h.calls.filter(function(c){return c.config;}).length,3);assert.equal(JSON.parse(h.calls.filter(function(c){return c.config;})[2].config.body).barcodeId,301);h.pending[2].resolve(preview());await settle();host.onclick({target:host.querySelector('[data-preview-print]')});assert.equal(h.calls.filter(function(c){return c.config;}).length,4);assert.equal(JSON.parse(h.calls.filter(function(c){return c.config;})[3].config.body).items[0].barcodeId,301);h.pending[3].resolve({status:'submitted_unknown',message:'تم الإرسال'});await settle();
 host.onclick({target:host.querySelector('[data-barcode-delete="301"]')});assert.equal(host.querySelector('[data-barcode-delete-confirm]').hidden,false);host.onclick({target:host.querySelector('[data-delete-cancel]')});assert.equal(host.querySelector('[data-barcode-delete-confirm]').hidden,true);host.onclick({target:host.querySelector('[data-barcode-delete="301"]')});host.onclick({target:host.querySelector('[data-delete-confirm]')});assert.equal(h.calls.filter(function(c){return c.config;}).length,5);assert.equal(JSON.parse(h.calls.filter(function(c){return c.config;})[4].config.body).barcodeId,301);h.pending[4].resolve({});await settle();
 h.win.AlKhayalFeatures={isEnabled:function(){return false;}};var other=new Element('section');h.win.AlKhayalLabels.mountProductPrint(other,product(1));assert.equal(other.children.length,0,'disabled print module hides inline output without disabling ordinary barcode management');
}
async function batchPagination(){var h=harness(),node=new Element('section');h.win.AlKhayalRenderers['product-labels']({},node,h.context);await settle();assert.ok(h.calls.some(function(c){return c.url.includes('pageSize=20&page=1');}));var search=node.querySelector('[data-label-search]');search.value='الأحدث';search.oninput();h.runTimer();h.searches[1].resolve({items:[{id:2,name:'الأحدث'}],totalCount:15000});await settle();h.searches[0].resolve({items:[{id:1,name:'قديم'}],totalCount:15000});await settle();assert.strictEqual(node.querySelector('[data-label-search]'),search);assert.ok(node.querySelector('[data-label-products]').innerHTML.includes('الأحدث'));assert.ok(!node.querySelector('[data-label-products]').innerHTML.includes('قديم'));
 await node.onclick({target:node.querySelector('[data-choose-label-product="2"]')});await node.onclick({target:node.querySelector('[data-add-label="302"]')});assert.equal(h.context.state.labels.items.length,1);var quantity=node.querySelector('[data-item-quantity="302"]');quantity.value='4';node.oninput({target:quantity});var task=node.onclick({target:node.querySelector('[data-print-batch]')});h.pending[0].resolve({status:'submitted_unknown',message:'تم إرسال الدفعة'});await task;var batchRequest=JSON.parse(h.calls.find(function(c){return c.config;}).config.body);assert.equal(batchRequest.items[0].quantity,4);assert.equal(batchRequest.profileId,null);assert.equal(batchRequest.templateId,null);assert.equal(batchRequest.exactQuantity,true);await node.onclick({target:node.querySelector('[data-remove-label="302"]')});assert.equal(h.context.state.labels.items.length,0);
}
async function simpleAssignment(){var h=harness(),parent=new Element('section');h.win.AlKhayalLabels.mountSettings(parent,h.context,[{name:'Labels',status:'available'},{name:'Other',status:'available'}]);await settle();var box=parent.children[0];assert.ok(box.innerHTML.includes('تعيين طابعة ملصقات باركود'));assert.ok(box.innerHTML.includes('مقاس الاستيكر'));assert.ok(box.innerHTML.includes('35 × 25 mm'));assert.ok(!box.innerHTML.includes('DPI'));assert.ok(!box.innerHTML.includes('قالب الملصق'));var size=box.querySelector('[data-simple-size]');size.value='35x25';var task=box.querySelector('[data-simple-save]').onclick();var call=h.calls.find(function(c){return c.url==='/api/labels/printer-assignment';});assert.ok(call);var request=JSON.parse(call.config.body);assert.equal(request.printerName,'Labels');assert.equal(request.widthMm,35);assert.equal(request.heightMm,25);h.pending[0].resolve(settings());await task;size=box.querySelector('[data-simple-size]');size.value='custom';size.onchange();assert.equal(box.querySelector('[data-custom-size]').hidden,false);box.querySelector('[data-simple-width]').value='9';box.querySelector('[data-simple-height]').value='25';await box.querySelector('[data-simple-save]').onclick();assert.equal(h.pending.length,1,'invalid size must not reach API');}
async function designer(){
 var h=harness(),parent=new Element('section');
 h.win.AlKhayalLabels.mountSettings(parent,h.context,[{name:'Labels'}]);
 await settle();
 var box=parent.children[0],size=box.querySelector('[data-simple-size]'),image=box.querySelector('[data-design-img]');
 h.runTimer();
 assert.deepStrictEqual(JSON.parse(h.calls[1].config.body),{version:0,elements:null,stockOverride:null});
 size.value='35x25';size.onchange();h.runTimer();
 assert.deepStrictEqual(JSON.parse(h.calls[2].config.body).stockOverride,{widthMm:35,heightMm:25,printerName:'Labels'});
 h.pending[1].resolve({pngBase64:'current'});await settle();
 h.pending[0].resolve({pngBase64:'stale'});await settle();
 assert.equal(image.src,'data:image/png;base64,current');
 assert.equal(box.querySelector('[data-design-save]').disabled,true);
 size.value='50x30';size.onchange();h.runTimer();
 h.pending[2].resolve({pngBase64:'savedStock'});await settle();
 assert.equal(box.querySelector('[data-design-save]').disabled,false);
}
function noOverwriteGenerator(){var h=harness(),catalog=fs.readFileSync(path.join(__dirname,'../js/modules/catalog.js'),'utf8').replace('  function bindBarcodeGenerator(host)','  global.__bindGenerator=bindBarcodeGenerator;\n  function bindBarcodeGenerator(host)');vm.runInNewContext(catalog,{window:h.win,Uint32Array:Uint32Array});var host=new Element('section');host.innerHTML='<input id="catalogBarcode" value="MANUAL-123"><button data-generate-barcode></button>';h.win.__bindGenerator(host);host.querySelector('[data-generate-barcode]').onclick();assert.equal(host.querySelector('#catalogBarcode').value,'MANUAL-123');host.querySelector('#catalogBarcode').value='';host.querySelector('#catalogBarcode').select=function(){};host.querySelector('[data-generate-barcode]').onclick();assert.ok(/^29\d{8}$/.test(host.querySelector('#catalogBarcode').value));}
async function run(){await transport();await inlinePreservesDraft();await directProductPrint();await previewConfigurationFailures();await directAdditionalUnit();await barcodeDialog();await batchPagination();await simpleAssignment();await designer();noOverwriteGenerator();assert.ok(!source.includes('window.print')&&!source.includes('.pdf'));console.log('Label designer preview, label transport, saved paper dimensions, missing paper size, simple assignment, direct product/unit printing, replay/reprint, persisted-only draft-safe product actions, paged/stale search and bulk quantities passed.');}
if(require.main===module)run().catch(function(e){console.error(e);process.exitCode=1;});module.exports=run;
