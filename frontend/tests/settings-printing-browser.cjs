
const {chromium}=require(process.env.CASHIER_PLAYWRIGHT || 'playwright-core');
const fs=require('fs'), path=require('path'), vm=require('vm');
const root=path.resolve('frontend'), out=process.env.CASHIER_SETTINGS_EVIDENCE || 'artifacts/settings-printing-implementation', origin='http://cashier-audit.test';
const registry=require(root+'/data/screens.json');
const src=fs.readFileSync(root+'/tests/printing-tests.js','utf8');
const config=vm.runInNewContext(src.slice(src.indexOf('var sections ='),src.indexOf('function harness()'))+'defaults()');
const assert=require('node:assert/strict');
const samplePng=process.env.CASHIER_RECEIPT_FIXTURE ? fs.readFileSync(process.env.CASHIER_RECEIPT_FIXTURE).toString('base64') : 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/w1sAAAAASUVORK5CYII=';
const labelFixture=process.env.CASHIER_LABEL_FIXTURE || 'artifacts/catalog-stage3/35x25.png';
const labelPng=fs.existsSync(labelFixture) ? fs.readFileSync(labelFixture).toString('base64') : samplePng;
fs.mkdirSync(out,{recursive:true});
const clone=x=>JSON.parse(JSON.stringify(x));
config.printers=[{id:'receipt-qa',name:'طابعة الإيصالات',windowsPrinterName:'Synthetic PDF',templateId:'builtin-sale'},{id:'archived-qa',name:'طابعة الفرع',windowsPrinterName:'Offline-Branch-Receipt-Printer-With-A-Long-Name',templateId:'builtin-sale'}];
config.profiles.forEach(p=>{p.printerId='receipt-qa';p.printerName='Synthetic PDF'});
const mime={'.html':'text/html','.js':'application/javascript','.css':'text/css','.json':'application/json','.woff2':'font/woff2','.svg':'image/svg+xml'};
(async()=>{
 const browser=await chromium.launch({channel:'msedge'}), captures=[], trials=[], errors=[];
 try {
 const page=await browser.newPage({reducedMotion:'reduce'});
 let mode='normal', hold=0, settingsDelay=0, brandDelay=0, failSettings=false, failBrand=false, timing, count=0;
 const loc=s=>page.locator(s).first();
 await page.route(origin+'/**',async r=>{
 const req=r.request(), p=new URL(req.url()).pathname;
 if(!p.startsWith('/api/')){
 const file=path.join(root,p==='/'?'index.html':decodeURIComponent(p));
 return file.startsWith(root+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()?r.fulfill({path:file,contentType:mime[path.extname(file)]||'application/octet-stream'}):r.fulfill({status:404});
 }
 if(p==='/api/labels/design/preview')return r.fulfill({json:{success:true,data:{pngBase64:labelPng}}});
 if(['/api/printing/preview/branding','/api/printing/preview/document'].includes(p))return r.fulfill({json:{success:true,data:{pngBase64:samplePng}}});
 if(req.method()!=='GET') return r.fulfill({status:405,json:{success:false,error:{message:'Audit fixture blocks writes'}}});
 let d=[];
 if(p.endsWith('/shifts/current'))d=null;
 if(p==='/api/operation-logs')d={page:1,totalCount:0,items:[]};
 if(p==='/api/labels/settings')d={version:1,defaultProfileId:'qa',profiles:[{id:'qa',templateId:'qa',printerName:'Synthetic PDF'}],templates:[{id:'qa',stock:{pageWidthMm:50,pageHeightMm:30}}]};
 if(p==='/api/printing/documents')d=[{id:42,reference:'QA-42',party:'عميل أو مورد اختباري',amount:'125.50'}];
 if(p==='/api/auth/me')d={id:1,username:'qa',displayName:'مدير الاختبار',role:'administrator'};
 if(p==='/api/auth/me/screen-access')d={role:'administrator',screenIds:registry.screens.map(s=>s.permissionRoute||s.route)};
 if(p==='/api/features/effective')d={modules:registry.screens.filter(s=>s.featureKey).map(s=>({key:s.featureKey,isEnabled:true})),capabilities:[]};
 if(p==='/api/settings/general')d={currencyCode:'EGP',currencySymbol:'ج.م',dateFormat:'dd/MM/yyyy',timeFormat:'HH:mm',decimalPrecision:2};
 if(p==='/api/settings/company'){await new Promise(resolve=>setTimeout(resolve,brandDelay));if(failBrand)return r.fulfill({status:503,json:{success:false}});d={businessName:'منشأة الاختبار العربية الطويلة للتجارة والتوريدات ومستلزمات نقاط البيع',displayName:'الفرع الرئيسي'};}
 if(p==='/api/users')d=[{id:2,displayName:'أمين الصندوق',username:'qa-cashier',role:'cashier',isActive:true}];
 if(p==='/api/settings/backups')d={configuration:{directory:'C:/QA/Configured'},files:[]};
 if(p==='/api/printing/settings'){await new Promise(resolve=>setTimeout(resolve,settingsDelay));if(failSettings)return r.fulfill({status:503,json:{success:false}});}
 if(p==='/api/printing/settings'||p==='/api/printing/defaults')d=clone(config);
 if(p==='/api/printing/printers'){count++;const start=Date.now(); const current=timing; const state=mode;
 await new Promise(resolve=>setTimeout(resolve,hold));if(current)current.discovery={start:start-current.ts,end:Date.now()-current.ts};
 if(state==='error')return r.fulfill({status:503,json:{success:false,error:{message:'تعذر قراءة طابعات Windows — اختبار معزول'}}});
 d=state==='empty'?[]:[{name:'Synthetic PDF',status:'available',papers:[{name:'A4',widthMm:210,heightMm:297}]}];
 }
 if(timing&&['/api/printing/settings','/api/settings/company'].includes(p))timing[p]=Date.now()-timing.ts;
 return r.fulfill({json:{success:true,data:d,error:null}});
 });
 page.on('pageerror',e=>errors.push(String(e)));
 const browserVersion=browser.version();
 const go=async route=>{if(await loc('[data-settings-route="'+route+'"]').isVisible())return loc('[data-settings-route="'+route+'"]').click({timeout:3000});
 await loc('[data-flyout-route="settings-program"], [data-route="settings-program"]').first().click({timeout:3000});
 if(route!=='settings-program')await loc('[data-settings-route="'+route+'"]').click({timeout:3000});};
 let width,height,theme;
 const capture=async(route,state)=>{const file=[width+'x'+height,theme,route,state].join('-')+'.png';await page.screenshot({path:out+'/'+file,timeout:5000});captures.push({file,width,height,theme,route,state});};
 const open=async()=>{await page.goto(origin+'/');await loc('.home-page').waitFor({timeout:6000});await loc('[data-theme-choice="'+theme+'"]').click({timeout:5000});};

 const waitText = (selector, text) => page.locator(selector).filter({hasText:text}).first().waitFor();
 const finish = () => page.waitForFunction(() => {
   const refresh=document.querySelector('[data-printer-manager] > .printing-actions [data-refresh-printers]');
   return refresh && !refresh.disabled;
 });
 const clickRefresh = () => page.evaluate(() => {
   const button=document.querySelector('[data-printer-manager] > .printing-actions [data-refresh-printers]');
   for(let i=0;i<5;i++) button.dispatchEvent(new MouseEvent('click',{bubbles:true}));
 });
 const closeTab = () => loc('[data-close-tab="settings-printers"]').click();
 try {
   width=1366;height=768;theme='light';await page.setViewportSize({width,height});
   for(const delay of [0,1250,3000]) {
     for(let i=1;i<=(delay===3000?2:5);i++){
       hold=delay;settingsDelay=0;await open();await go('settings-program');
       const before=count;timing={ts:Date.now(),delay,i,navigation:'cold'};
       await go('settings-printers');timing.navigationClick=Date.now()-timing.ts;
       await loc('[data-printer-manager]').waitFor();timing.firstContent=Date.now()-timing.ts;
       await loc('[data-edit-printer]').waitFor();timing.profiles=Date.now()-timing.ts;
       await loc('[data-assignment]').waitFor();timing.assignments=Date.now()-timing.ts;
       if(delay===1250&&i===1){
         assert.equal(await loc('[data-refresh-printers]').isDisabled(),true);
         await capture('settings-printers','simulated-delay-saved-settings');
       }
       await finish();timing.devicesReady=Date.now()-timing.ts;
       await loc('[data-simple-save]').waitFor();timing.fullReady=Date.now()-timing.ts;
       timing.discoveryRequests=count-before;assert.equal(timing.discoveryRequests,1);
       trials.push(timing);timing=null;
     }
   }
   await loc('[data-tab-route="settings-program"]').click();
   const warmStart=Date.now(), warmCount=count;
   await loc('[data-tab-route="settings-printers"]').click();
   await loc('[data-assignment]').waitFor();
   trials.push({navigation:'retained',assignments:Date.now()-warmStart});
   await page.waitForTimeout(250);assert.equal(count,warmCount,'snapshot activation must not trigger discovery');

   hold=1200;
   const before=count;
   await clickRefresh();await clickRefresh();
   await loc('[data-tab-route="settings-program"]').click();
   await page.waitForTimeout(1350);
   await loc('[data-tab-route="settings-printers"]').click();await finish();
   assert.equal(count-before,1,'rapid manual refresh and returning use one HTTP request');
   await capture('settings-printers','simulated-background-completion');
   assert.equal(await page.locator('[data-assignment]').count(),9);
   assert.deepEqual(await page.locator('[data-assignment]').evaluateAll(nodes=>nodes.map(node=>node.value)),Array(9).fill('receipt-qa'));
   await capture('settings-printers','simulated-success-registered-and-unavailable');
   await loc('[data-assignment="8"]').scrollIntoViewIfNeeded();
   await capture('settings-printers','simulated-nine-document-assignments');
   await loc('[data-printer-advanced] summary').click();
   await loc('[data-save-printing]').waitFor();
   assert.equal(count-before,1,'advanced editor uses the completed scan');

   const select=loc('[data-simple-size]');await select.selectOption('custom');
   await loc('[data-simple-width]').fill('63.5');
   for(const state of ['error','empty','normal']){
     mode=state;hold=500;
     const before=count;
     await loc('[data-edit-printer="receipt-qa"]').click();
     assert.equal(await loc('[data-friendly]').evaluate(e=>e===document.activeElement),true);
     await loc('[data-friendly]').fill('مسودة غير محفوظة');
     await loc('[data-width]').fill('86');
     await loc('[data-footer]').fill('تذييل قيد التحرير');
     await loc('[data-profile-refresh]').click();
     await loc('[data-height]').fill('210');
     await waitText('[data-profile-discovery]',state==='error'?'فشل تحديث':state==='empty'?'لا توجد':'الطابعات المكتشفة');
     assert.equal(count-before,1);
     assert.equal(await loc('[data-windows-printer]').inputValue(),'Synthetic PDF');
     assert.equal(await loc('[data-friendly]').inputValue(),'مسودة غير محفوظة');
     assert.equal(await loc('[data-width]').inputValue(),'86');
     assert.equal(await loc('[data-height]').inputValue(),'210');
     assert.equal(await loc('[data-footer]').inputValue(),'تذييل قيد التحرير');
     assert.equal(await loc('[data-simple-width]').inputValue(),'63.5');
     await page.keyboard.press('Escape');
     assert.equal(await loc('[data-edit-printer="receipt-qa"]').evaluate(e=>e===document.activeElement),true);
     await capture('settings-printers','simulated-'+state+'-after-refresh');
   }
   mode='error'; hold=650; await clickRefresh();
   await loc('[data-tab-route="settings-program"]').click();
   await page.waitForTimeout(750);await loc('[data-tab-route="settings-printers"]').click();
   await waitText('[data-windows-status]','فشل تحديث');
   mode='normal';hold=700;const reopenCount=count;
   await clickRefresh();await closeTab();await go('settings-printers');await finish();
   assert.equal(count-reopenCount,1,'replacement view joins the outstanding HTTP request');

   hold=1000;settingsDelay=1250;brandDelay=2300;
   await open();await go('settings-printers');await loc('[data-printer-manager] [role="status"]').waitFor();
   await capture('settings-printers','simulated-initial-navigation');
   await loc('[data-simple-save]').waitFor();
   assert.equal(await loc('[data-assignment]').count(),0,'label settings arrive independently');
   await loc('[data-assignment]').waitFor();await finish();
   await waitText('[data-branding-status]','جارٍ تحميل بيانات الشركة');
   await loc('[data-branding-status]').filter({hasText:/^$/}).waitFor({state:'attached'});
   settingsDelay=brandDelay=0;failSettings=failBrand=true;hold=0;
   await open();await go('settings-printers');await loc('[data-retry]').waitFor();
   assert.equal(await loc('[data-save-assignments]').count(),0);
   failSettings=false;await loc('[data-retry]').click();await waitText('[data-branding-status]','تعذر تحميل');
   assert.equal(await page.locator('[data-assignment]').count(),9);
   failBrand=false;await loc('[data-retry-branding]').click();
   await loc('[data-retry-branding]').waitFor({state:'hidden'});

   for(const size of [[1024,768],[1366,768],[1600,1000]]) for(const selectedTheme of ['light','dark']){
     [width,height]=size;theme=selectedTheme;await page.setViewportSize({width,height});
     await open();await go('settings-printers');await finish();
     assert.equal(await page.locator('html').getAttribute('dir'),'rtl');
     await capture('settings-printers','simulated-ready');
     await loc('[data-add-printer]').click();
     assert.equal(await loc('[data-modal-save]').isVisible(),true);
     await page.keyboard.press('Tab');
     assert.equal(await loc('[data-windows-printer]').evaluate(e=>e===document.activeElement),true);
     await capture('settings-printers','simulated-add-dialog');
     await loc('[data-modal-save]').click();
     assert.equal(await loc('[data-paper]').evaluate(e=>e===document.activeElement),true);
     await capture('settings-printers','simulated-required-paper');
     await loc('[data-paper]').selectOption('80mm');
     await loc('[data-preview-image]').waitFor();
     await page.getByRole('button',{name:'انتقل إلى المعاينة'}).click();
     assert.equal(await page.getByRole('region',{name:'معاينة الإيصال'}).evaluate(e=>e===document.activeElement),true);
     await loc('[data-preview-image]').scrollIntoViewIfNeeded();
     await capture('settings-printers','simulated-sample-receipt');
     await page.keyboard.press('Escape');
     await loc('[data-simple-save]').scrollIntoViewIfNeeded();
     await loc('[data-design-img]').waitFor();
     await loc('[data-design-img]').scrollIntoViewIfNeeded();
     await capture('settings-printers','simulated-barcode-settings');
   }
   await loc('[data-open-designer]').click();
   await loc('[data-doc-image]').waitFor();
   await loc('[data-doc-zoom]').fill('125');
   assert.equal(await loc('[data-doc-image]').evaluate(node=>node.style.width),'125%');
   await loc('[data-doc-mode]').selectOption('actual');
   await loc('[data-doc-choice="42"]').click();
   await waitText('[data-doc-status]','مستند محفوظ');
   await page.keyboard.press('Escape');

   await go('settings-appearance');
   for(const appearance of ['light','dark','system']){
     await loc('[data-appearance="'+appearance+'"]').focus();
     await page.keyboard.press('Enter');
     assert.equal(await loc('[data-appearance][aria-pressed="true"]').getAttribute('data-appearance'),appearance);
     assert.equal(await page.locator('[data-appearance][aria-pressed="false"]').count(),2);
   }
   await go('business-activities');
   for(const field of ['name','prefix','productLength','valueType','valueLength','valueDecimals']) {
     assert.ok(await loc('[data-scale-profile] [data-field="'+field+'"]').evaluate(node=>node.labels.length===1&&node.labels[0].textContent.trim().length));
   }
   assert.deepEqual(errors,[]);
 } finally {
   fs.writeFileSync(out+'/after.json',JSON.stringify({browserVersion,captures,trials,errors},null,2));
 }
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1});
