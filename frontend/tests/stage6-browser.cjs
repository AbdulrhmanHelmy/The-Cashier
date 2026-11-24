const { chromium } = require(process.env.THECASHIER_QA_PLAYWRIGHT || 'playwright-core');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const registry = require('../data/screens.json');
const root = path.resolve('frontend');
const out = path.resolve('artifacts/purchases-stage6');
const baseline = process.env.THECASHIER_QA_STAGE6_BASELINE;
const origin = 'http://cashier-stage6.test';
const mime = { '.html':'text/html', '.js':'application/javascript', '.css':'text/css', '.json':'application/json', '.svg':'image/svg+xml', '.woff2':'font/woff2' };
const suppliers = [{ id:2, name:'شركة الاختبار العربية لتوريد مستلزمات المحلات التجارية', phone:'0012345678' }];
const product = { id:7, name:'صنف اختباري طويل لمراجعة عرض المنتجات في الفاتورة', productType:'stock', isActive:true, purchasePrice:'10.10', salePrice:'12.20',
  variants:[{id:71,name:'افتراضي',isActive:true}], units:[{id:72,name:'قطعة',isActive:true,isPurchaseUnit:true,isBase:true}] };
const draft = {id:41,status:'draft',supplierId:2,supplierName:suppliers[0].name,warehouseId:1,warehouseName:'المخزن الرئيسي',
  invoiceNumber:'001-QA-اختبار',invoiceDate:'2026-11-19',paymentMode:'credit',notes:'ملاحظات أصلية',totalFormatted:'10.10',
  lines:[{productId:7,productName:product.name,variantId:71,productUnitId:72,unitName:'قطعة',quantity:'1',purchaseUnitPrice:'10.10',saleUnitPrice:'12.20',lineTotalFormatted:'10.10'}] };

