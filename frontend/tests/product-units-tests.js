"use strict";
var assert=require("assert"),fs=require("fs"),path=require("path"),vm=require("vm");
var Element=require("./inventory-tests.js").Element;
var baseElementMatches=Element.prototype.matches;
Element.prototype.addEventListener=function(){};
Element.prototype.removeEventListener=function(){};
Element.prototype.hasAttribute=function(name){return Object.prototype.hasOwnProperty.call(this.attrs,name);};
Element.prototype.click=function(){if(this.onclick)this.onclick({target:this,preventDefault:function(){}});};
Object.defineProperty(Element.prototype,"options",{get:function(){return this.children.filter(function(c){return c.tag==="option";});}});
Object.defineProperty(Element.prototype,"selectedIndex",{
  get:function(){var idx=this.options.findIndex(function(o){return String(o.value)===String(this.value);},this);return idx<0?0:idx;},
  set:function(v){var opts=this.options;this.value=opts[v]?String(opts[v].value):"";}
});
Object.defineProperty(Element.prototype,"type",{get:function(){return this.attrs.type||"";}});
Object.defineProperty(Element.prototype,"checked",{get:function(){return this._checked===undefined?Object.prototype.hasOwnProperty.call(this.attrs,"checked"):this._checked;},set:function(v){this._checked=!!v;}});

