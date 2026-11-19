"use strict";
var assert=require("assert"),fs=require("fs"),path=require("path"),root=path.join(__dirname,"..");
module.exports=function(){
  var helper=fs.readFileSync(path.join(root,"js/services/export-service.js"),"utf8"),index=fs.readFileSync(path.join(root,"index.html"),"utf8"),catalog=fs.readFileSync(path.join(root,"js/modules/catalog.js"),"utf8"),inventory=fs.readFileSync(path.join(root,"js/modules/inventory.js"),"utf8"),renderers=fs.readFileSync(path.join(root,"js/modules/renderers.js"),"utf8"),sales=fs.readFileSync(path.join(root,"js/modules/sales.js"),"utf8"),purchases=fs.readFileSync(path.join(root,"js/modules/purchasing.js"),"utf8");
  assert.ok(index.includes("js/services/export-service.js"));["products","customers","suppliers","warehouses","sales","purchases"].forEach(function(key){assert.ok(new RegExp('dataset\\s*:\\s*["\\\']'+key+'["\\\']').test(catalog+inventory+renderers+sales+purchases),"missing real export button: "+key);});
  assert.ok(helper.includes("pickFile(format")&&helper.indexOf("loadingModal")>helper.indexOf("pickFile(format"),"save cancellation must happen before loading");
  assert.ok(helper.includes("credentials: \"same-origin\"")&&helper.includes("/api/exports/")&&helper.includes("saveFile(path")&&helper.includes("if (busy) return"));
  assert.ok(helper.includes("closeModal(); global.AlKhayalUI.toast")&&helper.includes("catch (error) { global.AlKhayalUI.closeModal()"));
  assert.ok(!renderers.includes('data-supplier-export>تصدير CSV</button>'),"supplier DOM-row CSV implementation must be removed");
  console.log("Backend export buttons, filters, desktop cancellation/save, loading and duplicate-submit guards passed.");
};
if(require.main===module)module.exports();
