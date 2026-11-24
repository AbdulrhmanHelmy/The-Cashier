const { chromium } = require(process.env.THECASHIER_QA_PLAYWRIGHT || 'playwright-core');
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const root = path.resolve(process.env.THECASHIER_QA_FRONTEND || 'frontend'), registry = require(path.join(root, 'data/screens.json'));
const out = path.resolve('artifacts/customers-stage4');
const phase = process.env.THECASHIER_QA_PHASE || 'after';
const origin = 'http://cashier-stage4.test';
const customers = Array.from({length: 8}, (_, index) => ({
  id: index + 1, code: 'CUS-00' + (index + 1),
  name: index ? 'عميل الاختبار ' + (index + 1) : 'شركة الاختبار العربية الطويلة لتوريد المعدات والخدمات التجارية',
  phone: '000001000' + index, secondaryPhone: '000002000' + index, address: 'عنوان اختباري',
  subscription: 'فضي', status: 'نشط', balance: index ? '0.00' : '125.50'
}));
const mime = {'.html':'text/html; charset=utf-8','.js':'application/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.svg':'image/svg+xml','.woff2':'font/woff2','.json':'application/json'};
fs.mkdirSync(out, {recursive:true});

(async () => {
  const browser = await chromium.launch({channel:'msedge', headless:true});
  const errors = [];
  try {
    const page = await browser.newPage({reducedMotion:'reduce'});
    page.on('pageerror', error => errors.push(error.message));
    await page.addInitScript(() => {
      window.qaToasts = [];
      new MutationObserver(records => records.forEach(record => {
        if (record.target.id === 'toastStack') {
          record.addedNodes.forEach(node => window.qaToasts.push(node.textContent));
        }
      })).observe(document, {childList:true, subtree:true});
    });
    let empty = false, missingCustomer = false, pendingAccounts, pauseAccounts = false, deleteRequests = 0, createRequests = 0;
    await page.route(origin + '/**', async route => {
      const request = route.request(), url = new URL(request.url()), p = url.pathname;
      if (!p.startsWith('/api/')) {
        const file = path.join(root, p === '/' ? 'index.html' : decodeURIComponent(p));
        return file.startsWith(root + path.sep) && fs.existsSync(file)
          ? route.fulfill({path:file, contentType:mime[path.extname(file)] || 'application/octet-stream'})
          : route.fulfill({status:404});
      }
      let data = [];
      if (request.method() !== 'GET') {
        if (request.method() === 'DELETE') deleteRequests++;
        if (request.method() === 'POST' && p === '/api/customers') createRequests++;
        return route.fulfill({status:400, json:{success:false,error:{message:'رفض اختباري'}}});
      }
      if (p === '/api/auth/me') data = {id:21, username:'qa', displayName:'مستخدم الاختبار', role:'administrator'};
      if (p === '/api/auth/me/screen-access') data = {role:'administrator',screenIds:registry.screens.map(s=>s.permissionRoute || s.route)};
      if (p === '/api/features/effective') data = {modules:registry.screens.filter(s=>s.featureKey).map(s=>({key:s.featureKey,isEnabled:true})),capabilities:[]};
      if (p === '/api/settings/general') data = {currencySymbol:'ج.م',dateFormat:'dd/MM/yyyy'};
      if (p.endsWith('/shifts/current')) data = null;
      if (p === '/api/customers') data = empty ? [] : missingCustomer ? customers.slice(1) : customers;
      const statement = /^\/api\/customers\/(\d+)\/statement$/.exec(p);
      if (statement) {
        const customer = customers[Number(statement[1])-1];
        const opening = {kind:'opening',date:'—',description:'رصيد افتتاحي',debit:'0.00',credit:'0.00',balance:customer.id === 1 ? '25.50' : '0.00'};
        const increase = {kind:'increase',date:'2026-11-18',reference:'QA-11',description:'مديونية اختبارية',debit:'120.00',credit:'0.00',balance:'145.50'};
        const collection = {kind:'collection',transactionId:12,date:'2026-11-19',reference:'QA-12',description:'تحصيل اختباري',notes:'ملاحظة اختبارية',debit:'0.00',credit:'20.00',balance:'125.50'};
        data = {customerName:customer.name,currentBalance:customer.balance,openingBalance:opening.balance,totalDebit:customer.id === 1 ? '120.00' : '0.00',
          totalCredit:customer.id === 1 ? '20.00' : '0.00',closingBalance:customer.balance,movements:customer.id === 1 ? [opening,increase,collection] : [opening]};
      }
      if (p === '/api/treasury/accounts') {
        if (pauseAccounts) await new Promise(resolve => {pendingAccounts = resolve;});
        data = [{id:1,name:'خزنة الاختبار',type:'cash'}];
      }
      if (p === '/api/reports/customers') {
        const items = url.searchParams.has('search') ? [] : [{id:1,party:customers[0].name,contact:customers[0].phone,balanceScaled:'125.5000',periodPaidScaled:'20.0000',status:'نشط'}];
        data = {items,count:items.length,summary:{count:items.length,balanceScaled:'125.5000',periodPaidScaled:'20.0000'}};
      }
      return route.fulfill({json:{success:true,data}});
    });
    const loc = s => page.locator(s).first();
    const wait = s => loc(s).waitFor({timeout:7000});
    const click = s => loc(s).click({timeout:7000});
    const capture = async (prefix,name) => {
      await page.evaluate(() => document.fonts.ready);
      await page.screenshot({path:path.join(out,prefix + name + '.png')});
    };
    const go = async route => {
      await loc('#globalSearch').fill(route.replaceAll('-',' '));
      await click('[data-search-route="'+route+'"]');
      await wait('[data-tab-route="'+route+'"][aria-current]');
    };
    await page.goto(origin + '/');
    await wait('.home-page');
    for (const [width,height,theme] of [[1024,768,'light'],[1024,768,'dark'],[1366,768,'light'],[1366,768,'dark'],[1600,1000,'light'],[1600,1000,'dark']]) {
      const prefix = [phase,width+'x'+height,theme,''].join('-');
      await page.setViewportSize({width,height});
      await click('[data-theme-choice="'+theme+'"]');
      await go('customers-list');
      await wait('[data-row-id="1"]');
      assert.match(await loc('[data-row-id="1"]').textContent(), /0000010000/);
      assert.ok((await loc('[data-row-id="1"]').textContent()).includes(customers[0].name));
      assert.equal(await page.evaluate(() => document.documentElement.dir), 'rtl');
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
      await capture(prefix,'directory');
      await click('[data-page="next"]');
      await wait('[data-row-id="8"]');
      await click('[data-page="prev"]');
      await loc('#listSearch').fill('0000010000');
      await page.waitForFunction(() => document.querySelectorAll('.customer-directory-table tbody tr').length === 1);
      await click('[data-list-add]');
      await wait('#recordForm');
      const focus = await page.evaluate(() => document.activeElement.id);
      assert.equal(focus, phase === 'after' ? 'name' : '');
      await capture(prefix,'create');
      await loc('#recordForm #secondaryPhone').fill('000099');
      await click('[data-save-record]');
      assert.equal(await page.evaluate(() => document.activeElement.id), 'name');
      assert.equal(await loc('#recordForm #secondaryPhone').inputValue(), '000099');
      await capture(prefix,'validation');
      const writesBefore = createRequests;
      await loc('#recordForm #name').fill('عميل اختبار جديد');
      await loc('#recordForm #phone').fill('000099001');
      const saveResponse = page.waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith('/api/customers'));
      await click('[data-save-record]');
      assert.equal((await saveResponse).status(),400);
      await page.waitForFunction(() => !document.querySelector('[data-save-record]').disabled);
      assert.equal(createRequests,writesBefore+1);
      assert.equal(await loc('#recordForm #phone').inputValue(),'000099001');
      const rejectionToast = page.locator('#toastStack .toast').filter({hasText:'رفض اختباري'});
      await rejectionToast.waitFor({state:'visible'});
      await capture(prefix,'save-failure');
      await page.keyboard.press('Escape');
      await loc('#recordForm').waitFor({state:'hidden'});
      assert.equal(await loc('[data-list-add]').evaluate(el=>el===document.activeElement), true);
      await rejectionToast.waitFor({state:'hidden'});
      await click('[data-row-id="1"] [data-row-action="edit"]');
      assert.equal(await loc('#recordForm #code').inputValue(), customers[0].code);
      assert.equal(await loc('#recordForm #phone').inputValue(), customers[0].phone);
      assert.equal(await page.evaluate(() => document.activeElement.id), phase === 'after' ? 'code' : '');
      await loc('#modalLayer .modal-actions [data-modal-close]').focus();
      await page.keyboard.press('Tab');
      assert.ok(await loc('#modalLayer .modal-header [data-modal-close]').evaluate(el => el === document.activeElement));
      await capture(prefix,'edit');
      await page.keyboard.press('Escape');
      await click('[data-row-id="1"] [data-row-action="delete"]');
      assert.equal((await loc('#modalLayer .modal-body').textContent()).includes(customers[0].code), phase === 'after');
      await capture(prefix,'delete-confirmation');
      await page.keyboard.press('Escape');
      assert.equal(deleteRequests,0);
      await loc('#listSearch').fill('لا توجد نتيجة');
      await wait('.customer-directory-table .empty-state');
      await capture(prefix,'no-matches');
      await loc('#listSearch').fill('');
      await wait('[data-row-id="1"]');
      await click('[data-row-id="1"] td');
      await wait('#statementBody [data-customer-receipt="12"]');
      assert.equal(await loc('#statementCustomer').inputValue(),'1');
      assert.equal(await loc('.customer-current-balance .stat-value').textContent(), await page.evaluate(() => window.AlKhayalUtils.money('125.50')));
      await capture(prefix,'statement');
      await click('[data-customer-collect]');
      await wait('#customerCollectionMethod');
      await capture(prefix,'collection');
      await page.keyboard.press('Escape');
      await click('[data-customer-increase]');
      await wait('#customerAccountAmount');
      await capture(prefix,'add-debt');
      await page.keyboard.press('Escape');
      pauseAccounts = true;
      await click('[data-customer-collect]');
      await loc('#statementCustomer').selectOption('2');
      await page.waitForFunction(() => document.querySelector('#statementBody h2')?.textContent.includes('عميل الاختبار 2'));
      assert.ok(pendingAccounts);
      const accountsResponse = page.waitForResponse(response => response.url().endsWith('/api/treasury/accounts'));
      pendingAccounts();
      await (await accountsResponse).finished();
      pauseAccounts = false;
      if (phase === 'before') {
        await wait('#customerCollectionMethod');
        await capture(prefix,'stale-collection');
        await page.keyboard.press('Escape');
      } else {
        assert.ok(await loc('#customerCollectionMethod').isHidden());
        await capture(prefix,'stale-collection');
      }
      await go('report-customers');
      await wait('[data-report-route="customer-statement"]');
      assert.match(await loc('[data-results]').textContent(), /125\.5000/);
      assert.match(await loc('[data-results]').textContent(), /20\.0000/);
      await capture(prefix,'report');
      await loc('[data-report-route="customer-statement"]').scrollIntoViewIfNeeded();
      await capture(prefix,'report-table');
      await loc('.report-filters [name="search"]').fill('لا توجد نتيجة');
      await click('.report-filters [type="submit"]');
      await wait('[data-results] .empty-state');
      await click('.report-filters [data-reset]');
      await click('[data-report-route="customer-statement"]');
      await wait('#statementBody [data-customer-receipt="12"]');
      assert.equal(await loc('#statementCustomer').inputValue(),'1');
      await go('report-customers');
      missingCustomer = true;
      await click('[data-report-route="customer-statement"]');
      await wait('#statementCustomer');
      if (phase === 'before') {
        await wait('#statementBody h2');
        assert.equal(await loc('#statementCustomer').inputValue(),'2');
        assert.ok(await loc('[data-customer-increase]').isEnabled());
      } else {
        assert.equal(await loc('#statementCustomer').inputValue(),'');
        assert.match(await loc('#statementBody').textContent(), /العميل المطلوب غير متاح/);
        assert.ok(await loc('[data-customer-increase]').isDisabled());
      }
      await capture(prefix,'missing-customer');
      if (phase === 'after') {
        await loc('#statementCustomer').selectOption('2');
        await wait('#statementBody h2');
        assert.ok(await loc('[data-customer-increase]').isEnabled());
      }
      missingCustomer = false;
      await go('customers-list');
      empty = true;
      await click('[data-page-refresh]');
      await wait('.customer-directory-table .empty-state');
      assert.equal((await loc('.customer-directory-table .empty-state').textContent()).includes('لا يوجد عملاء'), phase === 'after');
      await capture(prefix,'empty-directory');
      empty = false;
      await click('[data-page-refresh]');
      await wait('[data-row-id="1"]');
    }
    assert.deepEqual(errors,[]);
    assert.doesNotMatch(await page.evaluate(() => window.qaToasts.join('\n')), /Cannot set properties of null/);
    console.log('Stage 4 Edge browser matrix passed (' + phase + ').');
  } finally {
    await browser.close();
  }
})().catch(error => {console.error(error);process.exitCode=1;});
