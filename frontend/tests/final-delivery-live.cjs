const { chromium } = require(process.env.THECASHIER_QA_PLAYWRIGHT || 'playwright-core');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

// The host directory is created by the disposable QA host and contains a generated credential.
const fixture = process.argv[2];
const origin = fs.readFileSync(path.join(fixture, 'url.txt'), 'utf8').trim();
assert.match(origin, /^http:\/\/127\.0\.0\.1:[1-9]\d*$/);
const output = path.resolve('artifacts/final-stage');

async function verifySales(page, api) {
  const orders = await api('delivery/orders');
  assert.equal(orders.total,20,'run the disposable delivery loop first');
  const reference = await api('sales/'+orders.items[0].salesInvoiceId);
  assert.match(reference.customerName,/تجريبي/);
  const postSale = async paymentMode => {
    const draft = await api('sales/drafts','POST',{
      warehouseId:reference.warehouseId,invoiceDate:reference.invoiceDate,paymentMode,discount:'0.00',
      customerId:paymentMode === 'credit' ? reference.customerId : null,
      lines:reference.lines.map(line => ({
        variantId:line.variantId,productUnitId:line.productUnitId,quantity:line.quantity,unitPrice:line.unitPrice,discount:line.discount
      }))
    });
    return api('sales/'+draft.id+'/post','POST',{operationKey:'final-live-cancel-sale-'+draft.id});
  };
  const sale = await postSale('cash');
  const credit = await postSale('credit');
  const loc = selector => page.locator(selector).first();
  const go = async route => {
    await loc('#globalSearch').fill(route.replaceAll('-',' '));
    await loc('[data-search-route="'+route+'"]').click();
  };
  await page.setViewportSize({width:1366,height:768});
  await page.goto(origin+'/');
  await loc('.home-page').waitFor();
  await go('report-sales');
  await loc('.report-filters [name="search"]').fill(sale.invoiceNumber);
  await loc('.report-filters button.primary').click();
  await loc('[data-report-route="sales-management"][data-report-id="'+sale.id+'"]').click();
  await loc('[data-invoice-identity]').waitFor();
  assert.match(await loc('.touch-cart-head').innerText(),new RegExp(sale.invoiceNumber));
  await loc('[data-close-tab="sales-management"]').click();
  await go('sales-management');
  await loc('#salesSearch').fill('QA-FINAL-NO-MATCH');
  await page.getByText('لا توجد فواتير بيع بعد.').waitFor();
  await loc('#salesSearch').fill(sale.invoiceNumber);
  await loc('[data-sale-pdf="'+sale.id+'"]').waitFor();
  assert.equal(await page.locator('#salesListResults tbody tr').count(),1);
  const pdf = await page.request.get(origin+'/api/printing/sales/'+sale.id+'/pdf');
  assert.equal(pdf.ok(),true);
  assert.equal((await pdf.body()).subarray(0,5).toString(),'%PDF-');
  const printing = await api('printing/settings');
  const template = printing.templates.find(item => item.id === printing.profiles.find(profile => profile.documentType === 'sale').templateId);
  assert.ok(template,'expected the sale receipt template in printing settings');
  const preview = await api('printing/preview/document','POST',{template,documentType:'sale',documentId:sale.id});
  assert.equal(Buffer.from(preview.pngBase64,'base64').subarray(1,4).toString(),'PNG');
  await loc('[data-invoice-action="'+sale.id+'"]').click();
  await loc('[data-confirm-invoice-action]').click();
  assert.equal(await loc('[data-cancel-reason]').getAttribute('aria-invalid'),'true');
  assert.equal(await page.evaluate(() => document.activeElement.hasAttribute('data-cancel-reason')),true);
  await page.screenshot({path:path.join(output,'live-cancellation-validation.png')});
  await page.keyboard.press('Escape');
  assert.equal((await api('sales/'+sale.id)).status,'posted');
  await loc('[data-invoice-action="'+sale.id+'"]').click();
  await loc('[data-cancel-reason]').fill('سبب اختبار نهائي');
  const canceled = page.waitForResponse(response => response.url().endsWith('/api/sales/'+sale.id+'/cancel'));
  await loc('[data-confirm-invoice-action]').click();
  const cancellation = await canceled;
  assert.equal(cancellation.status(),409);
  const refusal = (await cancellation.json()).error;
  assert.equal(refusal.code,'cash_refund_policy');
  await page.getByText(refusal.message,{exact:true}).waitFor();
  assert.equal(await loc('[data-sale-action-error]').textContent(),refusal.message);
  assert.equal((await api('sales/'+sale.id)).status,'posted');
  await page.keyboard.press('Escape');
  await loc('#salesSearch').fill(credit.invoiceNumber);
  await loc('[data-invoice-action="'+credit.id+'"]').click();
  await loc('[data-cancel-reason]').fill('سبب اختبار نهائي');
  const creditCancellation = page.waitForResponse(response => response.url().endsWith('/api/sales/'+credit.id+'/cancel'));
  await loc('[data-confirm-invoice-action]').click();
  const accepted = await creditCancellation;
  assert.equal(accepted.ok(),true,'Credit cancellation HTTP '+accepted.status()+': '+await accepted.text());
  await page.getByText('ملغاة',{exact:true}).waitFor();
  assert.equal((await api('sales/'+credit.id)).status,'canceled');
  console.log('PASS live report-to-invoice, exact/empty history lookup, PDF, saved receipt PNG, cancellation validation, Escape, cash refusal and persisted credit cancellation');
}