function TestElement(tag,attributes){
  Element.call(this,tag,attributes);
  Object.defineProperty(this,"style",{value:{},writable:true,configurable:true});
  this._textContent=this.textContent||"";
}
TestElement.prototype=Object.create(Element.prototype);
TestElement.prototype.constructor=TestElement;
TestElement.prototype.contains=function(other){
  for(var node=other;node;node=node.parent){if(node===this)return true;}
  return false;
};
TestElement.prototype.addEventListener=function(){};
TestElement.prototype.removeEventListener=function(){};
TestElement.prototype.getBoundingClientRect=function(){return {left:0,top:0,width:120,height:80,right:120,bottom:80};};
Object.defineProperty(TestElement.prototype,"textContent",{get:function(){return this._textContent;},set:function(v){this._textContent=String(v);}});
TestElement.prototype.matches=function(selector){
  var tagged=selector.match(/^([a-z]+)\[([^=\]]+)=['\"]([^'\"]*)['\"]\]$/i);
  if(tagged)return this.tag===tagged[1]&&this.getAttribute(tagged[2])===tagged[3];
  var quoted=selector.match(/^\[([^=\]]+)=['\"]([^'\"]*)['\"]\]$/);
  if(quoted)return this.hasAttribute(quoted[1])&&this.getAttribute(quoted[1])===quoted[2];
  if(selector.indexOf(":not(")>=0){
    var m=selector.match(/^\[([^\]=]+)(?:=(['"])(.+?)\2)?\]:not\(\[([^\]=]+)(?:=(['"])(.+?)\5)?\]\)$/);
    if(m){
      var has="["+m[1]+(m[3]!==undefined?"="+m[2]+m[3]+m[2]:"")+"]";
      var not="["+m[4]+(m[6]!==undefined?"="+m[5]+m[6]+m[5]:"")+"]";
      return Element.prototype.matches.call(this,has)&&!Element.prototype.matches.call(this,not);
    }
  }
  return baseElementMatches.call(this,selector);
};
Element.prototype.matches=TestElement.prototype.matches;

function flush(){return new Promise(function(resolve){setImmediate(resolve);});}
async function settle(){for(var i=0;i<6;i++)await flush();}

function catalogContext(addHook){
  var catalogSource=fs.readFileSync(path.join(__dirname,"../js/modules/catalog.js"),"utf8");
  if(addHook){
    catalogSource=catalogSource.replace('  R["catalog-products"]=products;', '  global.__catalogTest={productForm:productForm,readProductFormV2:readProductFormV2,captureProductDraft:captureProductDraft,restoreProductDraft:restoreProductDraft,capabilityDetails:capabilityDetails};\n  R["catalog-products"]=products;');
  }
  var utils={
    escape:function(value){return String(value==null?"":value).replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/"/g,"&quot;");},
    number:function(value){return String(value);},
    money:function(value){return Number(value).toFixed(2);},
    normalize:function(value){return String(value||"").toLowerCase();},
    debounce:function(fn){return fn;},
    uid:function(prefix){return prefix+"-"+Date.now();}
  };
  var context={
    window:{
      AlKhayalUtils:utils,
      AlKhayalUI:{
        pageHeader:function(){return "";},
        badge:function(text){return "<span>"+text+"</span>";},
        toast:function(){},
        confirm:function(){return Promise.resolve(true);},
        closeModal:function(){},
        modal:function(o){var m=new TestElement("section");m.innerHTML=o.body+o.actions;if(o.onOpen)o.onOpen(m);return m;}
      },
      AlKhayalApi:{request:function(){return Promise.resolve([]);}},
      AlKhayalRenderers:{},
      AlKhayalFeatures:{isEnabled:function(){return true;},routeEnabled:function(){return true;},find:function(){return null;},load:function(){return Promise.resolve({});}},
      crypto:{getRandomValues:function(a){a.fill(1);}}
    },
    document:{createElement:function(tag){return new TestElement(tag);},body:{appendChild:function(){}}},
    BigInt:BigInt,Date:Date,JSON:JSON,setTimeout:setTimeout,clearTimeout:clearTimeout
  };
  context.window.window=context.window;
  vm.runInNewContext(catalogSource,context,{filename:"catalog-units.js"});
  return context;
}

async function verifyAddScreenAndCreationPayload(){
  var units=[{id:1,name:"قطعة",symbol:"قط",isActive:true,isSystem:true},{id:2,name:"كرتونة",symbol:"كر",isActive:true}];
  var categories=[{id:1,name:"عام",isActive:true,sortOrder:1}];
  var ctx=catalogContext(true);
  ctx.window.AlKhayalApi.request=function(url){
    if(url==="/api/catalog/categories?includeInactive=true")return Promise.resolve(categories);
    if(url==="/api/catalog/units?includeInactive=true")return Promise.resolve(units);
    if(url==="/api/catalog/attributes?includeInactive=false")return Promise.resolve([]);
    if(url==="/api/features/effective")return Promise.resolve({modules:[],capabilities:[]});
    if(url.indexOf("/api/catalog/products?page=1")===0)return Promise.resolve({items:[],page:1,pageSize:100,totalCount:0});
    return Promise.resolve([]);
  };
  var container=new TestElement("div");
  var screen={route:"product-add",renderer:"catalog-product-form"};
  var renderCtx={state:{},user:{role:"administrator"},isActive:function(){return true;},navigate:function(){}};
  ctx.window.AlKhayalRenderers["catalog-product-form"](screen,container,renderCtx);
  await settle();
  var html=container.innerHTML;
  assert.ok(html.indexOf("وحدات بيع إضافية")>=0,"product add must render the additional sale units section");
  assert.ok(html.indexOf('data-add-conversion')>=0,"product add must expose the add-unit control");
  assert.ok(html.indexOf('data-conversion-unit')>=0,"product add must expose the unit selector");

  var select=container.querySelector("[data-conversion-unit]");
  var option=select.options.find(function(o){return String(o.value)==="2";});
  assert.ok(option,"unit selector must contain the carton option");
  option.textContent="كرتونة";
  select.value="2";
  container.querySelector("[data-add-conversion]").onclick();

  var row=container.querySelector("[data-product-unit]");
  assert.ok(row,"clicking add must create a unit row");
  row.querySelector("[data-unit-num]").value="12";
  row.querySelector("[data-unit-price]").value="330.00";

  container.querySelector("#catalogName").value="زيت";
  container.querySelector("#catalogPrice").value="30.00";
  container.querySelector("#catalogUnit").value="1";
  container.querySelector("#catalogActive").value="true";
  container.querySelector("#catalogCategoryInput").value="1";

  var result=ctx.window.__catalogTest.readProductFormV2(container,null);
  assert.strictEqual(result.valid,true,"the creation form must be valid");
  var nonBase=result.data.configuration.units.filter(function(u){return !u.isBase;});
  assert.strictEqual(nonBase.length,1,"payload must contain the added unit");
  assert.strictEqual(nonBase[0].unitId,2,"payload must reference the selected unit");
  assert.strictEqual(nonBase[0].numerator,12,"payload must preserve the base-unit count");
  assert.strictEqual(nonBase[0].denominator,1,"the common UI must store an integer conversion");
  assert.strictEqual(nonBase[0].salePrice,"330.00","payload must include the independent selling price");
}

async function verifyEditHydration(){
  var units=[{id:1,name:"زجاجة",symbol:"ز",isActive:true,isSystem:true},{id:2,name:"كرتونة",symbol:"كر",isActive:true}];
  var detail={
    id:5,name:"عصير",salePrice:"10.00",baseUnitId:1,isActive:true,isArchived:false,version:1,description:"",categoryId:null,
    units:[{id:51,unitId:2,numerator:3,denominator:2,salePrice:"110.00",isBase:false,isSaleUnit:true,isPurchaseUnit:false,isActive:true,isLocked:false}],
    variants:[{id:9,name:"افتراضي",isActive:true,isDefault:true,isArchived:false,barcodes:[]}],
    capabilities:[]
  };
  var ctx=catalogContext(true);
  ctx.window.AlKhayalApi.request=function(url){
    if(url==="/api/catalog/categories?includeInactive=true")return Promise.resolve([]);
    if(url==="/api/catalog/units?includeInactive=true")return Promise.resolve(units);
    if(url==="/api/catalog/attributes?includeInactive=false")return Promise.resolve([]);
    if(url==="/api/features/effective")return Promise.resolve({modules:[],capabilities:[]});
    if(url.indexOf("/api/catalog/products?page=1")===0)return Promise.resolve({items:[detail],page:1,pageSize:100,totalCount:1});
    if(url==="/api/catalog/products/5")return Promise.resolve(detail);
    return Promise.resolve([]);
  };
  var container=new TestElement("div");
  var screen={route:"product-edit",mode:"edit",renderer:"catalog-product-form"};
  var renderCtx={state:{editProductId:5},user:{role:"administrator"},isActive:function(){return true;},navigate:function(){}};
  ctx.window.AlKhayalRenderers["catalog-product-form"](screen,container,renderCtx);
  await settle();
  var row=container.querySelector('[data-product-unit="51"]');
  assert.ok(row,"edit must render the saved unit row");
  assert.strictEqual(row.querySelector("[data-unit-num]").value,"3","row must preserve the exact saved conversion numerator");
  assert.strictEqual(row.querySelector("[data-unit-den]").value,"2","row must preserve the exact saved conversion denominator");
  assert.strictEqual(row.querySelector("[data-unit-price]").value,"110.00","row must show the saved unit price");
  assert.strictEqual(row.querySelector("[data-unit-sale]").checked,true,"row must preserve the sale flag");
  assert.strictEqual(row.querySelector("[data-unit-active]").checked,true,"row must preserve the active flag");
}

async function verifyDraftPriceRestore(){
  var units=[{id:1,name:"قطعة",symbol:"قط",isActive:true,isSystem:true},{id:2,name:"كرتونة",symbol:"كر",isActive:true}];
  var ctx=catalogContext(true);
  ctx.window.AlKhayalApi.request=function(url){
    if(url==="/api/catalog/categories?includeInactive=true")return Promise.resolve([]);
    if(url==="/api/catalog/units?includeInactive=true")return Promise.resolve(units);
    if(url==="/api/catalog/attributes?includeInactive=false")return Promise.resolve([]);
    if(url==="/api/features/effective")return Promise.resolve({modules:[],capabilities:[]});
    if(url.indexOf("/api/catalog/products?page=1")===0)return Promise.resolve({items:[],page:1,pageSize:100,totalCount:0});
    return Promise.resolve([]);
  };
  var container=new TestElement("div");
  var screen={route:"product-add",renderer:"catalog-product-form"};
  var renderCtx={state:{},user:{role:"administrator"},isActive:function(){return true;},navigate:function(){}};
  ctx.window.AlKhayalRenderers["catalog-product-form"](screen,container,renderCtx);
  await settle();

  var select=container.querySelector("[data-conversion-unit]");
  var option=select.options.find(function(o){return String(o.value)==="2";});
  option.textContent="كرتونة";
  select.value="2";
  container.querySelector("[data-add-conversion]").onclick();
  var row=container.querySelector("[data-product-unit]");
  row.querySelector("[data-unit-num]").value="12";
  row.querySelector("[data-unit-price]").value="330.00";

  var draft=ctx.window.__catalogTest.captureProductDraft(container);
  assert.strictEqual(draft.conversions[0].price,"330.00","draft capture must include unit price");

  row.querySelector("[data-unit-price]").value="";
  ctx.window.__catalogTest.restoreProductDraft(container,draft);
  assert.strictEqual(row.querySelector("[data-unit-price]").value,"330.00","draft restore must bring back the unit price");
}

async function verifyPosContextMenuAndBaseClick(){
  var salesSource=fs.readFileSync(path.join(__dirname,"../js/modules/sales.js"),"utf8")
    .replace("    init();", "    global.__salesTest={state:state,productContextMenu:productContextMenu,click:click,appendLine:appendLine};\n    init();");
  var product={
    id:5,name:"زيت",salePrice:"30.00",attributes:[],capabilities:[],
    variants:[{id:9,name:"أساسي",isActive:true,isDefault:true,isArchived:false,effectiveSalePrice:"30.00",effectiveSalePriceExact:"30.0000"}],
    units:[
      {id:1,name:"زجاجة",isBase:true,isSaleUnit:true,isActive:true,numerator:1,denominator:1},
      {id:2,name:"كرتونة",isBase:false,isSaleUnit:true,isActive:true,numerator:12,denominator:1,salePrice:"330.00"}
    ]
  };
  var ctx={
    window:{
      AlKhayalUtils:{escape:function(x){return String(x==null?"":x);},money:function(x){return Number(x).toFixed(2);},number:String,normalize:String,debounce:function(fn){return fn;}},
      AlKhayalUI:{pageHeader:function(){return "";},badge:function(t){return t;},toast:function(){},confirm:function(){return Promise.resolve(true);},closeModal:function(){},modal:function(o){var m=new TestElement("section");m.innerHTML=o.body+o.actions;if(o.onOpen)o.onOpen(m);return m;}},
      AlKhayalApi:{request:function(url){
        if(url==="/api/inventory/warehouses")return Promise.resolve([{id:1,name:"مخزن",isActive:true,isDefault:true}]);
        if(url==="/api/settings/company")return Promise.resolve({});
        if(url==="/api/inventory/categories")return Promise.resolve([]);
        if(url==="/api/payment-methods/active")return Promise.resolve([]);
        if(url==="/api/treasury/accounts")return Promise.resolve([]);
        if(url==="/api/inventory/products/5")return Promise.resolve(product);
        if(url.indexOf("/api/inventory/products")===0)return Promise.resolve({items:[],page:1,pageSize:40,totalCount:0});
        return Promise.resolve([]);
      }},
      AlKhayalSettings:{current:function(){return {};},load:function(){return Promise.resolve({});}},
      AlKhayalFeatures:{isEnabled:function(){return false;},routeEnabled:function(){return true;},find:function(){return null;},load:function(){return Promise.resolve({});}},
      AlKhayalRenderers:{}
    },
    document:{createElement:function(tag){return new TestElement(tag);},body:{appendChild:function(){}},addEventListener:function(){},removeEventListener:function(){}},
    BigInt:BigInt,Date:Date,JSON:JSON,setTimeout:setTimeout,clearTimeout:clearTimeout,URL:URL,Math:Math
  };
  ctx.window.window=ctx.window;
  vm.runInNewContext(salesSource,ctx,{filename:"sales-units.js"});
  var container=new TestElement("div");
  ctx.window.AlKhayalRenderers.pos({route:"pos"},container,{state:{},user:{role:"administrator"},isActive:function(){return true;},navigate:function(){},canNavigate:function(){return true;},registry:{}});
  await settle();
  var t=ctx.window.__salesTest;
  t.state.warehouseId=1;

  var tile=new TestElement("button");
  tile.setAttribute("data-product","5");
  tile.setAttribute("data-sale-unit-count","2");
  var prevented=false;
  var menuEvent={target:tile,clientX:10,clientY:10,preventDefault:function(){prevented=true;}};
  await t.productContextMenu(menuEvent);
  assert.strictEqual(prevented,true,"context menu must call preventDefault for multi-unit cards");
  var menu=container.querySelector("[data-unit-menu]");
  assert.ok(menu,"context menu must be appended to the container");
  var menuHtml=menu.innerHTML;
  assert.ok(menuHtml.indexOf("كرتونة")>=0&&menuHtml.indexOf("330.00")>=0&&menuHtml.indexOf("12 زجاجة")>=0,"menu must show unit name, price and base count");

  var cartonButton=container.querySelector('[data-context-unit="2"]');
  assert.ok(cartonButton,"carton menu item must exist");
  container.onclick({target:cartonButton,preventDefault:function(){}});
  await settle();
  assert.strictEqual(t.state.lines.length,1,"selecting carton must add one line");
  assert.strictEqual(t.state.lines[0].productUnitId,2,"carton line must use the carton unit");
  assert.strictEqual(t.state.lines[0].unitName,"كرتونة","carton line must carry the unit name");
  assert.strictEqual(t.state.lines[0].unitPrice,"330.00","carton line must use the carton price");

  container.onclick({target:tile,preventDefault:function(){}});
  await settle();
  assert.strictEqual(t.state.lines.length,2,"ordinary click must add a base-unit line");
  assert.strictEqual(t.state.lines[1].productUnitId,1,"ordinary click must use the base unit");
  assert.strictEqual(t.state.lines[1].unitName,"زجاجة","base line must carry the base unit name");
}

module.exports=async function(){
  await verifyAddScreenAndCreationPayload();
  await verifyEditHydration();
  await verifyDraftPriceRestore();
  await verifyPosContextMenuAndBaseClick();
  console.log("Product unit UI: add/edit visibility, creation payload, draft price, and POS context menu tests passed.");
};
if(require.main===module)module.exports().catch(function(e){console.error(e);process.exitCode=1;});
