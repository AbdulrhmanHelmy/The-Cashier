"use strict";
var assert=require("assert"),fs=require("fs"),path=require("path"),vm=require("vm");

module.exports=function(){
  var source=fs.readFileSync(path.join(__dirname,"../js/modules/catalog.js"),"utf8");
  var start=source.indexOf("  function bindProfitMargin(host){"),end=source.indexOf("  function bindBundleEditor(host){",start);
  assert.ok(start>=0&&end>start,"shared product margin binding exists");
  var bind=vm.runInNewContext("(function(){"+source.slice(start,end)+"return bindProfitMargin;})()");
  function input(value){return{value:value,listeners:{},addEventListener:function(name,handler){this.listeners[name]=handler;},change:function(value){this.value=value;this.listeners.input();}};}
  var purchase=input("10.00"),sale=input("12.00"),margin=input("");
  var fields={"#catalogPurchasePrice":purchase,"#catalogPrice":sale,"#catalogProfitMargin":margin};
  bind({querySelector:function(selector){return fields[selector];}});
  assert.strictEqual(margin.value,"20.00","existing product margin is displayed");
  margin.change("12.5");assert.strictEqual(sale.value,"11.25","decimal margin updates sale price to two places");
  purchase.change("20.00");assert.strictEqual(sale.value,"22.50","purchase changes use the last edited margin");
  sale.change("25.00");assert.strictEqual(margin.value,"25.00","editing sale updates margin without a loop");
  purchase.change("0.00");assert.strictEqual(margin.value,"","zero cost leaves margin empty");
  purchase.change("");margin.change("30");assert.strictEqual(sale.value,"25.00","missing cost does not overwrite sale");
  var win={AlKhayalUtils:{escape:function(value){return String(value==null?"":value);}},AlKhayalUI:{badge:function(value){return String(value);}},AlKhayalApi:{},AlKhayalRenderers:{}};
  win.window=win;
  vm.runInNewContext(source.replace('  function productFields(record,categories,units,canSetState,componentProducts){','  window.__productFields=productFields;\n  function productFields(record,categories,units,canSetState,componentProducts){'),{window:win});
  var markup=win.__productFields({name:"منتج محفوظ",description:"وصف محفوظ",salePrice:"25.00",purchasePrice:"20.00",reorderPoint:"3",isActive:true},[{id:1,name:"تصنيف",isActive:true}],[{id:2,name:"قطعة",symbol:"ق",isActive:true}],true,[]);
  ["catalogName","catalogDescription","catalogCategoryInput","catalogUnit","catalogPurchasePrice","catalogPrice","catalogProfitMargin","catalogReorderPoint","catalogActive","catalogBundleMode"].forEach(function(id){assert.ok(markup.includes('id="'+id+'"'),id+" remains in the compact product form");});
  assert.ok(markup.includes('value="منتج محفوظ"')&&markup.includes('value="25.00"')&&markup.includes('value="20.00"'),"saved product values remain populated");
  assert.ok(markup.includes('catalog-form-section')&&markup.includes('هوية المنتج وتصنيفه')&&markup.includes('الأسعار والمخزون'),"compact groups are rendered by the active product form");
};
