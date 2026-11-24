const { chromium } = require(process.env.THECASHIER_QA_PLAYWRIGHT || 'playwright-core');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const registry = require('../data/screens.json');

const root = path.resolve('frontend');
const origin = 'http://cashier-remaining.test';
const batch = process.argv[2];
const output = path.resolve('artifacts/remaining-stage-batch-' + batch);
const phase = process.argv[3] || 'after';
const before = phase === 'before';
const mime = { '.html':'text/html', '.js':'application/javascript', '.css':'text/css', '.json':'application/json', '.svg':'image/svg+xml', '.woff2':'font/woff2' };
const staff = [
  {id:1,code:'EMP-001',name:'الموظف الأول',salary:'25.00',isActive:true,attendanceEnabled:true},
  {id:2,code:'EMP-002',name:'الموظف الثاني',salary:'75.00',isActive:true,attendanceEnabled:true}
];
const list = {items:[],page:1,totalPages:0,totalCount:0};
const invoice = {id:41,type:'sales',number:'QA-RETURN-41',lines:[]};
const purchaseInvoice = {...invoice,id:42,type:'purchase',number:'QA-PURCHASE-42',partyName:'مورد الاختبار'};
const postedReturns = [invoice,purchaseInvoice].map((source,index) => ({
  id:index+1,type:source.type,invoiceId:source.id,invoiceNumber:source.number,number:'QA-POSTED-RETURN-'+(index+1),
  partyName:source.partyName || 'عميل الاختبار',warehouseName:'المخزن الرئيسي',createdUtc:'2026-11-21T10:00:00Z',
  total:'12.30',cash:'0.00',accountAdjustment:'12.30',settlement:'credit',lines:[],movements:[]
}));
const backup = {configuration:{directory:'C:\\QA\\Configured'},files:[]};
const payrollRecord = {period:'2025-01',paidUtc:'2025-01-27T10:00:00Z',advances:'0.00'};
const shift = {id:17,cashierName:'الكاشير الأول',userShiftCount:1,durationMinutes:35,openedUtc:'2026-11-21T10:00:00Z',
  opening:'100.00',salesCount:0,cashSales:'0.00',creditSales:'0.00',collections:'0.00',cashIn:'0.00',cashOut:'0.00',
  expected:'100.00',expectedScaled:1000000};
const maintenanceIssue = 'عطل متكرر أثناء تشغيل الجهاز عند توصيل مصدر الطاقة بعد إعادة التشغيل في موقع العميل';
const tickets = [1,2].map(id => ({id,customerId:id,ticketNumber:'QA-MAINT-'+id,customerName:'عميل '+id,device:'جهاز الاختبار',
  model:'QA-17',serial:'TEST-'+id,issue:maintenanceIssue,expectedOn:'2026-11-22',estimatedCost:'12.50',
  advancePaid:'0.00',status:'قيد الفحص'}));
const routes = {
  a: /^(returns-overview|accounts-list|account-(deposit|withdraw)|expenses-list|expense-add|payment-.*|report-(expenses|returns|payment-methods))$/,
  b: /^(employees-list|employee-.*|shifts-list|shift-(add|details|reports)|attendance.*|installment-.*|maintenance-.*|report-(shifts|attendance|installments|maintenance))$/,
  c: /^(dashboard|alerts-list|report-.*|users-list|settings-.*|business-activities|feature-management)$/
};
assert.ok(routes[batch], 'Choose batch a, b or c.');
const baselineModule = phase === 'payment-before' ? 'payment-methods' :
  phase === 'installment-before' ? 'renderers' : phase === 'permissions-before' ? 'user-management' :
  phase === 'settings-before' ? 'general-settings' :
  before ? {a:'returns',b:'workforce',c:'backup-settings'}[batch] : null;
const baselinePath = baselineModule && '/js/modules/' + baselineModule + '.js';
const baselineSource = baselinePath && execFileSync('git',['show','HEAD:frontend'+baselinePath]);

