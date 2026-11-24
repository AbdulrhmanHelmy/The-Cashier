"use strict";
const { chromium } = require(process.env.THECASHIER_QA_PLAYWRIGHT || "playwright-core");
const fs = require("node:fs");
const path = require("node:path");
const assert = require("node:assert/strict");
const root = path.resolve("frontend");
const registry = require(path.join(root, "data/screens.json"));
const out = path.resolve("artifacts/global-search");
const sample = "اختبار";
const features = [...new Set(registry.screens.map(r => r.featureKey).filter(Boolean))];
const routes = [...new Set(registry.screens.map(r => r.permissionRoute || r.route))];
const mime = { ".html": "text/html", ".js": "application/javascript", ".css": "text/css",
  ".svg": "image/svg+xml", ".png": "image/png", ".woff2": "font/woff2", ".json": "application/json" };
function respond(url) {
  const p = url.pathname, q = url.searchParams;
  const matching = q.get("search") === sample || q.get("query") === sample;
  if (p === "/api/auth/me") return { id: 91, username: "visual-search-fixture", displayName: "مستخدم اختبار", role: "administrator" };
  if (p === "/api/auth/me/screen-access") return { role: "administrator", screenIds: routes };
  if (p === "/api/workforce/shifts/current") return null;
  if (p === "/api/features/effective") return { modules: features.map(key => ({ key, isEnabled: true })), capabilities: [] };
  if (p === "/api/settings/general") return { currencySymbol: "ج.م", dateFormat: "dd/MM/yyyy", timeFormat: "HH:mm" };
  if (p === "/api/catalog/products/42") return { id: 42, name: "قهوة عربية — اختبار طويل للعرض", categoryName: "المشروبات", isActive: true };
  if (!matching) return [];
  if (p === "/api/catalog/products") return { items: [{ id: 42, name: "قهوة عربية — اختبار طويل للعرض", primaryBarcode: "000123456789", categoryName: "المشروبات" }], totalCount: 1 };
  if (p === "/api/customers/directory-search") return [{ id: 427, name: "أحمد اختبار", code: "CUS-00427", phone: "01001230000" }];
  if (p === "/api/suppliers/directory-search") return { items: [{ id: 86, name: "مورد اختبار للمواد التجارية", code: "SUP-086" }], totalCount: 1 };
  if (p === "/api/sales") return [{ id: 173, invoiceNumber: "SAL-00173", customerName: "أحمد اختبار" }];
  if (p === "/api/purchases/directory-search") return [{ id: 25, invoiceNumber: "PUR-00025", supplierName: "مورد اختبار" }];
  if (p === "/api/delivery/orders") return { items: [{ salesInvoiceId: 173, invoiceNumber: "SAL-00173", customerName: "أحمد اختبار" }], total: 1 };
  return [];
}

