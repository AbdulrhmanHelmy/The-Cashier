const { chromium } = require(process.env.THECASHIER_QA_PLAYWRIGHT || 'playwright-core');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { screens } = require('../data/screens.json');

const root = path.resolve('frontend');
const origin = 'http://cashier-final.test';
const output = path.resolve('artifacts/final-stage');
const mime = {'.html':'text/html', '.js':'application/javascript', '.css':'text/css', '.json':'application/json', '.svg':'image/svg+xml', '.woff2':'font/woff2'};
const invoice = {
  id: 41, invoiceNumber: 'QA-DELIVERY-41', status: 'posted', canEditPosted: true,
  customerId: 1, customerName: 'عميل الاختبار', warehouseId: 1, invoiceDate: '2026-11-21',
  paymentMode: 'cash', subtotalScaled: 100000, discountScaled: 0, taxScaled: 0, totalScaled: 125000,
  paidScaled: 125000, outstandingScaled: 0, lines: [],
  delivery: {driverId: 8, driverName: 'طيار سابق', primaryPhone: '0101234', address: 'عنوان الاختبار', fee: '2.50', status: 'pending'}
};

async function run() {
  const browser = await chromium.launch({channel:'msedge'});
  try {
    const page = await browser.newPage({reducedMotion:'reduce'});
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const before = process.argv.includes('--before');
    const driverBefore = process.argv.includes('--driver-before');
    let invoiceRelease, requested, driverRelease, driverRequested, driverWrites = 0;
    await page.route(origin+'/**', async route => {
      const url = new URL(route.request().url());
      const p = url.pathname;
      if (!p.startsWith('/api/')) {
        const file = path.join(root, p === '/' ? 'index.html' : decodeURIComponent(p));
        if (!file.startsWith(root+path.sep) || !fs.existsSync(file)) return route.fulfill({status:404});
        const baselineFile = before && p === '/js/modules/sales.js' ? 'sales-before.js' :
          driverBefore && p === '/js/modules/delivery.js' ? 'delivery-before.js' : null;
        return route.fulfill({path:baselineFile ? path.join(output, baselineFile) : file, contentType:mime[path.extname(file)] || 'application/octet-stream'});
      }
      if (p === '/api/delivery/drivers' && route.request().method() === 'POST') {
        driverWrites++;
        await new Promise(resolve => {driverRelease = resolve; driverRequested();});
        return route.fulfill({status:400,json:{success:false,error:{message:'تعذر حفظ طيار الاختبار'}}});
      }
      assert.equal(route.request().method(), 'GET');
      let data = [];
      if (p === '/api/auth/me') data = {id:1, username:'qa', displayName:'مدير الاختبار', role:'administrator'};
      if (p === '/api/auth/me/screen-access') data = {role:'administrator', screenIds:screens.map(screen => screen.permissionRoute || screen.route)};
      if (p === '/api/features/effective') data = {modules:screens.filter(screen => screen.featureKey).map(screen => ({key:screen.featureKey,isEnabled:true})), capabilities:[]};
      if (p === '/api/settings/general') data = {currencyCode:'EGP', decimalPrecision:2};
      if (p === '/api/settings/company') data = {businessName:'منشأة الاختبار'};
      if (p === '/api/inventory/warehouses') data = [{id:1,name:'المخزن',isActive:true,isDefault:true}];
      if (p === '/api/delivery/drivers') data = [{id:9,name:'طيار متاح',phone:'0112345',isActive:true}];
      if (p === '/api/delivery/orders') data = {page:1,pageSize:25,total:1,items:[{...invoice.delivery,salesInvoiceId:invoice.id,invoiceNumber:invoice.invoiceNumber,customerName:invoice.customerName,totalScaled:invoice.totalScaled}]};
      if (p === '/api/inventory/products') data = {items:[],totalCount:0};
      if (p === '/api/workforce/shifts/current') data = null;
      if (p === '/api/sales/41') {
        await new Promise(resolve => {invoiceRelease = resolve; requested();});
        data = invoice;
      }
      return route.fulfill({json:{success:true,data}});
    });
    const loc = selector => page.locator(selector).first();
    const go = async route => {
      if (route === 'alerts-list') return loc('#headerAlerts').click();
      if (screens.find(screen => screen.route === route).settingsGroup) {
        await go('settings-program');
        return loc('[data-settings-route="'+route+'"]').click({timeout:3500});
      }
      await loc('#globalSearch').fill(route.replaceAll('-',' '));
      await loc('[data-search-route="'+route+'"]').click({timeout:3500});
    };
    for (const [width,height] of (before || driverBefore ? [[1024,768]] : [[1024,768],[1366,768],[1600,1000]])) {
      for (const theme of (before || driverBefore ? ['light'] : ['light','dark'])) {
        await page.setViewportSize({width,height});
        await page.goto(origin+'/');
        await loc('.home-page').waitFor();
        await loc('[data-theme-choice="'+theme+'"]').click();
        if (process.argv.includes('--matrix')) {
          for (const screen of screens.filter(item => item.implemented && !item.redirectTo && !item.navigationHidden)) {
            try {
              if (screen.route === 'dashboard') {
                await page.goto(origin+'/');
                await loc('.home-page').waitFor();
              } else await go(screen.route);
              await loc('[data-tab-route="'+screen.route+'"][aria-current="page"]').waitFor({state:'attached',timeout:3500});
              assert.equal(await page.evaluate(() => document.documentElement.dir === 'rtl' && document.documentElement.scrollWidth <= innerWidth), true);
              console.log('PASS', width, height, theme, screen.route);
            } catch (error) {
              errors.push([width,height,theme,screen.route,error.message.split('\n')[0]].join(' '));
            }
          }
          continue;
        }
        await go('delivery-orders');
        const capture = name => page.screenshot({path:path.join(output, width+'x'+height+'-'+theme+'-'+name+'.png')});
        await loc('[data-view-invoice="41"]').waitFor();
        if (!before) await capture('delivery-orders');
        const started = new Promise(resolve => {requested = resolve;});
        await loc('[data-view-invoice="41"]').click();
        await started;
        await loc('[data-tab-route="delivery-orders"]').click();
        const response = page.waitForResponse('**/api/sales/41');
        invoiceRelease();
        await (await response).finished();
        // The background view must process the invoice before reactivation.
        await page.evaluate(() => new Promise(resolve => setTimeout(resolve, 100)));
        await loc('[data-tab-route="sales-management"]').click();
        if (before) {
          assert.equal(await loc('[data-invoice-identity]').count(),0);
          await capture('invoice-load-before');
          console.log('Reproduced: invoice 41 stayed unloaded after switching tabs during its request');
          continue;
        }
        await loc('[data-invoice-identity]').waitFor();
        assert.match(await loc('.touch-cart-head').innerText(), /QA-DELIVERY-41/);
        await loc('[data-start-correction]').click();
        await loc('[data-confirm]').click();
        await loc('[data-delivery]').click();
        await loc('[data-delivery-driver]').waitFor();
        assert.equal(await loc('[data-delivery-driver]').inputValue(), '8');
        assert.equal(await loc('[data-delivery-address]').inputValue(), invoice.delivery.address);
        assert.equal(await page.evaluate(() => document.documentElement.dir === 'rtl' && document.documentElement.scrollWidth <= innerWidth), true);
        await capture('delivery-editor');
        await loc('[data-delivery-save]').click();
        await page.getByText('اختر طيارًا متاحًا أو اترك الطلب بدون طيار.').waitFor();
        await page.keyboard.press('Escape');
        await loc('[data-delivery-driver]').waitFor({state:'hidden'});
        console.log(width, height, theme, 'background invoice load, invoice identity, historical driver and Escape passed');
        await go('drivers-management');
        await loc('[data-add]').click();
        await loc('[data-name]').fill('طيار الاختبار');
        await loc('[data-phone]').fill('010123456');
        const saving = new Promise(resolve => {driverRequested = resolve;});
        const previousWrites = driverWrites;
        await loc('[data-save]').click();
        await saving;
        await page.keyboard.press('Escape');
        if (driverBefore) {
          await loc('[role="dialog"]').waitFor({state:'hidden'});
          const rejected = page.waitForResponse(response => response.url().endsWith('/api/delivery/drivers') && response.status() === 400);
          driverRelease();
          await rejected;
          await capture('driver-rejected-after-close-before');
          console.log('Reproduced: Escape closed a pending driver save and hid its rejection');
          continue;
        }
        assert.equal(await loc('[data-name]').isDisabled(), true);
        assert.equal(await loc('[data-save]').isDisabled(), true);
        await capture('driver-saving');
        driverRelease();
        await page.getByText('تعذر حفظ طيار الاختبار').waitFor();
        assert.equal(await loc('[data-name]').inputValue(), 'طيار الاختبار');
        assert.equal(await loc('[data-save]').isEnabled(), true);
        assert.equal(driverWrites, previousWrites+1);
        await page.keyboard.press('Escape');
        await loc('[role="dialog"]').waitFor({state:'hidden'});
        console.log(width, height, theme, 'driver save remained open, rejected save preserved fields and Escape recovered');
      }
    }
    assert.deepEqual(errors, []);
  } finally {
    await browser.close();
  }
}

run().catch(error => {console.error(error); process.exitCode=1;});