async function run() {
  const browser = await chromium.launch({channel:'msedge'});
  try {
    const page = await browser.newPage({reducedMotion:'reduce'});
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    let release, settingsHeld, restricted = false, alertRead = false, rejectSettings = false;
    const company = {businessName:'منشأة الاختبار',displayName:'الفرع الرئيسي'};
    const general = {currencyCode:'EGP',currencySymbol:'ج.م',dateFormat:'dd/MM/yyyy',timeFormat:'HH:mm',decimalPrecision:2};
    await page.route(origin+'/**', async route => {
      const req = route.request(), url = new URL(req.url()), p = url.pathname;
      if (!p.startsWith('/api/')) {
        if (p===baselinePath) return route.fulfill({body:baselineSource,contentType:mime['.js']});
        const file = path.join(root,p==='/'?'index.html':decodeURIComponent(p));
        if (!file.startsWith(root+path.sep)||!fs.existsSync(file)) return route.fulfill({status:404});
        return route.fulfill({path:file,contentType:mime[path.extname(file)]||'application/octet-stream'});
      }
      let data = [];
      const installmentFixture = '/api/customers/1/installments/1/payments/1';
      const alertFixture = '/api/alerts/qa-alert/read';
      const settingsFixture = ['/api/settings/general','/api/settings/company'];
      if (req.method() !== 'GET' && !['/api/settings/backups/create','/api/settings/backups/inspect',installmentFixture,alertFixture,...settingsFixture].includes(p)) {
        errors.push('Unexpected fixture write: ' + req.method() + ' ' + p);
        return route.fulfill({status:405});
      }
      const role = restricted ? 'cashier' : 'administrator';
      if (p==='/api/auth/me') data = {id:1,username:'qa',displayName:'مدير الاختبار',role};
      if (p==='/api/auth/me/screen-access') data = {role,screenIds:restricted ? ['dashboard'] : registry.screens.map(s=>s.permissionRoute||s.route)};
      if (p==='/api/features/effective') data = {modules:registry.screens.filter(s=>s.featureKey).map(s=>({key:s.featureKey,isEnabled:true})),capabilities:[]};
      if (p==='/api/settings/general') data = general;
      if (p==='/api/settings/company') data = company;
      if (settingsFixture.includes(p) && req.method()==='PUT') {
        data = req.postDataJSON();
        await new Promise(resolve => {release=resolve;settingsHeld();});
        if (rejectSettings) return route.fulfill({status:400,json:{success:false,error:{message:'رفض اختبار حفظ الإعدادات'}}});
      }
      if (p==='/api/users') data = [
        {id:2,displayName:'الكاشير الأول',username:'qa-first',role:'cashier',isActive:true},
        {id:3,displayName:'الكاشير الثاني',username:'qa-second',role:'cashier',isActive:true}
      ];
      if (/^\/api\/users\/[23]\/screen-permissions$/.test(p)) {
        if (p.includes('/2/')) await new Promise(resolve => {release=resolve;});
        data = [{screenId:'dashboard',module:'الرئيسية',arabicName:'الرئيسية',isModuleEnabled:true,isGranted:true}];
      }
      if (p==='/api/alerts' && batch==='c') data = [{id:'qa-alert',type:'installment',title:'قسط اختبار مستحق',severity:'تحذير',
        relatedRoute:'installment-payment',relatedId:1,relatedParentId:1,read:alertRead}];
      if (p===alertFixture) alertRead = req.postDataJSON().read;
      if (p.endsWith('/shifts/current')) data = null;
      if (p==='/api/returns/invoices') data = {...list,totalCount:2,totalPages:1,
        items:[invoice,purchaseInvoice].map(item=>({...item,invoiceNumber:item.number,isEligible:true}))};
      if (p==='/api/returns/invoice/sales/41') {
        await new Promise(resolve => {release=resolve;});
        data = invoice;
      }
      if (p==='/api/returns/invoice/purchase/42') data = purchaseInvoice;
      if (p==='/api/returns') data = postedReturns;
      if (p==='/api/returns/sales/1') data = postedReturns[0];
      if (p==='/api/returns/purchase/2') data = postedReturns[1];
      if (p==='/api/workforce/employees') data = {...list,items:staff,totalCount:2,totalPages:1};
      if (p==='/api/workforce/shifts') data = {...list,items:[shift],totalCount:1,totalPages:1};
      if (p==='/api/workforce/shifts/17') data = shift;
      if (p==='/api/maintenance/tickets') data = tickets.filter(ticket=>!url.searchParams.has('customerId') ||
        String(ticket.customerId)===url.searchParams.get('customerId'));
      if (p==='/api/treasury/accounts') data = [{id:1,name:'خزنة الاختبار',type:'cash',balance:'50.00'}];
      if (p==='/api/expenses/categories') data = [{id:9,name:'مصروف اختباري'}];
      if (p==='/api/customers') data = [{id:1,name:'العميل الأول',status:'نشط'},{id:2,name:'العميل الثاني',status:'نشط'}];
      if (/^\/api\/customers\/[12]\/installments$/.test(p)) {
        const customerId = Number(p.split('/')[3]);
        data = [{id:customerId,customerId,contractNumber:'QA-CONTRACT-'+customerId,productName:'جهاز اختباري',
          totalAmount:'100.00',downPayment:'0.00',interestRate:'0',interestReturnPolicy:'cancel',
          schedule:[{number:1,dueDate:'2026-12-01',amount:'100.00',remainingAmount:'100.00',status:'مستحق'}]}];
      }
      if (p===installmentFixture) {
        const command = req.postDataJSON();
        assert.equal(command.amount,'22');
        assert.equal(command.treasuryAccountId,1);
        await new Promise(resolve => {release=resolve;});
        data = {id:71};
      }
      if (p==='/api/payment-methods/instruments') data = Array.from({length:45}, (_, index) => ({id:index+1,name:'محفظة '+(index+1),number:'QA-'+(index+1),isActive:true}));
      if (p==='/api/workforce/payroll' && url.searchParams.get('employeeId')==='1') {
        await new Promise(resolve => {release=resolve;});
        data = [{...payrollRecord,id:1,salary:'25.00',net:'25.00',treasuryMovementId:91}];
      }
      if (p==='/api/workforce/payroll' && url.searchParams.get('employeeId')==='2') data = [{...payrollRecord,id:2,salary:'75.00',net:'75.00',treasuryMovementId:92}];
      if (p==='/api/settings/backups') data = backup;
      if (p==='/api/settings/backups/create') data = {name:'qa.alkbackup',path:'C:\\QA\\Other\\qa.alkbackup',size:1024};
      if (p==='/api/settings/backups/inspect') data = {createdUtc:'2026-11-21T10:00:00Z',applicationVersion:'1.1.0',schemaVersion:'077',size:1024};
      if (p.startsWith('/api/reports/') || url.searchParams.has('page') && !p.endsWith('/events')) data = {...list,...data};
      return route.fulfill({json:{success:true,data}});
    });
    const loc = selector => page.locator(selector).first();
    const go = async route => {
      if (route==='alerts-list') {
        await loc('#headerAlerts').click();
        return;
      }
      if (registry.screens.find(screen => screen.route === route).settingsGroup) {
        await go('settings-program');
        await loc('[data-settings-route="'+route+'"]').click({timeout:3500});
        return;
      }
      await loc('#globalSearch').fill(route.replaceAll('-',' '));
      await loc('[data-search-route="'+route+'"]').click({timeout:3500});
    };
    const smokeFailures = [];
    for (const [width,height,theme] of (before ? [[1366,768,'light']] : [[1024,768,'light'],[1024,768,'dark'],[1366,768,'light'],[1366,768,'dark'],[1600,1000,'light'],[1600,1000,'dark']])) {
      restricted = false;
      alertRead = false;
      await page.setViewportSize({width,height});
      await page.goto(origin+'/');
      await loc('.home-page').waitFor();
      await loc('[data-theme-choice="'+theme+'"]').click();
      const capture = name => page.screenshot({path:path.join(output,[phase,width+'x'+height,theme,name].join('-')+'.png')});
      release = null;
      if (batch==='a') {
        await go('returns-overview');
        await loc('[data-add-return]').click();
        if (!before) await capture('invoice-picker');
        await loc('[data-pick-invoice]').click();
        if (before) await loc('[data-close-invoice-picker]').click();
        else {
          await page.keyboard.press('Escape');
          assert.equal(await loc('[data-add-return]').evaluate(button=>document.activeElement===button),true);
        }
        assert.ok(release);
        const response = page.waitForResponse('**/api/returns/invoice/sales/41');
        release();
        await response;
        if (before) await loc('.return-selected-summary').waitFor();
        await capture('cancelled-invoice-selection');
        assert.equal(await loc('.return-selected-summary').count(), before ? 1 : 0);
        if (phase==='after') {
          await loc('[data-open-invoice-picker]').click();
          await loc('[data-pick-invoice="42"]').click();
          await loc('.return-selected-summary').getByText(purchaseInvoice.number).waitFor();
          assert.equal(await loc('#returnType').inputValue(),'purchase');
          await capture('purchase-return-editor');
          for (const posted of postedReturns) {
            await loc('[data-return-results]').getByRole('button',{name:posted.number}).click();
            await loc('.return-detail-hero').getByRole('heading',{name:posted.number}).waitFor();
            assert.equal(await loc('[data-original-invoice]').textContent(),posted.invoiceNumber);
            assert.match(await loc('.return-financial-summary').textContent(),/12\.30/);
            await capture(posted.type+'-return-detail');
            await go('returns-overview');
          }
        }
        if (!before || phase==='payment-before') {
          await go('payment-wallets');
          const directory = loc('[data-payment-results]');
          await directory.locator('[data-next]').click();
          await directory.locator('[data-details="21"]').click();
          await loc('[data-instrument-history] .payment-empty').waitFor();
          await directory.locator('[data-next]').click();
          await directory.locator('.payment-pager').scrollIntoViewIfNeeded();
          await capture('payment-directory-pagination');
          assert.equal(await directory.locator('[data-details="41"]').count(), phase==='payment-before' ? 0 : 1);
          if (phase==='payment-before') break;
          await directory.locator('[data-edit="41"]').click();
          assert.equal(await loc('#paymentInstrumentForm [name="number"]').inputValue(),'QA-41');
          await capture('wallet-edit');
          await page.keyboard.press('Escape');
          for (const movement of ['account-deposit','account-withdraw']) {
            await go(movement);
            await loc('#treasuryAccount').selectOption('1');
            assert.equal(await loc('[data-account-balance]').textContent(),'50.00');
            await loc('#treasuryAmount').fill('0');
            await loc('[data-movement-save]').click();
            await loc('[data-treasury-error]').waitFor();
            await capture(movement+'-validation');
          }
          await go('expense-add');
          await loc('#expenseType').selectOption('9');
          await loc('#expenseAccount').selectOption('1');
          assert.equal(await loc('[data-expense-balance]').textContent(),'50.00');
          await loc('#expenseAmount').fill('0');
          await loc('[data-expense-save]').click();
          await loc('[data-expense-error]').waitFor();
          await capture('expense-validation');
        }
      } else if (batch==='b') {
        await go('employee-payroll');
        await loc('[name="employeeId"]').selectOption('1');
        await loc('[name="employeeId"]').selectOption('2');
        await page.getByText('PAY-000002').waitFor();
        assert.ok(release);
        const response = page.waitForResponse('**/api/workforce/payroll?employeeId=1');
        release();
        await response;
        await page.getByText(before ? 'PAY-000001' : 'PAY-000002').waitFor();
        await capture('payroll-selection');
        assert.equal(await page.getByText('PAY-000001').count(), before ? 1 : 0);
        if (!before) {
          await go('employees-list');
          await loc('[data-new]').click();
          await loc('[role="dialog"]').waitFor();
          await capture('employee-create-dialog');
          await page.keyboard.press('Escape');
          await go('attendance');
          await loc('[data-in]').click();
          await loc('[data-pick-search]').waitFor();
          await capture('attendance-picker');
          await page.keyboard.press('Escape');
          await go('installment-payment');
          const customer = loc('#installmentCustomerFilter');
          const payment = loc('#installmentPayAmount');
          await customer.selectOption('1');
          await page.getByRole('heading',{name:/QA-CONTRACT-1/}).waitFor();
          await payment.fill('33');
          await customer.selectOption('2');
          await page.getByRole('heading',{name:/QA-CONTRACT-2/}).waitFor();
          await capture('installment-customer-change');
          assert.equal(await payment.inputValue(),phase==='installment-before' ? '33' : '');
          if (phase==='installment-before') break;
          await customer.selectOption('1');
          await page.getByRole('heading',{name:/QA-CONTRACT-1/}).waitFor();
          release = null;
          await payment.fill('22');
          await loc('[data-pay="1:1:1"]').click();
          await customer.selectOption('2');
          await page.getByRole('heading',{name:/QA-CONTRACT-2/}).waitFor();
          await payment.fill('17');
          assert.ok(release);
          release();
          await page.getByText('تم حفظ السداد في قاعدة البيانات وتحديث رصيد الأقساط.').waitFor();
          assert.equal(await payment.inputValue(),'17');
          await go('shifts-list');
          await loc('[data-shift="17"]').click();
          await loc('[data-detail] [data-close]').click();
          await page.getByRole('heading',{name:'إغلاق الوردية #17'}).waitFor();
          assert.match(await loc('[role="dialog"]').textContent(),/100\.00/);
          await capture('shift-close-review');
          await page.keyboard.press('Escape');
          await go('maintenance-tracking');
          await loc('#maintenanceCustomerFilter').selectOption('2');
          await loc('[data-maintenance-id="2"]').getByText(maintenanceIssue).waitFor();
          assert.equal(await loc('[data-maintenance-id="1"]').count(),0);
          await capture('maintenance-customer-filter');
        }
      } else {
        await go('settings-backup');
        await loc('[data-backup-create]').click();
        await loc('[data-backup-manual] [name="folder"]').fill('C:\\QA\\Other');
        await loc('[data-backup-save]').click();
        await page.getByText(/تم إنشاء النسخة والتحقق منها/).waitFor();
        await capture('external-backup-result');
        assert.equal(await page.getByText(/لم يظهر هذا الملف في سجل مجلد النسخ المُعد/).count(), before ? 0 : 1);
        if (!before) {
          await loc('[data-backup-file]').setInputFiles({name:'fixture.alkbackup',mimeType:'application/octet-stream',buffer:Buffer.from('synthetic browser fixture')});
          await loc('[data-backup-confirm]').waitFor();
          await capture('restore-confirmation');
          await page.keyboard.press('Escape');
          await loc('[data-backup-confirm]').waitFor({state:'hidden'});
          await go('alerts-list');
          const alert = loc('[data-alert="qa-alert"]');
          await alert.getByText('جديد',{exact:true}).waitFor();
          await alert.locator('[data-toggle-read]').click();
          await alert.getByText('جديد',{exact:true}).waitFor({state:'hidden'});
          await capture('notifications-read');
          await loc('#alertType').selectOption('reorder');
          await page.getByText('لا توجد تنبيهات مطابقة').waitFor();
          await capture('notifications-filtered-empty');
          await loc('#alertType').selectOption('');
          await go('users-list');
          await loc('[data-permissions="2"]').click();
          await loc('[data-permissions="3"]').click();
          await page.getByRole('heading',{name:'صلاحيات الكاشير الثاني'}).waitFor();
          assert.ok(release);
          const response = page.waitForResponse('**/api/users/2/screen-permissions');
          release();
          await response;
          const expectedUser = phase==='permissions-before' ? 'الأول' : 'الثاني';
          await page.getByRole('heading',{name:'صلاحيات الكاشير '+expectedUser}).waitFor();
          await capture('permissions-selection');
          assert.equal(await page.getByRole('heading',{name:'صلاحيات الكاشير الأول'}).count(), phase==='permissions-before' ? 1 : 0);
          await page.keyboard.press('Escape');
          if (phase==='permissions-before') break;
          for (const formName of ['company','general']) {
            await go('settings-'+formName);
            const form = loc('[data-'+formName+'-form]');
            const field = form.locator(formName==='company'?'#businessName':'#currencySymbol');
            const cancel = form.locator('[data-cancel-'+formName+']');
            const value = formName==='company'?'منشأة اختبار معدلة':'جنيه';
            await field.fill(value);
            const response = page.waitForResponse(response=>response.url().endsWith('/api/settings/'+formName) && response.request().method()==='PUT');
            const held = new Promise(resolve=>{settingsHeld=resolve;});
            await form.locator('[type="submit"]').click();
            await held;
            await capture(formName+'-settings-saving');
            assert.equal(await cancel.isDisabled(),phase!=='settings-before');
            assert.equal(await field.isDisabled(),phase!=='settings-before');
            if (phase!=='settings-before') assert.equal(await form.locator('[type="submit"]').textContent(),'جارٍ الحفظ…');
            assert.ok(release);
            release();
            await response;
            await page.getByText(formName==='company'?'تم حفظ بيانات المنشأة بنجاح.':'تم حفظ الإعدادات وتطبيق تنسيقات العرض.').waitFor();
            assert.equal(await field.inputValue(),value);
          }
          if (phase==='settings-before') break;
          const generalForm = loc('[data-general-form]');
          await generalForm.locator('#currencySymbol').fill('عملة الاختبار');
          rejectSettings = true;
          const held = new Promise(resolve=>{settingsHeld=resolve;});
          await generalForm.locator('[type="submit"]').click();
          await held;
          await page.waitForFunction(() => document.querySelector('[data-general-form]').getAttribute('aria-busy')==='true');
          assert.ok(release);
          release();
          await page.getByText('رفض اختبار حفظ الإعدادات').waitFor();
          rejectSettings = false;
          assert.equal(await generalForm.locator('[data-cancel-general]').isEnabled(),true);
          assert.equal(await generalForm.locator('[name="pricesIncludeTax"]').isDisabled(),true);
          assert.equal(await generalForm.locator('#currencySymbol').inputValue(),'عملة الاختبار');
          await capture('general-settings-rejected');
          await generalForm.locator('[data-cancel-general]').click();
        }
      }
      if (!before) {
        for (const screen of registry.screens.filter(s=>!s.navigationHidden && (routes[batch].test(s.route) || batch==='c' && s.settingsGroup))) {
          try {
            if (screen.route==='dashboard') await page.goto(origin+'/');
            else await go(screen.route);
            await loc('[data-tab-route="'+screen.route+'"][aria-current="page"]').waitFor({state:'attached',timeout:3500});
            assert.equal(await page.evaluate(()=>document.documentElement.dir==='rtl' && document.documentElement.scrollWidth<=innerWidth),true);
            await capture(screen.route);
          } catch (error) {
            smokeFailures.push([width,height,theme,screen.route,error.message.split('\n')[0]].join(' '));
          }
        }
        if (batch==='c') {
          restricted = true;
          await page.goto(origin+'/');
          await loc('.home-page').waitFor();
          await loc('#headerAlerts').waitFor({state:'hidden'});
          await loc('#globalSearch').fill('users list');
          assert.equal(await loc('[data-search-route="users-list"]').count(),0);
          await capture('restricted-home');
        }
      }
    }
    assert.deepEqual([...errors,...smokeFailures],[]);
    console.log('Batch',batch,phase,'passed');
  } finally {
    await browser.close();
  }
}
run().catch(error=>{console.error(error);process.exitCode=1;});