(async () => {
  fs.mkdirSync(out, { recursive: true });
  const browser = await chromium.launch({ channel: "msedge", headless: true });
  const timings = [];
  try {
    for (const theme of ["light", "dark"]) for (const [width, height] of [[1024, 768], [1366, 768], [1600, 1000]]) {
      const page = await browser.newPage({ viewport: { width, height }, colorScheme: theme, reducedMotion: "reduce", hasTouch: width === 1024 });
      const errors = [];
      let searchCalls = 0;
      page.on("pageerror", error => errors.push(error.message));
      await page.route("http://cashier-global-search.test/**", async route => {
        const url = new URL(route.request().url());
        if (url.pathname.startsWith("/api/")) {
          if (url.searchParams.get("search") === sample || url.searchParams.get("query") === sample) searchCalls++;
          if (url.pathname === "/api/catalog/products" && url.searchParams.get("search") === "QA-Failure") {
            return route.fulfill({ status: 500, json: { success: false, error: { code: "qa_failure", message: "Fixture failure" } } });
          }
          return route.fulfill({ json: { success: true, data: respond(url) } });
        }
        const file = path.join(root, url.pathname === "/" ? "index.html" : decodeURIComponent(url.pathname));
        if (!file.startsWith(root + path.sep) || !fs.existsSync(file)) return route.fulfill({ status: 404 });
        return route.fulfill({ path: file, contentType: mime[path.extname(file)] || "application/octet-stream" });
      });
      await page.goto("http://cashier-global-search.test/");
      await page.locator("#workspaceTabs [aria-current]").waitFor();
      assert.equal(await page.locator("html").getAttribute("data-theme"), theme);
      await page.locator("#workspace").focus();
      await page.keyboard.press("Control+k");
      const header = page.locator("#globalSearch");
      assert.equal(await header.evaluate(el => el === document.activeElement), true);
      const settings = page.locator("[data-settings-search]");
      for (const [entry, input, list] of [
        ["header", header, page.locator("#globalSearchList")],
        ["settings", settings, page.locator("#settingsGlobalSearchList")]
      ]) {
        if (entry === "settings") await page.locator(".sidebar-footer [data-route='settings-program']").click();
        const start = Date.now(), initialCalls = searchCalls;
        await input.evaluate(el => {
          window.qaSearchRendered = new Promise(resolve => {
            el.addEventListener("input", () => {
              const begin = performance.now();
              const list = document.getElementById(el.getAttribute("aria-controls"));
              const observer = new MutationObserver(() => {
                if (!list.querySelector('[data-search-route="products-list"]')) return;
                observer.disconnect();
                resolve(Math.round(performance.now() - begin));
              });
              observer.observe(list, { childList: true });
            }, { once: true });
          });
        });
        await input.fill(sample);
        const product = list.locator('[data-search-route="products-list"]');
        await product.waitFor();
        await list.locator('[data-search-route="customers-list"]').waitFor();
        const renderMs = await page.evaluate(() => window.qaSearchRendered);
        timings.push({ entry, theme, width, height, renderMs, elapsedMs: Date.now() - start, requests: searchCalls - initialCalls });
        assert.equal(await input.getAttribute("aria-expanded"), "true");
        await input.press("ArrowDown");
        assert.ok(await input.getAttribute("aria-activedescendant"));
        const panel = await list.evaluate(el => {
          const rect = el.parentNode.getBoundingClientRect();
          return { left: rect.left, right: rect.right, bottom: rect.bottom, direction: getComputedStyle(el).direction };
        });
        assert.ok(panel.left >= 0 && panel.right <= width && panel.bottom <= height);
        assert.equal(panel.direction, "rtl");
        await page.screenshot({ path: path.join(out, `${entry}-${theme}-${width}x${height}.png`) });
        if (width === 1024) {
          const popup = list.locator("..");
          await input.fill("QA-No-Record");
          await popup.getByText("لا توجد نتائج", { exact: true }).waitFor();
          await input.fill("QA-Failure");
          await popup.getByText("حدث خطأ أثناء البحث", { exact: true }).waitFor();
          await popup.getByRole("button", { name: "إعادة المحاولة" }).click();
          await popup.getByText("حدث خطأ أثناء البحث", { exact: true }).waitFor();
          assert.equal(await input.evaluate(el => el === document.activeElement), true);
          await input.fill(sample);
          await product.waitFor();
        }
        if (entry === "settings") {
          if (width === 1024) {
            await page.evaluate(() => window.AlKhayalUI.modal({
              title: "اختبار التركيز", body: "<p>اختبار ترتيب الطبقات</p>",
              actions: '<button type="button" class="button" data-modal-close>إغلاق</button>'
            }));
            const dialog = page.getByRole("dialog", { name: "اختبار التركيز" });
            await dialog.waitFor();
            assert.equal(await input.getAttribute("aria-expanded"), "false");
            await dialog.getByRole("button", { name: "إغلاق" }).last().click();
            await input.focus();
            await product.waitFor();
            await product.tap();
          } else await product.click();
          await page.locator("#workspace h2").filter({ hasText: "قهوة عربية" }).waitFor();
          assert.equal(await page.locator('#workspaceTabs [data-tab-route="products-list"]').count(), 1);
        } else {
          await input.fill("");
          await input.press("Escape");
        }
      }
      if (theme === "dark" && width === 1600) {
        await page.locator(".sidebar-footer [data-route='settings-program']").click();
        await settings.fill("report sales");
        await page.locator('#settingsGlobalSearchList [data-search-route="report-sales"]').click();
        await page.locator('#workspaceTabs [data-tab-route="report-sales"][aria-current]').waitFor();
        await header.fill("company profile");
        await page.locator('#globalSearchList [data-search-route="settings-company"]').click();
        await page.locator('#workspaceTabs [data-tab-route="settings-company"][aria-current]').waitFor();
      }
      assert.deepEqual(errors, []);
      await page.close();
    }
    fs.writeFileSync(path.join(out, "browser-metrics.json"), JSON.stringify(timings, null, 2));
    console.log(JSON.stringify(timings));
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