async function run() {
  fs.mkdirSync(out,{recursive:true});
  const browser = await chromium.launch({channel:'msedge'});
  try {
    const page = await browser.newPage({reducedMotion:'reduce'});
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    let writes = 0, invoice, release, releasePost, rejectSave, interruptPost, postRequests, returnLookup, printRequest, readOnly = false, autoProduct = false;
    await page.route(origin+'/**', async route => {
      const req = route.request(), url = new URL(req.url()), p = url.pathname;
      if (!p.startsWith('/api/')) {
        let file = path.join(root,p==='/'?'index.html':decodeURIComponent(p));
        if (!file.startsWith(root+path.sep)||!fs.existsSync(file)) return route.fulfill({status:404});
        if (baseline && /\/(?:purchasing|transaction-reports)\.js$/.test(p)) file=path.join(baseline,path.basename(p));
        return route.fulfill({path:file,contentType:mime[path.extname(file)]||'application/octet-stream'});
      }
      let data = [];
      const role=readOnly?"cashier":"administrator";
      if (p==='/api/auth/me') data = {id:1,username:'qa',role};
      if (p==='/api/auth/me/screen-access') data = {role,screenIds:readOnly?['dashboard','purchases-list','report-purchases']:registry.screens.map(s=>s.permissionRoute||s.route)};
      if (p==='/api/features/effective') data = {modules:registry.screens.filter(s=>s.featureKey).map(s=>({key:s.featureKey,isEnabled:true})),capabilities:[]};
      if (p==='/api/settings/general') data = {currencySymbol:'ج.م',dateFormat:'dd/MM/yyyy'};
      if (p.endsWith('/shifts/current')) data=null;
      if (p==='/api/inventory/warehouses') data = [{id:1,name:draft.warehouseName,isActive:true,isDefault:true},{id:2,name:'مخزن ثان',isActive:true}];
      if (p==='/api/suppliers') data = {items:url.searchParams.get('search')==='مفقود'?[]:suppliers,totalCount:1};
      if (p==='/api/suppliers/2') data = suppliers[0];
      if (p==='/api/inventory/products') data = {items:[product],totalCount:1};
      if (p==='/api/inventory/products/7') {
        if (!autoProduct) await new Promise(resolve => { release=resolve; });
        data=product;
      }
      if (p==='/api/purchases') data = invoice?[invoice]:[];
      if (p==='/api/purchases/41') data = invoice;
      if (p==='/api/returns/invoice/purchase/41') {
        returnLookup=p;
        data={...invoice,type:'purchase',partyName:suppliers[0].name,number:draft.invoiceNumber,lines:[]};
      }
      if (p==='/api/printing/jobs' && req.method()==='POST') {
        printRequest=req.postDataJSON();
        return route.fulfill({status:400,json:{success:false,error:{message:'طابعة الاختبار غير متاحة'}}});
      }
      if (p==='/api/reports/purchases') {
        const items=url.searchParams.get('search')==='مفقود'?[]:
          [{invoiceId:41,reference:draft.invoiceNumber,eventUtc:'2026-11-21T10:00:00Z',party:suppliers[0].name,total:'10.10',paymentMode:'cash'}];
        data={items,count:items.length,total:'10.10',returns:'0.00',netEvents:'10.10'};
      }
      if (p==='/api/purchases/drafts' || (p==='/api/purchases/41' && req.method()==='PUT')) {
        writes++;
        if (rejectSave) return route.fulfill({status:400,json:{success:false,error:{message:'رفض الحفظ الاختباري'}}});
        data = invoice = {...draft,...req.postDataJSON(),lines:draft.lines};
      }
      if (p==='/api/purchases/41/post') {
        postRequests.push(req.postDataJSON());
        await new Promise(resolve => {releasePost=resolve;});
        if (interruptPost) return route.abort('failed');
        invoice={...invoice,status:'posted'};
        data={invoice};
      }
      return route.fulfill({json:{success:true,data}});
    });
    const loc = s=>page.locator(s).first();
    const visible = s=>loc(s).waitFor({timeout:7000});
    const go = async route=>{
      await loc('#globalSearch').fill(route.replaceAll('-',' '));
      await loc('[data-search-route="'+route+'"]').click();
    };
    for (const [width,height,theme] of [[1024,768,'light'],[1024,768,'dark'],[1366,768,'light'],[1366,768,'dark'],[1600,1000,'light'],[1600,1000,'dark']]) {
      invoice=undefined;
      release=undefined;
      releasePost=undefined;
      rejectSave=false;
      interruptPost=false;
      postRequests=[];
      await page.setViewportSize({width,height});
      await page.goto(origin+'/');
      await visible('.home-page');
      await loc('[data-theme-choice="'+theme+'"]').click();
      const capture = name=>page.screenshot({path:path.join(out,[baseline?'before':'after',width+'x'+height,theme,name].join('-')+'.png')});
      await go('purchase-management');
      await visible('#purchaseSupplierSearch');
      assert.equal(await page.evaluate(()=>document.documentElement.dir==='rtl' && document.documentElement.scrollWidth<=innerWidth),true);
      await capture('editor');
      await loc('#purchaseSupplierSearch').fill('0012345678');
      await visible('[data-select-supplier="2"]');
      await capture('suppliers');
      await loc('[data-select-supplier="2"]').click();
      await loc('#purchaseSupplierSearch').fill('مفقود');
      await page.getByText('لا يوجد مورد مطابق.').waitFor();
      assert.equal((await loc('#purchaseSupplierSelected').textContent()).includes(suppliers[0].name),!!baseline);
      await capture('supplier-replacement');
      await loc('#purchaseSupplierSearch').fill('0012345678');
      await visible('[data-select-supplier="2"]');
      if (baseline) await loc('[data-select-supplier="2"]').click();
      else await loc('#purchaseSupplierSearch').press('Enter');
      await loc('#purchaseSearch').fill('صنف');
      await visible('[data-add-product="7"]');
      await capture('products');
      await loc('[data-add-product="7"]').click();
      await capture('loading-product');
      assert.equal(await loc('[data-purchase-save]').isDisabled(),!baseline);
      assert.equal(typeof release,'function');
      release();
      await visible('[data-line-field="quantity"]');
      await loc('#purchaseNumber').fill(draft.invoiceNumber);
      await loc('#purchaseNotes').fill('ملاحظات الاختبار');
      await loc('#purchaseWarehouse').selectOption('2');
      await capture('line');
      rejectSave=true;
      await loc('[data-purchase-save]').click();
      await page.getByText('رفض الحفظ الاختباري',{exact:false}).first().waitFor({timeout:7000});
      assert.equal(await loc('#purchaseNotes').inputValue(),'ملاحظات الاختبار');
      await capture('save-failed');
      rejectSave=false;
      const beforeWrites=writes;
      await loc('[data-purchase-save]').click();
      await visible('[data-purchase-post]');
      assert.equal(writes,beforeWrites+1);
      assert.equal(await loc('.purchase-line-total').textContent(),baseline?'—':'10.10');
      await loc('.purchase-lines-table').scrollIntoViewIfNeeded();
      await capture('saved');
      if (!baseline) {
        assert.equal(invoice.warehouseId,2);
        await loc('#purchaseNotes').fill('');
        assert.equal(await loc('[data-purchase-post]').isDisabled(),true);
        assert.equal(await loc('[data-purchase-state]').textContent(),'تغييرات غير محفوظة — احفظ المسودة قبل النشر');
        await loc('[data-purchase-save]').click();
        await page.waitForFunction(()=>!document.querySelector('[data-purchase-post]')?.disabled);
        assert.equal(invoice.notes,'');
      }
      if (!baseline) {
        interruptPost=true;
        await loc('[data-purchase-post]').click();
        await visible('[data-purchase-state]');
        await capture('posting');
        assert.equal(await loc('[data-purchase-save]').isDisabled(),true);
        releasePost();
        await page.getByText('تعذر تأكيد نتيجة النشر',{exact:false}).first().waitFor({timeout:7000});
        await capture('uncertain');
        interruptPost=false;
        releasePost=undefined;
      }
      await loc('[data-purchase-post]').click();
      await page.waitForFunction(()=>document.querySelector('[data-purchase-post]')?.disabled);
      await capture('post-pending');
      if (!baseline) {
        await loc('[data-tab-route="dashboard"]').first().click();
        releasePost();
        await loc('[data-tab-route="purchase-management"]').first().click();
        await visible('[data-purchase-last-posted]');
        assert.equal(postRequests.length,2);
        assert.equal(postRequests[0].operationKey,postRequests[1].operationKey);
      } else {
        releasePost();
      }
      await page.getByText(/تم نشر الفاتورة/).first().waitFor({timeout:7000});
      await go('purchases-list');
      await visible('[data-view-purchase="41"]');
      await capture('history');
      await loc('[data-view-purchase="41"]').click();
      await visible('.purchase-detail');
      await capture('details');
      await page.getByRole('button',{name:/طباعة الفاتورة/}).click();
      await page.getByText('طابعة الاختبار غير متاحة').first().waitFor();
      assert.equal(printRequest.documentId,41);
      assert.equal(printRequest.documentType,'purchase');
      await loc('[data-back-purchases]').click();
      await loc('[data-create-purchase-return="41"]').click();
      await page.waitForFunction(()=>document.querySelector('#returnNumber')?.value || document.querySelector('[data-return-preview]')?.textContent.includes('001-QA'));
      assert.equal(returnLookup,'/api/returns/invoice/purchase/41');
      await capture('return-entry');
      await go('report-purchases');
      await page.getByRole('table').getByText(baseline?'فوري':'نقدي',{exact:true}).waitFor();
      await capture('report');
      await loc('[data-results] button').first().click();
      await visible('.purchase-detail');
      await loc('[data-tab-route="report-purchases"]').first().click();
      await loc('[name="search"]').fill('مفقود');
      await loc('.report-filters button[type="submit"]').click();
      await page.getByText('لا توجد سجلات مطابقة.').waitFor();
      await capture('report-empty');
    }
    if (!baseline) {
      autoProduct=true;
      invoice={...draft,lines:Array.from({length:14},()=>({...draft.lines[0]}))};
      await page.setViewportSize({width:1024,height:768});
      await page.goto(origin+'/');
      await visible('.home-page');
      await loc('[data-theme-choice="dark"]').click();
      await go('purchases-list');
      await loc('[data-edit-purchase="41"]').click();
      await page.waitForFunction(()=>document.querySelectorAll('.purchase-line-main').length===14);
      await page.locator('.purchase-line-main').nth(7).scrollIntoViewIfNeeded();
      assert.equal(await loc('[data-purchase-save]').evaluate(button=>{
        const bounds=button.getBoundingClientRect();
        return bounds.bottom<=innerHeight && bounds.top>=0;
      }),true,'summary must remain available while scrolling a long invoice');
      await page.screenshot({path:path.join(out,'after-1024x768-dark-long-draft.png')});
      await loc('[data-line="7"] [data-line-field="quantity"]').fill('abc');
      assert.equal(await loc('[data-purchase-total]').textContent(),'—');
      await loc('[data-line="7"] [data-remove-line]').click();
      assert.equal(await page.locator('.purchase-line-main').count(),13);
      assert.equal(await page.evaluate(()=>document.activeElement.getAttribute('data-remove-line')),'7');
      const dialog=page.waitForEvent('dialog');
      const close=loc('[data-close-tab="purchase-management"]').click();
      await (await dialog).dismiss();
      await close;
      assert.equal(await page.locator('.purchase-line-main').count(),13);

      readOnly=true;
      await page.goto(origin+'/');
      await visible('.home-page');
      await go('purchases-list');
      await visible('[data-view-purchase="41"]');
      assert.equal(await page.locator('[data-new-purchase],[data-edit-purchase]').count(),0);
      await loc('[data-view-purchase="41"]').click();
      await visible('.purchase-detail');
      assert.equal(await page.locator('[data-resume-purchase]').count(),0);
      await page.screenshot({path:path.join(out,'after-1024x768-dark-read-only-detail.png')});
    }
    assert.deepEqual(errors,[]);
    console.log('Stage 6 Edge checks passed across six theme and viewport combinations.');
  } finally { await browser.close(); }
}
run().catch(error=>{console.error(error);process.exitCode=1;});
