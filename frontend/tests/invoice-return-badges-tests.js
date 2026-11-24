"use strict";
var assert=require("assert"),fs=require("fs"),path=require("path"),vm=require("vm"),Element=require("./inventory-tests.js").Element;
function settle(){return new Promise(function(resolve){setImmediate(resolve);});}
async function verify(module,renderer,route,host){
  var rows=[{id:1,invoiceNumber:"INV-NONE",invoiceDate:"2026-10-29",status:"posted",returnStatus:"none",totalScaled:100000,paidScaled:100000},{id:2,invoiceNumber:"INV-PART",invoiceDate:"2026-10-29",status:"posted",returnStatus:"partial",totalScaled:100000,paidScaled:100000},{id:3,invoiceNumber:"INV-FULL",invoiceDate:"2026-10-29",status:"posted",returnStatus:"full",totalScaled:100000,paidScaled:0},{id:4,invoiceNumber:"INV-DRAFT",invoiceDate:"2026-10-29",status:"draft",returnStatus:"none",totalScaled:100000,paidScaled:0}];
  var api={request:function(url){if(url==="/api/payment-methods/active")return Promise.resolve([]);if(url.indexOf("/api/sales/page?")===0||url.indexOf("/api/purchases/page?")===0)return Promise.resolve({items:rows,page:1,totalCount:rows.length});if(url==="/api/purchases"||url.indexOf("/api/sales?")===0)return Promise.resolve(rows);if(url==="/api/inventory/warehouses")return Promise.resolve([{id:1,name:"مخزن",isActive:true,isDefault:true}]);if(url==="/api/settings/company")return Promise.resolve({});if(url==="/api/customers")return Promise.resolve([]);throw new Error("Unexpected invoice-list request: "+url);}};
  var opened=[],handed=[];
  var win={AlKhayalRenderers:{},AlKhayalUtils:{escape:function(v){return String(v==null?"":v);},money:String,debounce:function(fn){return fn;}},AlKhayalUI:{pageHeader:function(){return "";},toast:function(){}},AlKhayalApi:api,AlKhayalSettings:{current:function(){return {};}},open:function(url,target){opened.push([url,target]);}};
  vm.runInNewContext(fs.readFileSync(path.join(__dirname,"../js/modules/"+module+".js"),"utf8"),{window:win,AlKhayalApi:api,URLSearchParams:URLSearchParams,BigInt:BigInt,Date:Date,setTimeout:setTimeout,clearTimeout:clearTimeout,btoa:function(value){return Buffer.from(value,"binary").toString("base64");},fetch:function(){return Promise.resolve({ok:true,arrayBuffer:function(){return Promise.resolve(Uint8Array.from(Buffer.from("%PDF-1.4 test")).buffer);}});}});
  var node=new Element("section");win.AlKhayalRenderers[renderer]({route:route},node,{state:{},isActive:function(){return true;},navigate:function(){}});await settle();await settle();
  var html=node.querySelector(host).innerHTML||node.innerHTML;
  function invoiceRow(number){var index=html.indexOf(number);return html.slice(html.lastIndexOf("<tr",index)).split("</tr>")[0];}
  assert.ok(invoiceRow("INV-PART").includes("مرتجعة جزئيًا"));assert.ok(invoiceRow("INV-FULL").includes("مرتجعة بالكامل"));
  assert.ok(!invoiceRow("INV-NONE").includes("data-invoice-return-status"));assert.ok(!invoiceRow("INV-DRAFT").includes("data-invoice-return-status"));
  assert.ok(invoiceRow("INV-PART").includes(module==="sales"?"مكتملة":"منشورة"),"return badge must preserve the original posting status");
  if(module==="sales"){
    assert.ok(invoiceRow("INV-NONE").includes('data-sale-pdf="1"'));
    assert.ok(invoiceRow("INV-PART").includes('data-sale-pdf="2"'));
    assert.ok(invoiceRow("INV-FULL").includes('data-sale-pdf="3"'));
    assert.ok(!invoiceRow("INV-DRAFT").includes("data-sale-pdf"));
    var button={disabled:false,isConnected:true,getAttribute:function(){return "1";}},event={target:{closest:function(selector){return selector==="[data-sale-pdf]"?button:null;}}};
    node.onclick(event);
    assert.strictEqual(opened.length,1);
    assert.deepStrictEqual(opened[0],["/api/printing/sales/1/pdf","_blank"]);
    win.AlKhayalHasPdfOpener=true;
    win.chrome={webview:{hostObjects:{alkhayalPdf:{open:function(base64){handed.push(base64);return Promise.resolve(true);}}}}};
    node.onclick(event);await settle();await settle();
    assert.strictEqual(opened.length,1,"desktop must keep the application view open");
    assert.strictEqual(handed.length,1,"desktop hands the PDF to the external opener");
    assert.ok(Buffer.from(handed[0],"base64").toString("utf8").startsWith("%PDF-1.4"));
    assert.ok(invoiceRow("INV-DRAFT").includes('class="invoice-suspended"')&&invoiceRow("INV-DRAFT").includes('class="badge warning">معلقة</span>'));
    assert.ok(invoiceRow("INV-NONE").includes('class="badge success">مكتملة</span>'));
    assert.ok(!invoiceRow("INV-NONE").includes("invoice-suspended")&&!invoiceRow("INV-PART").includes("invoice-suspended"));
  }
}
module.exports=async function(){await verify("purchasing","purchase-list","purchases-list","[data-purchase-list]");await verify("sales","sales-invoices","sales-list","#salesListResults");console.log("Sales and purchase invoice lists display persisted partial/full return badges without marking untouched invoices.");};