async function run() {
  const browser = await chromium.launch({channel:'msedge'});
  try {
    const page = await browser.newPage({reducedMotion:'reduce'});
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    let csrf;
    const read = async (response, label) => {
      assert.ok(response, label+' returned no response');
      const body = await response.json();
      assert.equal(body.success, true, label+': '+JSON.stringify(body.error || {}));
      return body.data;
    };
    const api = async (route, method = 'GET', data) => read(await page.request.fetch(origin+'/api/'+route, {
      method, data, headers:csrf ? {'X-CSRF-Token':csrf} : {}
    }), route);
    const auth = await api('auth/login', 'POST', {
      username:'stage6qa', password:fs.readFileSync(path.join(fixture, 'qa-credential.txt'), 'utf8').trim()
    });
    csrf = auth.csrfToken;
    await page.addInitScript(token => sessionStorage.setItem('alkhayal_csrf', token), csrf);
    if (process.argv.includes('--sales')) {
      await verifySales(page, api);
      assert.deepEqual(errors,[]);
      return;
    }
    const fresh = await api('delivery/orders');
    assert.equal(fresh.total,0,'requires a fresh disposable database');
    assert.equal((await api('catalog/products')).totalCount,0,'requires a fresh disposable catalog');
    assert.equal((await api('customers')).length,0,'requires a fresh disposable customer directory');
    await page.route('**/api/printing/jobs', route => {
      if (route.request().method() !== 'POST') return route.continue();
      assert.equal(route.request().postDataJSON().automatic,true);
      return route.fulfill({json:{success:true,data:{status:'disabled'}}});
    });
    const customer = await api('customers','POST',{
      code:'QA-FINAL-DELIVERY',name:'عميل التوصيل التجريبي',phone:'01012345678',address:'عنوان العميل',status:'نشط'
    });
    const createdProduct = await api('catalog/products','POST',{
      operationKey:'final-live-product',name:'صنف توصيل تجريبي',baseUnitId:1,productType:'stock',
      salePrice:'12.34',purchasePrice:'9.00',isActive:true
    });
    const product = await api('catalog/products/'+createdProduct.id);
    const variant = product.variants.find(item => item.isDefault);
    const unit = product.units.find(item => item.isBase);
    const warehouse = (await api('inventory/warehouses')).find(item => item.isDefault);
    await api('inventory/post','POST',{
      type:'manual_receipt',operationKey:'final-live-receipt',warehouseId:warehouse.id,
      lines:[{variantId:variant.id,productUnitId:unit.id,quantity:'30',unitCost:'9.00',salePrice:'12.34'}]
    });
    const driver = await api('delivery/drivers','POST',{name:'طيار تجريبي',phone:'01112345678',isActive:true});
    await api('workforce/shifts/open','POST',{amount:'0',operationKey:'final-live-shift'});
    const loc = selector => page.locator(selector).first();
    const go = async route => {
      await loc('#globalSearch').fill(route.replaceAll('-',' '));
      await loc('[data-search-route="'+route+'"]').click();
      await loc('[data-tab-route="'+route+'"][aria-current="page"]').waitFor({state:'attached'});
    };
    const capture = name => page.screenshot({path:path.join(output,'live-'+name+'.png')});
    const statuses = ['pending','out_for_delivery','delivered','failed'];
    const viewports = [[1024,768],[1366,768],[1600,1000]];
    const corrected = [];
    for (let i=1; i<=20; i++) {
      const [width,height] = viewports[(i-1)%viewports.length];
      const theme = i%2 ? 'light' : 'dark';
      const driverId = i%2 ? driver.id : null;
      await page.setViewportSize({width,height});
      if (i===1 || i%5===0) {
        await page.goto(origin+'/');
        await loc('.home-page').waitFor();
      }
      await loc('[data-theme-choice="'+theme+'"]').click();
      await go('pos');
      await loc('[data-product="'+product.id+'"]').click();
      await loc('.sale-cart-line').waitFor();
      await loc('[data-customer-search]').fill(customer.code);
      await loc('[data-customer-option="'+customer.id+'"]').click();
      await loc('[data-delivery]').click();
      if (driverId) await loc('[data-delivery-driver]').selectOption(String(driverId));
      await loc('[data-delivery-address]').fill('عنوان أول '+i);
      await loc('[data-delivery-fee]').fill('2.50');
      await loc('[data-delivery-save]').click();
      const posting = page.waitForResponse(response => /\/api\/sales\/\d+\/post$/.test(response.url()));
      await loc('[data-post]').click();
      const sale = await read(await posting, 'posting trial '+i);
      assert.equal(sale.delivery.driverId ?? null,driverId);
      await loc('.sale-result').getByText(sale.invoiceNumber).waitFor();
      if (i===1) await capture('checkout-confirmation');
      if (i%3===0) await go('report-sales');
      await go('delivery-orders');
      await loc('.workspace-page [data-page-refresh]').click();
      await loc('[data-view-invoice="'+sale.id+'"]').click();
      await loc('[data-start-correction]').click();
      await loc('[data-confirm]').click();
      await loc('[data-delivery]').click();
      assert.equal(await loc('[data-delivery-address]').inputValue(),'عنوان أول '+i);
      await loc('[data-delivery-address]').fill('عنوان معدل '+i);
      await loc('[data-delivery-save]').click();
      const saving = page.waitForResponse(response => response.url().endsWith('/api/sales/'+sale.id+'/corrections'));
      await loc('[data-save-changes]').click();
      const revision = (await read(await saving, 'correction trial '+i)).invoice;
      assert.notEqual(revision.id,sale.id);
      assert.equal(revision.totalScaled,sale.totalScaled);
      if (i===2) await capture('confirmed-correction');
      await go('delivery-orders');
      await loc('.workspace-page [data-page-refresh]').click();
      const status = statuses[(i-1)%statuses.length];
      if (status !== 'pending') {
        const updating = page.waitForResponse(response => response.url().endsWith('/api/delivery/orders/'+revision.id+'/status'));
        await loc('[data-s="'+revision.id+'"]').selectOption(status);
        await read(await updating, 'status trial '+i);
      }
      const current = (await api('delivery/orders?search='+encodeURIComponent(revision.invoiceNumber))).items;
      assert.equal(current.length,1);
      assert.equal(current[0].salesInvoiceId,revision.id);
      assert.equal(current[0].address,'عنوان معدل '+i);
      assert.equal(revision.delivery.driverId ?? null,driverId);
      assert.equal(current[0].status,status);
      await loc('[data-view-invoice="'+revision.id+'"]').click();
      await loc('[data-invoice-identity]').waitFor();
      assert.match(await loc('.touch-cart-head').innerText(),new RegExp(revision.invoiceNumber));
      const reopened = await api('sales/'+revision.id);
      assert.equal(reopened.delivery.driverId ?? null,driverId);
      assert.equal(reopened.delivery.address,'عنوان معدل '+i);
      corrected.push(revision.id);
      console.log('PASS',i,theme,width+'x'+height,sale.invoiceNumber,sale.id+'→'+revision.id,status,driverId===null?'unassigned':'assigned');
    }
    assert.equal(new Set(corrected).size,20);
    assert.deepEqual(errors,[]);
  } finally {
    await browser.close();
  }
}

run().catch(error => {console.error(error); process.exitCode=1;});
