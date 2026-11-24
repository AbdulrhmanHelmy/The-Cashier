
const { chromium } = require(process.env.THECASHIER_QA_PLAYWRIGHT || 'playwright-core');
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const root = path.resolve(process.env.THECASHIER_QA_FRONTEND || 'frontend'), registry = require(path.join(root, 'data/screens.json'));
const out = path.resolve('artifacts/catalog-stage3');
fs.mkdirSync(out, { recursive: true });
const allKeys = [...new Set(registry.screens.map(s=>s.featureKey).filter(Boolean))];
const allRoutes = [...new Set(registry.screens.map(s=>s.permissionRoute||s.route))];
const mime = {'.html':'text/html; charset=utf-8','.js':'application/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.svg':'image/svg+xml','.png':'image/png','.woff2':'font/woff2','.json':'application/json'};
const role = 'administrator', grants = allRoutes, features = allKeys, userId = 21, shift = null;
const phase=process.env.THECASHIER_QA_PHASE||'after';
const product={id:1,name:'منتج اختبار طويل لملصق الباركود باللغة العربية',baseUnitName:'قطعة',unitSymbol:'ق',primaryBarcode:'2912345678',productType:'stock',isActive:true,salePrice:'12.50',version:1,units:[{id:1,name:'قطعة',isActive:true,isSaleUnit:true}],variants:[{id:1,isDefault:true,isActive:true,barcodes:[{id:1,productUnitId:1,format:'code128',code:'2912345678'}]}]};
function response(p,url) {
 if(p==='/api/catalog/categories') return [{id:1,name:'أصناف الاختبار',isActive:true,sortOrder:0}];
 if(p==='/api/catalog/units') return [{id:1,name:'قطعة',code:'PCS',symbol:'ق',isActive:true}];
 if(p==='/api/printing/settings') return {profiles:[],printers:[],templates:[]};
 if(p==='/api/printing/printers') return [{name:'Synthetic fixture'}];
 if(p==='/api/labels/settings') return {defaultProfileId:'qa',profiles:[{id:'qa',printerName:'Synthetic fixture',templateId:'label'}],templates:[{id:'label',stock:{pageWidthMm:35,pageHeightMm:25}}]};
 if(p==='/api/inventory/balance-page') return {items:[],totalCount:0,page:1,pageSize:25};
 if(p==='/api/inventory/warehouses') return [{id:1,name:'مخزن الاختبار',isActive:true,isDefault:true,productCount:0,quantity:0}];
 if(p==='/api/catalog/products/1') return product;
 if(p==='/api/catalog/products') return {items:url.searchParams.get('search')?[]:[product],totalCount:url.searchParams.get('search')?0:1,page:1,pageSize:20};
 if(p==='/api/labels/options') return {defaultProfileId:'qa',profiles:[{id:'qa',printerName:'Synthetic fixture',templateId:'label',paper:{pageWidthMm:35,pageHeightMm:25}}],templates:[{id:'label'}]};
 if(p==='/api/labels/product-preview') return {profileId:'qa',templateId:'label',printerName:'Synthetic fixture',printerConfigured:true,barcode:product.primaryBarcode,productName:product.name,unit:'قطعة',price:'12.50 ج.م',pageWidthMm:35,pageHeightMm:25,pngBase64:fs.readFileSync(path.join(out,'35x25.png')).toString('base64')};
 if(p==='/api/labels/jobs'||p==='/api/printing/jobs'||p.startsWith('/api/printing/test/')) throw new Error('Browser QA must never dispatch a print job');
 if (p==='/api/auth/me') return {id:userId,username:'shell-qa',displayName:'مستخدم الاختبار',role};
 if (p==='/api/auth/me/screen-access') return {role,screenIds:grants};
 if (p==='/api/features/effective') return {modules:features.map(key=>({key,isEnabled:true})),capabilities:[]};
 if (p==='/api/settings/general') return {currencySymbol:'ج.م',dateFormat:'dd/MM/yyyy',timeFormat:'HH:mm'};
 if (p.endsWith('/shifts/current')) return shift;
 if (p==='/api/alerts') return [{id:'1',type:'low-stock',title:'مخزون منخفض',message:'صنف اختبار',read:false}];
 if (p.endsWith('/warehouses')) return [{id:1,name:'مخزن الاختبار',isActive:true,isDefault:true}];
 if (p.endsWith('/customers') || p.endsWith('/suppliers') || p.endsWith('/invoices') || p.endsWith('/products')) return {items:[],totalCount:0};
 return [];
}
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 const errors=[];
 try {
 const page=await browser.newPage({viewport:{width:1600,height:1000},reducedMotion:'reduce'});
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('http://cashier-stage3.test/**',async route=>{
   const url=new URL(route.request().url()), pathname=url.pathname;
   if(pathname.startsWith('/api/')) return route.fulfill({json:{success:true,data:response(pathname,url)}});
   const file=path.join(root, pathname==='/'?'index.html':decodeURIComponent(pathname));
   if(!file.startsWith(root+path.sep)||!fs.existsSync(file)) return route.fulfill({status:404});
   return route.fulfill({path:file,contentType:mime[path.extname(file)]||'application/octet-stream'});
 });
 const loc=(s)=>page.locator(s).first();
 const capture=async name=>{await page.evaluate(()=>document.fonts.ready);await page.screenshot({path:path.join(out,name+'.png')});};
 const check=async (selector)=>loc(selector).waitFor({timeout:7000});
 const click=async (selector)=>loc(selector).click({timeout:7000});
 const go=async id=>{
   const selector='[data-search-route="'+id+'"]';
   await loc('#globalSearch').fill(id.replaceAll('-',' '));
   await click(selector);
   await check('#workspaceTabs [data-tab-route="'+id+'"][aria-current]');
 };
 await page.goto('http://cashier-stage3.test/');
 await check('.home-page');
 for(const [width,height,theme] of [[1024,768,'light'],[1024,768,'dark'],[1366,768,'light'],[1366,768,'dark'],[1600,1000,'light'],[1600,1000,'dark']]){
   await page.setViewportSize({width,height});
   await click('[data-theme-choice="'+theme+'"]');
   await go('products-list');
   const prefix=phase+'-'+width+'-'+theme+'-';
   await check('[data-product-id="1"]');
   await capture(prefix+'products');
   await loc('#catalogSearch').fill('لا يوجد');
   await check('#catalogResults .empty-state');
   await capture(prefix+'no-results');
   if(phase==='after') await click('[data-catalog-clear-filters]');
   else await loc('#catalogSearch').fill('');
   await check('[data-product-id="1"]');
   await click('.catalog-actions-menu summary');
   await click('[data-product-barcodes]');
   await check('[data-barcode-edit="1"]');
   await capture(prefix+'barcode-manager');
   const managerPrint=loc('[data-barcode-print="1"]');
   await managerPrint.focus();
   await page.keyboard.press('Enter');
   await page.waitForFunction(()=>document.querySelector('[data-preview-image]')?.naturalWidth>0);
   await capture(prefix+'manager-label-preview');
   await page.keyboard.press('Escape');
   if(phase==='after'){
     assert.ok(await loc('[data-barcode-preview-dialog]').isHidden());
     assert.equal(await managerPrint.evaluate(button=>button===document.activeElement),true);
     await page.keyboard.press('Escape');
   }
   const summary=loc('.catalog-actions-menu summary');
   if(!(await summary.evaluate(el=>el.parentElement.open))) await summary.click();
   await click('[data-product-labels]');
   if(phase==='before') assert.match(await loc('[data-product-print] [role="alert"]').textContent(),/مقاس ورق الملصق غير متاح/);
   else {
     await page.waitForFunction(()=>{const img=document.querySelector('[data-direct-image]');return img&&!img.hidden&&img.naturalWidth>0;});
     assert.equal(await loc('.modal-actions [data-direct-print]').isEnabled(),true);
     assert.equal(await loc('html').getAttribute('dir'),'rtl');
     const sizes=await loc('[data-direct-image]').evaluate(img=>[img.naturalWidth,img.naturalHeight,img.width<=img.parentElement.clientWidth]);
     assert.deepEqual(sizes,[414,296,true]);
   }
   await capture(prefix+'label-preview');
   await page.keyboard.press('Escape');

   await click('[data-edit-product]');
   await check('#catalogName');
   assert.equal(await loc('#catalogName').inputValue(),product.name);
   await capture(prefix+'product-edit');
   await page.keyboard.press('Escape');
   await click('[data-add-product]');
   await check('#catalogProductForm');
   await capture(prefix+'product-create');
   await click('button[form="catalogProductForm"]');
   await page.waitForFunction(()=>document.querySelector('#catalogNameError').textContent.trim().length>0,{},{timeout:7000});
   if(phase==='after') assert.equal(await page.evaluate(()=>document.activeElement.id),'catalogName');
   await capture(prefix+'product-validation');
   await go('products-list');
   await click('[data-manage-categories]');
   await check('#categoryManagementSearch');
   await capture(prefix+'categories');
   await go('products-list');
   await click('[data-manage-units]');
   await check('#unitManagementSearch');
   await capture(prefix+'units');
   await go('warehouses-list');
   await check('#inventoryWarehouse');
   await capture(prefix+'warehouse');
   await click('[data-view="count"]');
   await check('[data-start-count]');
   await capture(prefix+'inventory-count');
   await click('[data-operation="adjustment"]');
   await check('.modal [data-direction]');
   await capture(prefix+'stock-adjustment');
   await page.keyboard.press('Escape');
   await click('[data-view="history"]');
   await check('[data-history-type]');
   await capture(prefix+'stock-history');
   for(const operation of ['manual_receipt','manual_issue']){
     await click('[data-operation="'+operation+'"]');
     await check('.modal');
     await capture(prefix+'stock-'+operation);
     await page.keyboard.press('Escape');
   }
   await click('.sidebar-footer [data-route="settings-program"]');
   await click('[data-settings-route="settings-printers"]');
   await check('[data-simple-size]');
   await capture(prefix+'printers');
   await go('products-list');
   await click('[data-label-bulk]');
   await check('[data-label-search]');
   if(phase==='after') assert.match(await loc('[data-batch-options]').textContent(),/35 × 25/);
   await capture(prefix+'label-batch');
 }
 assert.deepEqual(errors,[]);
 console.log('PASS '+phase+' product and barcode UI in both themes at three viewports');
 } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
