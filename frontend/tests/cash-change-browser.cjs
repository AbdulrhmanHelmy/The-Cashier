const { chromium } = require(process.env.THECASHIER_QA_PLAYWRIGHT || 'playwright-core');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { screens } = require('../data/screens.json');

const root = path.resolve(__dirname, '..');
const origin = 'http://cash-change.test';
const product = {
  id: 1, name: 'صنف الاختبار', salePrice: '175.00', isActive: true, availableQuantityScaled: 10000000,
  variants: [{id: 11, isDefault: true, isActive: true, effectiveSalePriceExact: '175.0000'}],
  units: [{id: 21, name: 'قطعة', isActive: true, isBase: true, isSaleUnit: true}],
  capabilities: []
};
const invoice = {
  id: 42, invoiceNumber: 'QA-42', invoiceDate: '2026-11-22', warehouseId: 1,
  paymentMode: 'cash', immediateMethod: 'cash', totalScaled: 1750000, subtotalScaled: 1750000,
  discountScaled: 0, taxScaled: 0, paidScaled: 1750000, outstandingScaled: 0,
  lines: [{variantId: 11, productUnitId: 21, quantity: '1', productName: product.name,
    unitPrice: '175.00', discount: '0.00', lineTotalScaled: 1750000, physicalUnits: []}]
};

async function run() {
  const browser = await chromium.launch({channel: 'msedge'});
  try {
    for (const [theme, width, height, scale] of [
      ['light', 1366, 768, 1], ['dark', 760, 540, 1.5], ['light', 390, 600, 1], ['dark', 390, 600, 1]
    ]) {
      const page = await browser.newPage({viewport: {width, height}, deviceScaleFactor: scale});
      const errors = [];
      let posts = 0;
      let receiptJobs = 0;
      page.on('pageerror', error => errors.push(error.message));
      const responses = new Map([
        ['/api/auth/me', {id: 1, displayName: 'مدير الاختبار', role: 'administrator'}],
        ['/api/auth/me/screen-access', {role: 'administrator', screenIds: screens.map(s => s.permissionRoute || s.route)}],
        ['/api/features/effective', {modules: screens.filter(s => s.featureKey).map(s => ({key: s.featureKey, isEnabled: true})), capabilities: []}],
        ['/api/settings/general', {currencyCode: 'EGP', currencySymbol: 'ج.م', useGroupSeparators: true, showCashChangeCalculator: true}],
        ['/api/settings/company', {businessName: 'منشأة الاختبار'}],
        ['/api/inventory/warehouses', [{id: 1, name: 'المخزن', isActive: true, isDefault: true}]],
        ['/api/workforce/shifts/current', {id: 1}],
        ['/api/inventory/products', {items: [product], totalCount: 1}],
        ['/api/inventory/products/1', product],
        ['/api/sales/drafts', {...invoice, status: 'draft'}]
      ]);
      await page.route(origin + '/**', async route => {
        const {pathname} = new URL(route.request().url());
        if (pathname.startsWith('/api/')) {
          if (pathname === '/api/sales/42/post') {
            posts++;
            return route.fulfill({json: {success: true, data: {...invoice, status: 'posted'}}});
          }
          if (pathname === '/api/printing/jobs') {
            const job = route.request().postDataJSON();
            assert.equal(job.automatic, true);
            assert.equal(job.documentId, 42);
            receiptJobs++;
            return route.fulfill({json: {success: true, data: {status: 'submitted_unknown'}}});
          }
          return route.fulfill({json: {success: true, data: responses.get(pathname) || []}});
        }
        const file = path.join(root, pathname === '/' ? 'index.html' : decodeURIComponent(pathname));
        if (!file.startsWith(root + path.sep) || !fs.existsSync(file)) return route.fulfill({status: 404});
        return route.fulfill({path: file});
      });
      await page.addInitScript(() => sessionStorage.setItem('alkhayal_csrf', 'browser-fixture'));
      await page.goto(origin);
      await page.locator('.home-page').waitFor();
      await page.locator('[data-theme-choice="' + theme + '"]').click();
      await page.locator('#globalSearch').fill('pos');
      await page.locator('[data-search-route="pos"]').click();
      await page.locator('[data-product="1"]').click();
      await page.locator('[data-post]').click();

      const dialog = page.getByRole('dialog', {name: 'حساب الباقي'});
      const received = dialog.getByLabel('المدفوع');
      await dialog.waitFor();
      assert.equal(posts, 1);
      assert.equal(await page.evaluate(() => document.activeElement.id), 'cashChangeReceived');
      assert.equal(await received.inputValue(), '175.00');
      await received.fill('200.25');
      assert.match(await dialog.locator('#cashChangeAmount').innerText(), /25\.25/);
      const rect = await dialog.boundingBox();
      assert.ok(rect.x >= 0 && rect.y >= 0 && rect.x + rect.width <= width && rect.y + rect.height <= height);
      assert.equal(await dialog.evaluate(node => getComputedStyle(node).direction), 'rtl');
      const cards = dialog.locator('.cash-change-card');
      assert.deepEqual(await cards.locator(':scope > :first-child').allTextContents(), ['المدفوع', 'المطلوب', 'الباقي']);
      const boxes = await Promise.all([0, 1, 2].map(index => cards.nth(index).boundingBox()));
      if (width > 560) {
        assert.ok(boxes[0].x > boxes[1].x && boxes[1].x > boxes[2].x, 'cards follow RTL order');
        assert.equal(new Set(boxes.map(box => box.y)).size, 1, 'cards share one row');
      } else {
        assert.ok(boxes[0].y < boxes[1].y && boxes[1].y < boxes[2].y, 'narrow cards stack in reading order');
        assert.equal(new Set(boxes.map(box => box.x)).size, 1, 'stack stays aligned');
      }
      assert.equal(new Set(await cards.evaluateAll(nodes => nodes.map(node => getComputedStyle(node).backgroundColor))).size, 3);
      assert.equal(await dialog.locator('.modal-body').evaluate(body => body.scrollWidth <= body.clientWidth), true);
      await page.locator('#toastStack .toast').filter({hasText: 'إلى Windows'}).waitFor();
      assert.equal(receiptJobs, 1);
      assert.equal(await dialog.getByRole('button', {name: 'تم', exact: true}).evaluate(button => {
        const bounds = button.getBoundingClientRect();
        return document.elementFromPoint(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2) === button;
      }), true);
      if (process.env.THECASHIER_QA_SCREENSHOTS) {
        await page.screenshot({path: path.join(process.env.THECASHIER_QA_SCREENSHOTS, 'cash-change-' + theme + '-' + width + '.png')});
      }
      await received.press(theme === 'light' ? 'Enter' : 'Escape');
      await dialog.waitFor({state: 'hidden'});
      assert.equal(await page.evaluate(() => document.activeElement.hasAttribute('data-search')), true);
      assert.equal(await page.locator('[data-post]').isDisabled(), true);
      assert.deepEqual(errors, []);
      console.log('Cash change popup passed in Edge', theme, width, height, scale);
      await page.close();
    }
  } finally {
    await browser.close();
  }
}

run().catch(error => { console.error(error); process.exitCode = 1; });
