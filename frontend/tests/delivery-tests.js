"use strict";
var assert=require("assert"),fs=require("fs"),path=require("path"),vm=require("vm"),Element=require("./inventory-tests.js").Element;
var salesSource=fs.readFileSync(path.join(__dirname,"../js/modules/sales.js"),"utf8");
var ordersSource=fs.readFileSync(path.join(__dirname,"../js/modules/delivery.js"),"utf8");
Element.prototype.hasAttribute=function(name){return Object.prototype.hasOwnProperty.call(this.attrs,name);};
Element.prototype.removeAttribute=function(name){delete this.attrs[name];};
Element.prototype.validity={customError:false};
async function settle(){await new Promise(function(r){setImmediate(r);});await new Promise(function(r){setImmediate(r);});}
function event(node){return {target:node,stopImmediatePropagation:function(){},preventDefault:function(){}};}
async function testModal(){
 var modal,messages=[],printed=[],customerSaved,host=new Element("section"),listeners=[];host.addEventListener=function(type,fn){if(type==="click")listeners.push(fn);};host.deliveryClick=function(e){listeners.forEach(function(fn){fn(e);});};
 var win={AlKhayalRenderers:{openCustomerModal:function(done){customerSaved=done;}},AlKhayalOpenCustomerModal:function(done){customerSaved=done;},AlKhayalSettings:{current:function(){return{};}},AlKhayalUtils:{escape:String,money:String,number:String,normalize:String,debounce:function(fn){return fn;}},AlKhayalUI:{pageHeader:function(){return"";},toast:function(x){messages.push(x);},modal:function(o){modal=new Element("section");modal.innerHTML=o.body+o.actions;o.onOpen(modal);},closeModal:function(){}},AlKhayalPrinting:{submit:function(type,id){printed.push({type:type,id:id});}},AlKhayalApi:{request:function(url){return Promise.resolve(url.indexOf("/api/delivery/drivers")===0?[{id:7,name:"طيار ألف",phone:"011"}]:url.indexOf("/api/customers/search")===0?[{id:2,name:"عميل موجود",code:"C-2",phone:"0102"}]:[]);}}};
 var source=salesSource.replace("    init();","    global.__deliveryTest={state:state,draw:draw,payload:payload};");assert.notEqual(source,salesSource);
 vm.runInNewContext(source,{window:win,BigInt:BigInt,Date:Date,setTimeout:setTimeout,clearTimeout:clearTimeout,sessionStorage:{getItem:function(){return null;},setItem:function(){},removeItem:function(){}}});
 win.AlKhayalRenderers.pos({route:"pos"},host,{state:{},isActive:function(){return true;}});var test=win.__deliveryTest,state=test.state;
 state.warehouses=[{id:1,name:"الرئيسي"}];state.warehouseId=1;state.lines=[{variantId:1,productUnitId:1,quantity:"1",unitPrice:"10.00",discount:"0.00",productName:"صنف"}];test.draw();
 assert.equal(test.payload().delivery,null,"ordinary sales require no delivery fields");
 assert.ok(!host.innerHTML.includes("touch-customer-add")&&host.innerHTML.includes("عميل: بدون عميل"),"standalone action is removed");
 var customerInput=host.querySelector("[data-customer-search]"),dropdown=host.querySelector("[data-customer-dropdown]");customerInput.onfocus();await settle();
 assert.equal(dropdown.hidden,false);assert.ok(host.innerHTML.indexOf("إضافة عميل جديد")<host.innerHTML.indexOf("data-customer-results"));var customerResults=host.querySelector("[data-customer-results]");assert.ok(customerResults.innerHTML.indexOf("بدون عميل")<customerResults.innerHTML.indexOf("عميل موجود"));
 assert.ok(customerResults.innerHTML.includes('data-customer-option=""'),"no-customer choice remains available");
 var focused=false;Element.prototype.focus=function(){focused=true;};customerInput.onkeydown({key:"ArrowDown",preventDefault:function(){}});assert.ok(focused,"keyboard can enter the dropdown");customerInput.onkeydown({key:"Escape",preventDefault:function(){}});assert.equal(dropdown.hidden,true,"Escape closes the dropdown");customerInput.onfocus();await settle();
 state.paymentMode="credit";state.immediateMethod="credit";state.discountInput="1.00";state.delivery={driverId:null,primaryPhone:"010",address:"عنوان",fee:"0.00",status:"pending"};
 await host.onclick(event(host.querySelector("[data-add-customer]")));assert.equal(typeof customerSaved,"function");assert.equal(dropdown.hidden,true,"opening the modal closes the dropdown");
 assert.equal(state.customerId,"");assert.equal(state.lines.length,1);assert.equal(state.discountInput,"1.00");assert.equal(state.immediateMethod,"credit","cancel leaves sale state alone");assert.equal(state.delivery.address,"عنوان");
 await customerSaved({id:15,name:"عميل جديد",phone:"01015"});assert.equal(state.customerId,"15");assert.equal(state.selectedCustomer.name,"عميل جديد");assert.equal(state.lines.length,1);assert.equal(state.discountInput,"1.00");assert.equal(state.immediateMethod,"credit");assert.equal(state.delivery.address,"عنوان");
 state.paymentMode="cash";state.immediateMethod="cash";state.discountInput="0.00";state.customerId="";state.selectedCustomer=null;state.customerName="";state.delivery=null;test.draw();
 assert.ok(host.querySelector("[data-delivery]"),"delivery control is rendered");assert.equal(typeof host.deliveryClick,"function");assert.ok(host.querySelector("[data-delivery]").closest("[data-delivery]"));host.deliveryClick(event(host.querySelector("[data-delivery]")));await settle();assert.ok(messages.some(function(x){return x.includes("اختر العميل");}),JSON.stringify(messages));
 state.customerId="1";state.customers=[{id:1,name:"الأول",phone:"010111",secondaryPhone:"010222",address:"العنوان الأول"},{id:2,name:"الثاني",phone:"010333",secondaryPhone:"010444",address:"العنوان الثاني"},{id:3,name:"الثالث",phone:"0010555",secondaryPhone:null,address:null}];test.draw();
 host.deliveryClick(event(host.querySelector("[data-delivery]")));await settle();assert.equal(modal.querySelector("[data-delivery-phone]").value,"010111");assert.equal(modal.querySelector("[data-delivery-secondary]").value,"010222");assert.equal(modal.querySelector("[data-delivery-address]").value,"العنوان الأول");assert.ok(modal.innerHTML.includes("طيار ألف")&&!modal.innerHTML.includes("السائق"));
 modal.querySelector("[data-delivery-driver]").value="7";modal.querySelector("[data-delivery-phone]").value="010555";modal.querySelector("[data-delivery-secondary]").value="0020555";modal.querySelector("[data-delivery-address]").value="عنوان الطلب";modal.querySelector("[data-delivery-notes]").value="اتصل قبل الوصول";modal.querySelector("[data-delivery-fee]").value="2.50";modal.querySelector("[data-delivery-save]").onclick();
 assert.equal(state.delivery.driverId,7);assert.equal(test.payload().delivery.fee,"2.50");assert.equal(test.payload().delivery.notes,"اتصل قبل الوصول");assert.equal(test.payload().delivery.secondaryPhone,"0020555");assert.ok(host.innerHTML.includes("12.5"),"cart total includes fee once");
 host.deliveryClick(event(host.querySelector("[data-delivery]")));await settle();assert.ok(modal.innerHTML.includes("الطيار (اختياري)")&&modal.innerHTML.includes("تعيين لاحقًا"));modal.querySelector("[data-delivery-driver]").value="";modal.querySelector("[data-delivery-save]").onclick();assert.equal(state.delivery.driverId,null);assert.equal(test.payload().delivery.driverId,null,"no default driver enters the payload");
 host.deliveryClick(event(host.querySelector("[data-delivery]")));await settle();modal.querySelector("[data-delivery-driver]").value="7";modal.querySelector("[data-delivery-save]").onclick();assert.equal(state.delivery.driverId,7,"assigned rider still works");
 state.customers[0].phone="010999";state.customers[0].secondaryPhone="0020999";state.customers[0].address="عنوان ملف العميل الجديد";
 host.deliveryClick(event(host.querySelector("[data-delivery]")));await settle();assert.equal(modal.querySelector("[data-delivery-phone]").value,"010555");assert.equal(modal.querySelector("[data-delivery-secondary]").value,"0020555");assert.equal(modal.querySelector("[data-delivery-address]").value,"عنوان الطلب");assert.equal(test.payload().delivery.primaryPhone,"010555","saved delivery remains an invoice snapshot");
 var customer=new Element("button",{"data-customer-option":"2","data-customer-name":"الثاني"});host.deliveryClick(event(customer));await host.onclick(event(customer));assert.equal(state.delivery,null,"switching customer clears prior delivery");
 host.deliveryClick(event(host.querySelector("[data-delivery]")));await settle();assert.equal(modal.querySelector("[data-delivery-phone]").value,"010333");assert.equal(modal.querySelector("[data-delivery-secondary]").value,"010444");assert.equal(modal.querySelector("[data-delivery-address]").value,"العنوان الثاني");
 var third=new Element("button",{"data-customer-option":"3","data-customer-name":"الثالث"});host.deliveryClick(event(third));await host.onclick(event(third));assert.equal(state.delivery,null);
 state.customers=[];host.deliveryClick(event(host.querySelector("[data-delivery]")));await settle();assert.equal(modal.querySelector("[data-delivery-phone]").value,"0010555","selected contact survives replacement of search results");assert.equal(modal.querySelector("[data-delivery-secondary]").value,"");assert.equal(modal.querySelector("[data-delivery-address]").value,"");
 modal.querySelector("[data-delivery-address]").value="عنوان يدوي";
 modal.querySelector("[data-delivery-driver]").value="7";modal.querySelector("[data-delivery-fee]").value="-1";modal.querySelector("[data-delivery-save]").onclick();assert.equal(state.delivery,null);
 modal.querySelector("[data-delivery-fee]").value="1.00";modal.querySelector("[data-delivery-save]").onclick();state.invoice={id:99,status:"posted",delivery:state.delivery};test.draw();assert.ok(host.innerHTML.indexOf('data-delivery ')<host.innerHTML.indexOf('data-delivery-print '),"ticket button follows delivery setup");host.deliveryClick(event(host.querySelector("[data-delivery-print]")));assert.equal(printed.length,1);assert.equal(printed[0].type,"delivery");assert.equal(printed[0].id,99);
 state.invoice=null;state.lines=[];state.customerId="";state.delivery=null;state.lastPostedDelivery={id:99,invoiceNumber:"SAL-99",status:"posted",delivery:{}};test.draw();assert.ok(host.innerHTML.includes("SAL-99"));host.deliveryClick(event(host.querySelector("[data-delivery-print]")));assert.equal(printed[1].id,99,"cleared cart retains latest posted delivery");state.lines=[{}];test.draw();assert.ok(host.querySelector("[data-delivery-print]").hasAttribute("disabled"),"unrelated active cart cannot print older delivery");
}
async function testOrders(){
 var calls=[],pending=[],prints=[],navigation=[],host=new Element("section"),win={AlKhayalRenderers:{},AlKhayalPrinting:{submit:function(type,id){prints.push({type:type,id:id});}},AlKhayalUtils:{escape:String,money:String,debounce:function(fn){return fn;}},AlKhayalUI:{pageHeader:function(){return"";},toast:function(){}},AlKhayalApi:{request:function(url,options){calls.push({url:url,options:options});return new Promise(function(resolve,reject){pending.push({resolve:resolve,reject:reject});});}}};
 vm.runInNewContext(ordersSource,{window:win});win.AlKhayalRenderers["delivery-orders"]({},host,{navigate:function(route,state){navigation.push({route:route,state:state});}});assert.equal(calls[0].url,"/api/delivery/drivers","inactive historical drivers remain filterable");pending[0].resolve([{id:7,name:"طيار ألف",isActive:false}]);await settle();
 assert.ok(calls[1].url.includes("page=1&pageSize=25"));pending[1].resolve({items:["pending","out_for_delivery","delivered","failed","unknown"].map(function(status,index){return {salesInvoiceId:index+1,invoiceNumber:"SAL-"+(index+1),customerName:"عميل",primaryPhone:"010",address:"عنوان",driverName:index===0?"":"طيار ألف",status:status,totalScaled:125000};}),total:26,page:1,pageSize:25});await settle();assert.ok(!host.querySelector("[data-q]").hasAttribute("disabled"),"controls reenable after loading");assert.ok(host.innerHTML.includes("تعيين لاحقًا"),"unassigned orders show a clear rider state");
 [["warning","بانتظار التوصيل"],["info","خرج للتوصيل"],["success","تم التسليم"],["danger","فشل التوصيل"],["neutral","unknown"]].forEach(function(pair){assert.ok(host.innerHTML.includes("delivery-order--"+pair[0]),"row tone for "+pair[1]);assert.ok(host.innerHTML.includes('aria-label="حالة التوصيل: '+pair[1]+'"'),"readable status badge for "+pair[1]);});
 assert.ok(host.innerHTML.includes('title="عنوان"')&&host.innerHTML.includes('delivery-row-actions'));
 host.onclick(event(host.querySelector('[data-print-invoice]')));host.onclick(event(host.querySelector('[data-print-delivery]')));host.onclick(event(host.querySelector('[data-view-invoice]')));
 assert.deepEqual(prints,[{type:'sale',id:1},{type:'delivery',id:1}]);assert.equal(navigation[0].route,'sales-management');assert.equal(navigation[0].state.saleInvoiceId,1);
 var styles=fs.readFileSync(path.join(__dirname,"../css/modules.css"),"utf8"),light=fs.readFileSync(path.join(__dirname,"../css/tokens.css"),"utf8"),dark=fs.readFileSync(path.join(__dirname,"../css/themes.css"),"utf8");
 ["warning","info","success","danger"].forEach(function(tone){assert.ok(styles.includes(".delivery-order--"+tone),"status row CSS: "+tone);});
 assert.ok(styles.includes(".delivery-order:hover td")&&styles.includes(".delivery-order:focus-within td")&&styles.includes("border-inline-start"),"row tint must retain hover, keyboard focus and RTL status border");
 var palettes={pending:"warning",out:"info",delivered:"success",failed:"error"};
 Object.keys(palettes).forEach(function(status){
   ["bg","text","border"].forEach(function(part){
     var token="--delivery-"+status+"-"+part, semantic="--tc-"+palettes[status]+(part==="bg"?"-bg":"");
     assert.ok(light.includes(token+": var("+semantic+")"),"delivery aliases the shared theme token: "+token);
     assert.ok(styles.includes("var("+token+")"),"status CSS consumes "+token);
   });
 });
 assert.ok(dark.includes(':root[data-theme="dark"]')&&fs.readFileSync(path.join(__dirname,"../js/core/theme.js"),"utf8").includes('data-theme", resolved(selected)'),"system appearance must resolve through the existing theme selector");
 assert.ok(styles.includes('delivery-order[aria-selected="true"]')&&styles.includes(".delivery-status-badge"),"selected rows and badges remain readable");
 host.onclick(event(host.querySelector("[data-next]")));assert.ok(calls[2].url.includes("page=2"));pending[2].resolve({items:[],total:26,page:2,pageSize:25});await settle();
 var status=host.querySelector("[data-f]");status.value="pending";host.onchange(event(status));assert.ok(calls[3].url.includes("status=pending")&&calls[3].url.includes("page=1"));pending[3].resolve({items:[],total:0,page:1,pageSize:25});await settle();assert.ok(host.innerHTML.includes("لا توجد طلبات"));
 var driver=host.querySelector("[data-driver]");driver.value="7";host.onchange(event(driver));assert.ok(calls[4].url.includes("driverId=7")&&calls[4].url.includes("status=pending"));pending[4].resolve({items:[],total:0,page:1,pageSize:25});await settle();
 var size=host.querySelector("[data-size]");size.value="50";host.onchange(event(size));assert.ok(calls[5].url.includes("page=1&pageSize=50"));pending[5].reject(new Error("تعذر الاتصال"));await settle();assert.ok(host.innerHTML.includes("تعذر الاتصال"));host.onclick(event(host.querySelector("[data-retry]")));assert.equal(calls.length,7);pending[6].resolve({items:[],total:0,page:1,pageSize:50});await settle();
 var activeDriver=host.querySelector("[data-driver]");assert.ok(host.innerHTML.includes("طيار ألف"),"historical inactive driver remains filterable");
}
async function testDriverNavigationFilter(){
 var calls=[],pending=[],host=new Element("section"),navigated=[];
 var win={AlKhayalRenderers:{},AlKhayalUtils:{escape:String,money:String,debounce:function(fn){return fn;}},AlKhayalUI:{pageHeader:function(){return"";},toast:function(){}},AlKhayalApi:{request:function(url){calls.push(url);return new Promise(function(resolve){pending.push(resolve);});}}};
 vm.runInNewContext(ordersSource,{window:win});
 win.AlKhayalRenderers["delivery-orders"]({},host,{state:{driverId:42},navigate:function(route,state){navigated.push({route:route,state:state});}});
 assert.equal(calls[0],"/api/delivery/drivers");pending[0]([{id:42,name:"طيار ثابت"},{id:73,name:"طيار آخر"}]);await settle();
 assert.ok(calls[1].includes("driverId=42")&&calls[1].includes("page=1"),"initial driver ID applies before the first orders fetch");
 assert.equal(host.querySelector("[data-driver]").value,"42");
 pending[1]({items:[{salesInvoiceId:1,invoiceNumber:"SAL-1",customerName:"أ",status:"pending",driverName:"طيار ثابت",totalScaled:10000}],total:1,page:1,pageSize:25});await settle();
 var other=host.querySelector("[data-driver]");other.value="73";host.onchange(event(other));
 assert.ok(calls[2].includes("driverId=73")&&calls[2].includes("page=1"));assert.ok(!host.innerHTML.includes("SAL-1"),"previous driver's rows are cleared during the new filtered request");
 pending[2]({items:[],total:0,page:1,pageSize:25});await settle();
 var driversHost=new Element("section"),driverCalls=[],driverPending=[];
 win.AlKhayalApi.request=function(url){driverCalls.push(url);return new Promise(function(resolve){driverPending.push(resolve);});};
 win.AlKhayalRenderers["drivers-management"]({title:"إدارة الطيارين"},driversHost,{navigate:function(route,state){navigated.push({route:route,state:state});}});
 driverPending[0]([{id:987,name:"طيار الاسم" ,phone:"012345678",isActive:true}]);await settle();
 assert.ok(driversHost.innerHTML.includes('data-orders="987"')&&driversHost.innerHTML.includes("طلبات التوصيل"));
 driversHost.onclick(event(driversHost.querySelector("[data-orders]")));
 assert.deepEqual(navigated[0],{route:"delivery-orders",state:{driverId:987}},"row action navigates with stable numeric ID");
}
async function testOrderStatusChange(){
 var calls=[],pending=[],host=new Element("section"),win={AlKhayalRenderers:{},AlKhayalUtils:{escape:String,money:String,debounce:function(fn){return fn;}},AlKhayalUI:{pageHeader:function(){return"";},toast:function(){}},AlKhayalApi:{request:function(url,options){calls.push({url:url,options:options});return new Promise(function(resolve,reject){pending.push({resolve:resolve,reject:reject});});}}};
 vm.runInNewContext(ordersSource,{window:win});win.AlKhayalRenderers["delivery-orders"]({},host);
 pending[0].resolve([]);await settle();
 pending[1].resolve({items:[{salesInvoiceId:8,invoiceNumber:"SAL-8",customerName:"عميل",status:"pending",totalScaled:10000}],total:1,page:1,pageSize:25});await settle();
 var control=host.querySelector("[data-s]");control.value="out_for_delivery";host.onchange(event(control));
 assert.equal(calls[2].url,"/api/delivery/orders/8/status");assert.equal(JSON.parse(calls[2].options.body).status,"out_for_delivery");
 pending[2].resolve({updated:true});await settle();assert.ok(calls[3].url.includes("/api/delivery/orders?"),"status update refreshes the list");
 pending[3].resolve({items:[{salesInvoiceId:8,invoiceNumber:"SAL-8",customerName:"عميل",status:"out_for_delivery",totalScaled:10000}],total:1,page:1,pageSize:25});await settle();
 assert.ok(host.innerHTML.includes("delivery-order--info")&&host.innerHTML.includes("خرج للتوصيل"));
}
async function testDrivers(){
 var calls=[],pending=[],modal,closed=0,host=new Element("section");
 var win={AlKhayalRenderers:{},AlKhayalUtils:{escape:String,money:String,debounce:function(fn){return fn;}},AlKhayalUI:{pageHeader:function(screen,actions){return "<h1>"+screen.title+"</h1>"+(actions||"");},modal:function(options){modal=new Element("section");modal.innerHTML=options.body+options.actions;options.onOpen(modal);},closeModal:function(){closed++;},toast:function(){}},AlKhayalApi:{request:function(url,options){calls.push({url:url,options:options});return new Promise(function(resolve,reject){pending.push({resolve:resolve,reject:reject});});}}};
 vm.runInNewContext(ordersSource,{window:win});
 win.AlKhayalRenderers["drivers-management"]({title:"إدارة الطيارين"},host);
 assert.ok(host.innerHTML.includes("جارٍ تحميل الطيارين"));
 pending[0].resolve([]);await settle();
 assert.ok(host.innerHTML.includes("لا يوجد طيارون")&&host.querySelector("[data-add]"));
 host.onclick(event(host.querySelector("[data-add]")));
 assert.ok(modal.innerHTML.includes("اسم الطيار")&&modal.innerHTML.includes("رقم الهاتف"));
 modal.querySelector("[data-save]").onclick();assert.equal(calls.length,1,"invalid form does not reach API");
 modal.querySelector("[data-name]").value="طيار ألف";modal.querySelector("[data-phone]").value="010123";modal.querySelector("[data-active]").checked=true;
 modal.querySelector("[data-save]").onclick();assert.equal(calls[1].url,"/api/delivery/drivers");
 assert.equal(JSON.parse(calls[1].options.body).isActive,true);
 pending[1].resolve({id:7});await settle();assert.equal(closed,1);assert.equal(calls.length,3);
 pending[2].resolve([{id:7,name:"طيار ألف",phone:"010123",isActive:true}]);await settle();
 assert.ok(host.innerHTML.includes("طيار ألف")&&host.querySelector("[data-edit]"));
 host.onclick(event(host.querySelector("[data-edit]")));
 modal.querySelector("[data-active]").checked=false;
 modal.querySelector("[data-save]").onclick();
 assert.equal(JSON.parse(calls[3].options.body).isActive,false);
 pending[3].resolve({id:7});await settle();
 pending[4].resolve([{id:7,name:"طيار ألف",phone:"010123",isActive:false}]);await settle();
 assert.ok(host.innerHTML.includes("موقوف"),"inactive drivers stay in management");
 host.querySelector("[data-search]").value="missing";host.querySelector("[data-search]").oninput();
 pending[5].reject(new Error("تعذر الاتصال"));await settle();
 assert.ok(host.innerHTML.includes("تعذر الاتصال")&&host.querySelector("[data-retry]"));
 host.onclick(event(host.querySelector("[data-retry]")));pending[6].resolve([]);await settle();
 assert.ok(host.innerHTML.includes("لا يوجد طيارون"));
 var registry=JSON.parse(fs.readFileSync(path.join(__dirname,"../data/screens.json"),"utf8"));
 assert.ok(["delivery-orders","drivers-management"].every(function(route){return registry.screens.find(function(screen){return screen.route===route;}).implemented===true;}),"canonical registry must mark delivery routes as implemented");
 var html=fs.readFileSync(path.join(__dirname,"../index.html"),"utf8");
 assert.ok(html.indexOf('js/modules/renderers.js')<html.indexOf('js/modules/delivery.js')&&html.indexOf('js/modules/delivery.js')<html.indexOf('js/app.js'),"delivery must register after base renderers and before app startup");
}
module.exports=async function(){await testModal();await testOrders();await testDriverNavigationFilter();await testOrderStatusChange();await testDrivers();console.log("Delivery UI: driver CRUD, filtered navigation, empty/error states, POS modal, order status colors and interactions passed.");};
if(require.main===module)module.exports().catch(function(e){console.error(e);process.exitCode=1;});
