const { chromium } = require(process.env.THECASHIER_QA_PLAYWRIGHT || 'playwright-core');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve('frontend');
const registry = require('../data/screens.json');
const out = path.resolve('artifacts/suppliers-stage5');
const baseline = process.env.THECASHIER_QA_STAGE5_BASELINE;
const origin = 'http://cashier-stage5.test';
const suppliers = Array.from({ length: 26 }, (_, i) => ({
  id: i + 1, code: 'QA-S' + (i + 1), phone: '00000010' + String(i).padStart(2, '0'),
  name: i ? 'مورد الاختبار ' + (i + 1) : 'شركة الاختبار العربية الطويلة لتوريد المعدات والخدمات التجارية',
  contact: 'مسؤول الاختبار', status: 'نشط', payable: '125.50'
}));
const mime = { '.html': 'text/html', '.js': 'application/javascript', '.css': 'text/css', '.json': 'application/json', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };

async function run() {
  fs.mkdirSync(out, { recursive: true });
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    const page = await browser.newPage({ reducedMotion: 'reduce' });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.exposeFunction('recordStage5Toast', message => {
      if (message.includes('innerHTML')) errors.push(message);
    });
    await page.addInitScript(() => {
      new MutationObserver(records => records.forEach(record => {
        if (record.target.id === 'toastStack') {
          record.addedNodes.forEach(node => window.recordStage5Toast(node.textContent || ''));
        }
      })).observe(document, { childList: true, subtree: true });
    });
    let empty = false, holdAccount = false, releaseAccount, deleteCount = 0, restricted = false;
    let holdTreasury = false, releaseTreasury, missingSupplier = false, allowDirectoryWrites = false;
    let finishCreate;
    const requests = [];
    await page.route(origin + '/**', async route => {
      const request = route.request();
      const url = new URL(request.url());
      const p = url.pathname;
      if (!p.startsWith('/api/')) {
        const file = path.join(root, p === '/' ? 'index.html' : decodeURIComponent(p));
        if (!file.startsWith(root + path.sep) || !fs.existsSync(file)) return route.fulfill({ status: 404 });
        const source = baseline && /\/js\/(?:modules\/(?:renderers|operational-reports)|components\/ui)\.js$/.test(p)
          ? path.join(baseline, 'stage5-' + path.basename(p, '.js') + '-before.js') : file;
        return route.fulfill({ path: source, contentType: mime[path.extname(file)] || 'application/octet-stream' });
      }
      requests.push({ path: p, method: request.method() });
      if (!['GET', 'HEAD'].includes(request.method())) {
        if (request.method() === 'DELETE') deleteCount++;
        if (allowDirectoryWrites && p === '/api/suppliers' && request.method() === 'POST') {
          await new Promise(resolve => { finishCreate = resolve; });
          const supplier = { ...request.postDataJSON(), id: 27, payable: '0' };
          suppliers.push(supplier);
          return route.fulfill({ json: { success: true, data: supplier } });
        }
        if (allowDirectoryWrites && p === '/api/suppliers/27' && request.method() === 'PUT') {
          Object.assign(suppliers.at(-1), request.postDataJSON());
          return route.fulfill({ json: { success: true, data: suppliers.at(-1) } });
        }
        if (allowDirectoryWrites && p === '/api/suppliers/27' && request.method() === 'DELETE') {
          suppliers.pop();
          return route.fulfill({ json: { success: true, data: true } });
        }
        return route.fulfill({ status: 400, json: { success: false, error: { message: 'رفض اختباري' } } });
      }
      let data = [];
      const role = restricted ? 'cashier' : 'administrator';
      if (p === '/api/auth/me') data = { id: restricted ? 22 : 21, username: 'qa', displayName: 'مستخدم الاختبار', role };
      if (p === '/api/auth/me/screen-access') data = { role, screenIds: restricted ? ['dashboard', 'suppliers-list'] : registry.screens.map(s => s.permissionRoute || s.route) };
      if (p === '/api/features/effective') data = { modules: registry.screens.filter(s => s.featureKey).map(s => ({ key: s.featureKey, isEnabled: true })), capabilities: [] };
      if (p === '/api/settings/general') data = { currencySymbol: 'ج.م', dateFormat: 'dd/MM/yyyy' };
      if (p.endsWith('/shifts/current')) data = null;
      if (p === '/api/suppliers') {
        const query = (url.searchParams.get('search') || '').trim().toLowerCase();
        const pageNumber = Number(url.searchParams.get('page'));
        const pageSize = Number(url.searchParams.get('pageSize'));
        const matches = empty ? [] : suppliers.filter(s => [s.name, s.code, s.phone].some(value => String(value || '').toLowerCase().includes(query)));
        data = { items: matches.slice((pageNumber - 1) * pageSize, pageNumber * pageSize), totalCount: matches.length, page: pageNumber, pageSize };
      }
      const account = /^\/api\/suppliers\/(\d+)(?:\/(statement|invoices))?$/.exec(p);
      if (account) {
        const [, id, resource] = account;
        if (!resource) {
          data = missingSupplier && id === '1' ? null : suppliers[Number(id) - 1];
          if (!data) return route.fulfill({ status: 404, json: { success: false, error: { message: 'المورد غير موجود' } } });
        }
        if (resource === 'statement') {
          if (id === '1' && holdAccount) await new Promise(resolve => { releaseAccount = resolve; });
          data = { totalInvoices: '145.50', totalPaid: '20.00', movements: [{ date: '2026-11-19', reference: 'QA-P1', description: 'سداد اختباري', debit: 20, credit: 0, balance: '125.50' }] };
        }
        if (resource === 'invoices') data = [{ id: 81, purchaseInvoiceId: 81, invoiceType: 'purchase', documentStatus: 'posted', invoiceNumber: 'QA-INV-' + id, invoiceDate: '2026-11-18', total: '145.50', paidAmount: '20.00', remaining: '125.50' }];
      }
      if (p === '/api/treasury/accounts') {
        if (holdTreasury) await new Promise(resolve => { releaseTreasury = resolve; });
        data = [{ id: 9, name: 'خزنة الاختبار', balance: '900.00', type: 'cash' }];
      }
      if (p === '/api/inventory/warehouses') data = [{ id: 1, name: 'مخزن الاختبار', isActive: true, isDefault: true }];
      if (p === '/api/reports/suppliers') {
        const items = url.searchParams.has('search') ? [] : [{ id: 1, party: suppliers[0].name, contact: suppliers[0].phone, balanceScaled: '125.5000', periodPaidScaled: '20.0000', status: 'نشط' }];
        data = { items, count: items.length, summary: { count: items.length, balanceScaled: '125.5000', periodPaidScaled: '20.0000' } };
      }
      return route.fulfill({ json: { success: true, data } });
    });

    const loc = selector => page.locator(selector).first();
    const wait = selector => loc(selector).waitFor({ timeout: 7000 });
    const click = selector => loc(selector).click({ timeout: 7000 });
    const go = async route => {
      await loc('#globalSearch').fill(route.replaceAll('-', ' '));
      await click('[data-search-route="' + route + '"]');
      await wait('[data-tab-route="' + route + '"][aria-current]');
    };
    for (const [width, height, theme] of [[1024,768,'light'], [1024,768,'dark'], [1366,768,'light'], [1366,768,'dark'], [1600,1000,'light'], [1600,1000,'dark']]) {
      await page.setViewportSize({ width, height });
      await page.goto(origin + '/');
      await wait('.home-page');
      await click('[data-theme-choice="' + theme + '"]');
      const capture = async name => {
        await page.evaluate(() => document.fonts.ready);
        await page.screenshot({ path: path.join(out, [baseline ? 'before' : 'after', width + 'x' + height, theme, name].join('-') + '.png') });
      };
      await go('suppliers-list');
      await wait('[data-row-id="1"]');
      assert.match(await loc('[data-row-id="1"]').textContent(), /0000001000/);
      assert.equal(await page.evaluate(() => document.documentElement.dir === 'rtl' && document.documentElement.scrollWidth <= innerWidth), true);
      await capture('directory');
      await click('[data-supplier-page="next"]');
      await wait('[data-row-id="26"]');
      await loc('#supplierSearch').fill('0000001000');
      await wait('[data-row-id="1"]');
      const row = id => '[data-row-id="' + id + '"]';
      await click('[data-supplier-add]');
      await wait('#recordForm #code');
      assert.equal(await page.evaluate(() => document.activeElement.id), baseline ? '' : 'code');
      await capture('create');
      await click('[data-save-record]');
      assert.equal(await page.evaluate(() => document.activeElement.id), 'code');
      await capture('create-validation');
      await page.keyboard.press('Escape');
      assert.equal(await loc('[data-supplier-add]').evaluate(node => node === document.activeElement), true);
      await click('[data-supplier-add]');
      await loc('#recordForm #code').fill('QA-NEW');
      await loc('#recordForm #name').fill('مورد جديد للاختبار');
      await loc('#recordForm #phone').fill('000123456');
      await click('[data-save-record]');
      await wait('#toastStack :text("رفض اختباري")');
      assert.equal(await loc('#recordForm #phone').inputValue(), '000123456');
      await capture('create-rejection');
      await page.keyboard.press('Escape');
      await loc('#toastStack :text("رفض اختباري")').waitFor({ state: 'hidden' });
      await loc('#supplierSearch').fill('');
      await wait(row(2));
      await click(row(2) + ' [data-row-action="view"]');
      assert.ok((await loc('.modal-body').textContent()).includes(suppliers[1].name));
      await page.keyboard.press('Escape');
      for (const id of [1, 2]) {
        await click(row(id) + ' [data-row-action="edit"]');
        assert.equal(await loc('#recordForm #code').inputValue(), suppliers[id - 1].code);
        assert.equal(await page.evaluate(() => document.activeElement.id), baseline ? '' : 'code');
        await capture('edit-' + id);
        await click('.modal-actions [data-modal-close]');
      }
      await click(row(1) + ' [data-row-action="delete"]');
      assert.equal((await loc('.modal-body').textContent()).includes(suppliers[0].code), !baseline);
      await capture('delete');
      await page.keyboard.press('Escape');
      assert.equal(deleteCount, 0);
      await loc('#supplierSearch').fill('لا-يوجد-اختبار');
      await wait('.empty-state');
      assert.match(await loc('.empty-state').textContent(), /لا توجد نتائج مطابقة/);
      await capture('no-matches');
      empty = true;
      await loc('#supplierSearch').fill('');
      await page.waitForResponse(r => r.url().includes('/api/suppliers?'));
      await wait('.empty-state');
      assert.match(await loc('.empty-state').textContent(), baseline ? /لا توجد نتائج مطابقة/ : /لا يوجد موردون/);
      await capture('empty');
      empty = false;

      await go('supplier-payment');
      await wait('[data-select-account-supplier="1"]');
      await capture('account-unselected');
      holdAccount = true;
      await click('[data-select-account-supplier="1"]');
      const pay = '[data-pay-supplier-account]';
      await wait(pay);
      assert.equal(await loc(pay).isDisabled(), !baseline);
      assert.equal(await loc('[data-add-account-invoice]').isDisabled(), !baseline);
      await loc(pay).scrollIntoViewIfNeeded();
      await capture('account-loading');
      await click('[data-select-account-supplier="2"]');
      await wait('[data-account-statement]');
      holdAccount = false;
      assert.ok(releaseAccount);
      releaseAccount();
      await wait('[data-supplier-account="2"]');
      assert.match(await loc('[data-account-invoices]').textContent(), /QA-INV-2/);
      await loc('[data-account-statement]').scrollIntoViewIfNeeded();
      await capture('account-selected');
      holdTreasury = true;
      await click(pay);
      await wait('#supplierPayForm');
      assert.ok((await loc('.modal-body').textContent()).includes(suppliers[1].name));
      assert.equal(await loc('[data-save-supplier-payment]').isDisabled(), true);
      await capture('payment-loading');
      await page.keyboard.press('Escape');
      await click('[data-select-account-supplier="1"]');
      await wait('[data-account-statement]');
      holdTreasury = false;
      assert.ok(releaseTreasury);
      releaseTreasury();
      assert.equal(await page.locator('#supplierPayForm').count(), 0);
      await click(pay);
      await loc('#treasuryAccountId option[value="9"]').waitFor({ state: 'attached' });
      assert.ok((await loc('.modal-body').textContent()).includes(suppliers[0].name));
      await capture('payment');
      await click('[data-save-supplier-payment]');
      await wait('#supplierPayForm [aria-invalid="true"]');
      await capture('payment-validation');
      assert.equal(requests.filter(r => r.method === 'POST' && /\/payments$/.test(r.path)).length, 0);
      await page.keyboard.press('Escape');
      await go('report-suppliers');
      await wait('.data-table tbody tr');
      assert.match(await loc('.data-table tbody tr').textContent(), /125\.5000.*20\.0000/);
      assert.match(await loc('.data-table thead').textContent(), baseline ? /التحصيلات المسجلة خلال الفترة/ : /مدفوعات الموردين خلال الفترة/);
      await capture('report');
      await loc('.report-filters [name="search"]').fill('لا يوجد');
      await click('.report-filters [type="submit"]');
      await wait('.report-view .empty-state');
      await capture('report-no-matches');
      await click('.report-filters [data-reset]');
      await wait('[data-report-route="supplier-balance"]');
      missingSupplier = true;
      await click('[data-report-route="supplier-balance"]');
      await wait('[data-select-account-supplier="2"]');
      assert.equal(await page.locator(pay).count(), baseline ? 1 : 0);
      assert.equal(await page.locator('[data-supplier-selection-error]:visible').count(), baseline ? 0 : 1);
      await capture('missing-supplier');
      missingSupplier = false;
      await go('purchase-management');
      await loc('#purchaseSupplierSearch').fill(suppliers[0].phone);
      await wait('[data-select-supplier="1"]');
      await capture('purchase-choices');
      await click('[data-select-supplier="1"]');
      assert.equal(await loc('#purchaseSupplier').inputValue(), '1');
      await capture('purchase-selected');
    }
    allowDirectoryWrites = true;
    await go('suppliers-list');
    await loc('#supplierSearch').fill('QA-NEW');
    await wait('[data-supplier-results] .empty-state');
    await click('[data-supplier-add]');
    await loc('#recordForm #code').fill('QA-NEW');
    await page.keyboard.press('Tab');
    assert.equal(await page.evaluate(() => document.activeElement.id), 'name');
    await loc('#recordForm #name').fill('مورد جديد للاختبار');
    await loc('#recordForm #phone').fill('000123456');
    const createCount = () => requests.filter(r => r.path === '/api/suppliers' && r.method === 'POST').length;
    const previousCreates = createCount();
    await loc('[data-save-record]').evaluate(button => { button.click(); button.click(); });
    assert.equal(await loc('[data-save-record]').isDisabled(), true);
    for (let attempt = 0; !finishCreate && attempt < 70; attempt++) {
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert.ok(finishCreate, 'The fixture received the Create request');
    assert.equal(createCount(), previousCreates + 1);
    finishCreate();
    await wait('[data-row-id="27"]');
    const editNew = '[data-row-id="27"] [data-row-action="edit"]';
    const originalEdit = await loc(editNew).elementHandle();
    await loc(editNew).press('Enter');
    await loc('#recordForm #name').fill('اسم مؤقت');
    // Refresh the matching rows while the editor still has focus.
    await loc('#supplierSearch').evaluate(input => input.dispatchEvent(new Event('input', { bubbles: true })));
    await page.waitForFunction(button => !button.isConnected, originalEdit);
    await wait(editNew);
    await loc('.modal-header [data-modal-close]').press('Enter');
    assert.equal(await loc(editNew).evaluate(button => button === document.activeElement), !baseline);
    await page.screenshot({ path: path.join(out, (baseline ? 'before' : 'after') + '-1600x1000-dark-edit-restored-focus.png') });
    await click(editNew);
    assert.equal(await loc('#recordForm #name').inputValue(), 'مورد جديد للاختبار');
    await loc('#recordForm #name').fill('مورد معدل للاختبار');
    await click('[data-save-record]');
    await wait('[data-row-id="27"] :text("مورد معدل للاختبار")');
    await click('[data-row-id="27"] [data-row-action="delete"]');
    await click('[data-confirm]');
    await wait('[data-supplier-results] .empty-state');
    assert.equal(deleteCount, 1);
    restricted = true;
    await page.goto(origin + '/');
    await wait('.home-page');
    await click('[data-theme-choice="dark"]');
    await go('suppliers-list');
    await wait('[data-row-id="1"]');
    await click('[data-row-id="1"] [data-row-action="statement"]');
    await wait('#toastStack :text("لا تملك صلاحية")');
    assert.equal(await page.locator('[data-tab-route="supplier-payment"][aria-current]').count(), 0);
    await page.screenshot({ path: path.join(out, (baseline ? 'before' : 'after') + '-1600x1000-dark-restricted-account.png') });
    assert.deepEqual(errors, []);
    console.log('Stage 5 Edge checks passed across six viewport and theme combinations.');
  } finally {
    await browser.close();
  }
}

run().catch(error => { console.error(error); process.exitCode = 1; });
