
const { chromium } = require(process.env.THECASHIER_QA_PLAYWRIGHT || 'playwright-core');
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const root = path.resolve('frontend'), registry = require(path.join(root, 'data/screens.json'));
const out = path.resolve('artifacts/layout-navigation-stage2');
fs.mkdirSync(out, { recursive: true });
const allKeys = [...new Set(registry.screens.map(s=>s.featureKey).filter(Boolean))];
const allRoutes = [...new Set(registry.screens.map(s=>s.permissionRoute||s.route))];
const mime = {'.html':'text/html; charset=utf-8','.js':'application/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.svg':'image/svg+xml','.png':'image/png','.woff2':'font/woff2','.json':'application/json'};
const routeResults = {};
const failures = [];
let role = 'administrator', grants = allRoutes, features = allKeys, userId = 21, shift = null;
function response(p) {
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
 await page.route('http://cashier-stage2.test/**',async route=>{
   const url=new URL(route.request().url()), pathname=url.pathname;
   if(pathname.startsWith('/api/')) return route.fulfill({json:{success:true,data:response(pathname)}});
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
   routeResults[id] ||= 'PASS (navigation)';
 };
 const bounds=async()=>{
   const result=await page.evaluate(()=>{
     const main=document.querySelector('.app-main'), search=document.querySelector('.global-search'), header=document.querySelector('.topbar');
     const inside=e=>{const a=e.getBoundingClientRect(),b=header.getBoundingClientRect();return a.left>=b.left-1&&a.right<=b.right+1};
     const ring=[search,...header.querySelectorAll('button:not([hidden])')].filter(e=>e.getClientRects().length&&!e.closest('#quickbarMenu'));
     const rects=ring.map(e=>e.getBoundingClientRect());
     const shiftState=header.querySelector('.shift-state');
     return {document:document.documentElement.scrollWidth,window:innerWidth,main:main.scrollWidth,width:main.clientWidth,inside:ring.every(inside),shiftClipped:shiftState&&shiftState.scrollWidth>shiftState.clientWidth+1,overlap:rects.some((r,i)=>rects.slice(i+1).some(o=>r.left<o.right-1&&r.right>o.left+1&&r.top<o.bottom-1&&r.bottom>o.top+1))};
   });
   assert.ok(result.document<=result.window&&result.main<=result.width&&result.inside&&!result.overlap&&!result.shiftClipped,JSON.stringify(result));
 };
 const testThemeContrast = async () => {
   const pairs = await page.evaluate(() => {
     const luminance = color => color.match(/\d+/g).slice(0, 3).map(Number).map(channel => {
       const value = channel / 255;
       return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
     }).reduce((sum, value, index) => sum + value * [0.2126, 0.7152, 0.0722][index], 0);
     return [
       ['brand', '.sidebar-identity strong', '.sidebar-head'],
       ['sidebar', '.sidebar-section h2', '.sidebar'],
       ['highlight', '.sidebar-group-toggle.active', '.sidebar-group-toggle.active'],
       ['header', '#branchBadge', '.topbar']
     ].map(([name, ink, surface]) => {
       const text = luminance(getComputedStyle(document.querySelector(ink)).color);
       const background = luminance(getComputedStyle(document.querySelector(surface)).backgroundColor);
       return [name, (Math.max(text, background) + 0.05) / (Math.min(text, background) + 0.05)];
     });
   });
   for (const [name, ratio] of pairs) assert.ok(ratio >= 4.5, name + ' contrast ' + ratio);
 };
 const optional=async (label,fn)=>{
   try { await fn(); console.log('PASS '+label); }
   catch(error) { failures.push(label); console.log('FAIL '+label+' '+error.message.split('\n')[0]); }
 };
 await page.goto('http://cashier-stage2.test/');
 await check('.home-page');
 await click('[data-theme-choice="dark"]');
 await check('#headerShift[data-shift-state="closed"]');
 assert.equal(await loc('#branchBadge').textContent(),'الفرع الرئيسي');
 await page.waitForFunction(() => {
   const sidebar = document.querySelector('.sidebar');
   return document.documentElement.dataset.theme === 'dark' && getComputedStyle(sidebar).backgroundColor === 'rgb(29, 35, 36)';
 });
 assert.equal(Math.round((await loc('.sidebar').boundingBox()).width),272);
 await bounds(); await testThemeContrast();
 await capture('01-expanded-dark-dashboard-closed-shift');
 routeResults.dashboard='PASS (navigation)';
 await click('#sidebarToggle'); assert.equal(Math.round((await loc('.sidebar').boundingBox()).width),64);
 await capture('02-collapsed-dark');
 await page.reload(); await check('.home-page'); assert.equal(await loc('#sidebarToggle').getAttribute('aria-expanded'),'false');
 assert.equal(await loc('html').getAttribute('data-theme'),'dark');
 await loc('[data-module="sales"] [data-module-toggle]').focus();
 await page.keyboard.press('Enter'); await check('#sidebarFlyout:not([hidden])'); await capture('03-collapsed-flyout');
 await page.keyboard.press('Escape'); assert.ok(await loc('#sidebarFlyout').isHidden());
 assert.equal(await page.evaluate(()=>document.activeElement.closest('[data-module]').dataset.module),'sales');
 await click('[data-theme-choice="light"]');
 await page.waitForFunction(() => getComputedStyle(document.querySelector('.sidebar')).backgroundColor === 'rgb(255, 255, 255)');
 await capture('04-collapsed-light');
 await click('#sidebarToggle'); await testThemeContrast();
 await capture('05-expanded-light');
 console.log('PASS light and dark shell text contrast');
 const customerGroup = '[data-module="customers"] [data-module-toggle]';
 await click(customerGroup);
 await page.reload(); await check('.home-page');
 assert.equal(await loc(customerGroup).getAttribute('aria-expanded'),'true');
 await click(customerGroup);
 console.log('PASS user-scoped sidebar width and manual group persistence');
 await optional('sidebar scroll fixed footer',async()=>{
  const footer=await loc('.sidebar-footer').boundingBox();
  await loc('#sidebarNavigation').evaluate(e=>{e.scrollTop=e.scrollHeight});
  assert.ok(await loc('#sidebarNavigation').evaluate(e=>e.scrollTop>0));
  assert.deepEqual(await loc('.sidebar-footer').boundingBox(),footer);
  await capture('06-sidebar-scroll-footer');
  await loc('#sidebarNavigation').evaluate(e=>{e.scrollTop=0});
 });
 await loc('#globalSearch').fill('products list');
 await check('#globalSearchResults [role=option]'); await page.keyboard.press('ArrowDown');
 assert.ok(await loc('#globalSearch').getAttribute('aria-activedescendant'));await capture('07-keyboard-search');
 await page.keyboard.press('Enter'); await check('#workspaceTabs [data-tab-route="products-list"][aria-current]');
 routeResults['products-list']='PASS (keyboard navigation)';
 await capture('08-products');
 await click('#headerAlerts');await check('#workspaceTabs [data-tab-route="alerts-list"][aria-current]');await capture('09-notifications');
 routeResults['alerts-list']='PASS (bell navigation)';
 await click('#navigationBack'); await check('#workspaceTabs [data-tab-route="products-list"][aria-current]');
 for(const [id,name] of [['sales-management','10-sales'],['purchase-management','11-purchasing'],['customers-list','12-customers'],['warehouses-list','13-inventory'],['report-overview','14-reports'],['pos','15-pos']]) await optional(id,async()=>{await go(id);await capture(name)});
 await click('.sidebar-footer [data-route="settings-program"]'); await check('#workspaceTabs [data-tab-route="settings-program"][aria-current]');await capture('16-settings');
 routeResults['settings-program']='PASS (footer navigation)';
 await optional('flyout active submenu',async()=>{await go('sales-management');await check('.sidebar-group.open [data-route="sales-management"][aria-current]');await capture('17-active-submenu')});
 await optional('customer quick action',async()=>{await click('#quickbarTiles > [data-quick-route="customers-list"]:not([hidden])');await check('#modalLayer:not([hidden])');await capture('18-add-customer');await page.keyboard.press('Escape');});
 await optional('purchase dirty state, cancel and close',async()=>{
   await go('purchase-management');
   const field=loc('#workspace [data-field="notes"], #workspace #purchaseNotes, #workspace textarea');
   await field.fill('مسودة اختبار', {timeout:4000}); await check('.workspace-tab.active .tab-dirty:not([hidden])'); await capture('23-purchase-dirty-indicator');
   await click('#workspaceTabs [data-tab-route="sales-management"]');
   await click('#workspaceTabs [data-tab-route="purchase-management"]');
   assert.equal(await field.inputValue(),'مسودة اختبار');
   let prompt=false;page.once('dialog',async dialog=>{prompt=true;await dialog.dismiss();});
   await click('[data-close-tab="purchase-management"]');assert.ok(prompt);await check('#workspaceTabs [data-tab-route="purchase-management"]');
   page.once('dialog',dialog=>dialog.accept());await click('[data-close-tab="purchase-management"]');
   assert.equal(await page.locator('#workspaceTabs [data-tab-route="purchase-management"]').count(),0);
 });
 shift={id:8,cashierName:'مستخدم الاختبار',openedUtc:new Date(Date.now()-3600000).toISOString()};
 await page.evaluate(()=>dispatchEvent(new Event('focus')));await check('#headerShift[data-shift-state="open"]');await capture('19-open-shift');
 for(const [width,height] of [[1600,1000],[1366,768],[1024,768],[820,680],[512,384]]) {
  await page.setViewportSize({width,height});await page.waitForTimeout(100); await bounds(); console.log('PASS viewport '+width+'x'+height);
 }
 await page.setViewportSize({width:1024,height:768});await click('[data-quickbar-more]');await capture('20-header-overflow');
 await page.keyboard.press('Escape');assert.ok(await loc('#quickbarMenu').isHidden());
 await click('#workspaceSwitcher summary');await capture('21-tab-overflow');await page.keyboard.press('Escape');assert.equal(await loc('#workspaceSwitcher').getAttribute('open'),null);
 assert.equal(await page.locator('#workspaceTabs [data-tab-route="sales-management"]').count(),1);
 await click('#workspaceSwitcher summary');
 await click('#workspaceTabList [data-tab-route="dashboard"]');
 await check('#workspaceTabs [data-tab-route="dashboard"][aria-current]');
 console.log('PASS tab list selection and focus');
 await optional('shortcuts',async()=>{await loc('#workspace').focus();await page.keyboard.press('Control+k');assert.equal(await page.evaluate(()=>document.activeElement.id),'globalSearch');await loc('#workspace').focus();await page.keyboard.press('F3');await check('#workspaceTabs [data-tab-route="pos"][aria-current]');});
 await page.setViewportSize({width:1600,height:1000});
 await optional('payment landing and separate submenu',async()=>{
   const parent='[data-module="payment-methods"]';
   await click(parent+' .sidebar-landing');
   await check('#workspaceTabs [data-tab-route="payment-overview"][aria-current]');
   await click(parent+' [data-module-toggle]');
   await check(parent+' [data-route="payment-wallets"]');
   await click(parent+' [data-route="payment-wallets"]');
   await check(parent+' [data-route="payment-wallets"][aria-current]');
   routeResults['payment-overview']='PASS (sidebar landing)';
   routeResults['payment-wallets']='PASS (sidebar submenu)';
 });
 await optional('all authorized header actions',async()=>{
   for(const {route} of registry.quickAccess.filter(item=>item.route!=='alerts-list'&&item.route!=='customers-list')){
     const button='#quickbarTiles > [data-quick-route="'+route+'"]';
     if(await loc(button).isVisible()) await click(button);
     else {
       await click('[data-quickbar-more]');
       await click('#quickbarMenu [data-quick-route="'+route+'"]');
     }
     await check('#workspaceTabs [data-tab-route="'+route+'"][aria-current]');
     routeResults[route] ||= 'PASS (quick action navigation)';
     if(route==='product-quick-add'){
       await check('#catalogProductForm');
       await capture('24-quick-product-form');
     }
     if(route==='purchase-management') await check('#purchaseNotes');
   }
 });
 await page.reload(); await check('.home-page');
 for(const screen of registry.screens.filter(s=>!s.navigationHidden&&s.route!=='settings-program')){
   await optional('route '+screen.route,async()=>{
     if(screen.settingsGroup){
       await click('.sidebar-footer [data-route="settings-program"]');
       await click('[data-settings-route="'+screen.route+'"]');
       await check('#workspaceTabs [data-tab-route="'+screen.route+'"][aria-current]');
       routeResults[screen.route]='PASS (Settings hub navigation)';
     } else await go(screen.route);
     const closeButton=loc('[data-close-tab="'+screen.route+'"]');
     if(await closeButton.count())await closeButton.click();
   });
 }
 role='cashier'; grants=['dashboard','pos','product-quick-add'];features=allKeys.filter(k=>k!=='product-catalog');userId=22;
 await page.reload();await check('.home-page');assert.ok(await loc('#headerAlerts').isHidden());
 assert.equal(await page.locator('[data-quick-route="product-quick-add"]').count(),0);
 await loc('#globalSearch').fill('settings program');assert.equal(await page.locator('[data-search-route="settings-program"]').count(),0);
 await page.setViewportSize({width:1024,height:768});
 await check('#sidebarToggle[aria-expanded="false"]');
 await capture('22-restricted-cashier'); console.log('PASS permission and feature filtering');
 grants=['pos']; userId=23;
 await page.reload();
 await check('#workspaceTabs [data-tab-route="pos"][aria-current]');
 await click('#workspaceTabs [data-close-tab="pos"]');
 await loc('#workspace .empty-state').filter({hasText:'لا توجد مساحة عمل مفتوحة'}).waitFor();
 assert.equal(await page.locator('#workspaceTabs [data-tab-route]').count(),0);
 await click('#quickbarTiles [data-quick-route="pos"]');
 await check('#workspaceTabs [data-tab-route="pos"][aria-current]');
 console.log('PASS close and reopen when Home is unauthorized');
 console.log('page errors '+JSON.stringify(errors));
 assert.deepEqual(errors, [], 'uncaught browser errors');
 assert.deepEqual(failures, [], 'failed shell checks');
 } finally {fs.writeFileSync(path.join(out,'route-results.json'),JSON.stringify(routeResults,null,2));await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});

