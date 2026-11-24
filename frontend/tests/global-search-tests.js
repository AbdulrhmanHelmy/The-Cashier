"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

module.exports = async function () {
  const calls = [], routes = [], requests = [], timers = new Map();
  let timer = 0, allowed = true, active = true, failures = 0;
  const window = { AlKhayalUtils: { escape: value => String(value).replace(/</g, "&lt;") } };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, "../js/services/global-search.js"), "utf8"), {
    window, setTimeout: callback => { timers.set(++timer, callback); return timer; }, clearTimeout: id => timers.delete(id)
  });
  const engine = window.AlKhayalGlobalSearch.create({
    canNavigate: route => allowed && ["products-list", "customers-list", "settings-program"].includes(route),
    request: url => { calls.push(url); return new Promise((resolve, reject) => requests.push({ url, resolve, reject })); },
    navigate: (route, state) => routes.push([route, state]), unavailable: () => failures++,
    screens: () => [], icon: () => "", openScreen: () => true
  });
  function mount(name, visible) {
    const attributes = new Map(), nodes = { "[data-search-list]": { id: name, querySelectorAll: () => [] },
      "[data-search-status]": {}, "[data-search-retry]": {} };
    const panel = { hidden: true, contains: () => false, querySelector: name => nodes[name] };
    const input = { value: "", setAttribute: (key, value) => attributes.set(key, value),
      removeAttribute: key => attributes.delete(key) };
    engine.bind(input, panel, { active: visible });
    return { input, panel, nodes, attributes, type: value => { input.value = value; input.oninput(); },
      html: () => nodes["[data-search-list]"].innerHTML, status: () => nodes["[data-search-status]"].textContent };
  }
  function flushTimers() { for (const [id, run] of timers) { timers.delete(id); run(); } }
  function finish(term, name, count = 1) {
    requests.filter(x => x.url.endsWith(encodeURIComponent(term))).forEach(x => {
      const rows = Array.from({ length: count }, (_, index) => ({ id: index + 1, name, phone: term, primaryBarcode: term }));
      x.resolve(x.url.includes("/catalog/") ? { items: rows, totalCount: count } : rows);
    });
    return new Promise(resolve => setImmediate(resolve));
  }
  const header = mount("header", () => true), settings = mount("settings", () => active);
  header.type("٠٠١٢"); settings.type("0012"); flushTimers();
  assert.equal(calls.length, 2, "both inputs share the two simultaneous authorized provider requests");
  assert.ok(calls.every(url => url.endsWith("0012")), "digit conversion retains leading zeros");
  assert.match(header.status(), /جاري البحث/);
  await finish("0012", "<منتج & عميل>", 20);
  assert.match(header.html(), /&lt;منتج/);
  assert.match(settings.html(), /&lt;منتج/);
  assert.equal((header.html().match(/role="option"/g) || []).length, 10);
  assert.match(settings.status(), /نتائج إضافية/);
  const event = key => ({ key, preventDefault() {}, stopPropagation() {} });
  settings.input.onkeydown(event("ArrowDown"));
  settings.input.onkeydown(event("Enter"));
  assert.equal(routes[0][0], "products-list");
  assert.equal(routes[0][1].globalSearchProductId, 1);
  assert.equal(header.panel.hidden, false);
  settings.type("قديم"); flushTimers();
  settings.type("حديث"); flushTimers();
  await finish("حديث", "حديث");
  await finish("قديم", "قديم");
  assert.match(settings.html(), /حديث/);
  assert.doesNotMatch(settings.html(), /قديم/);
  header.type("header late"); flushTimers();
  settings.type("settings current"); flushTimers();
  header.input.onkeydown(event("Escape"));
  await finish("settings current", "Settings match");
  await finish("header late", "Header match");
  assert.match(settings.html(), /Settings match/);
  assert.equal(header.panel.hidden, true);
  header.type("header now"); settings.type("settings late"); flushTimers();
  active = false;
  await finish("settings late", "Inactive settings");
  await finish("header now", "Active header");
  assert.doesNotMatch(settings.html(), /Inactive settings/);
  assert.match(header.html(), /Active header/);
  header.type("failure"); flushTimers();
  for (const request of requests.filter(x => x.url.endsWith("failure"))) request.reject({ status: 500 });
  await new Promise(resolve => setImmediate(resolve));
  assert.match(header.status(), /حدث خطأ/);
  assert.equal(header.nodes["[data-search-retry]"].hidden, false);
  header.type("revoked"); flushTimers();
  allowed = false;
  engine.invalidate();
  await finish("revoked", "Restricted");
  assert.equal(header.panel.hidden, true);
  assert.doesNotMatch(header.html(), /Restricted/);
  assert.equal(engine.open({ route: "customers-list", type: "customer", id: 1 }), false);
  assert.equal(failures, 0);
  const cases = [
    ["/api/catalog/products", { id: 4, name: "قهوة", primaryBarcode: "00042" }, "products-list", { globalSearchProductId: 4 }],
    ["/api/inventory/movements", { id: 2, documentId: 42, warehouseName: "مخزن", warehouseId: 3 }, "warehouses-list",
      { warehouseId: 3, view: "history", reference: "42", globalSearchMovement: true }],
    ["/api/customers/directory-search", { id: 5, name: "عميل", phone: "00042" }, "customers-list", { globalSearchContactId: 5 }],
    ["/api/suppliers/directory-search", { id: 6, name: "مورد" }, "suppliers-list", { globalSearchContactId: 6 }],
    ["/api/sales", { id: 7, invoiceNumber: "00042" }, "sales-management", { saleInvoiceId: 7 }],
    ["/api/purchases/directory-search", { id: 8, invoiceNumber: "00042" }, "purchase-details", { invoiceId: 8 }],
    ["/api/returns", { id: 7, type: "sales", number: "RET-00042" }, "return-details", { returnType: "sales", returnId: 7, returnDetailSearch: "RET-00042" }],
    ["/api/delivery/orders", { salesInvoiceId: 7, invoiceNumber: "00042" }, "delivery-orders", { globalSearchDeliveryId: 7, globalSearchDeliveryNumber: "00042" }],
    ["/api/delivery/drivers", { id: 9, name: "طيار" }, "drivers-management", { globalSearchDriverId: 9 }],
    ["/api/maintenance/tickets/directory-search", { id: 10, ticketNumber: "MNT-00042" }, "maintenance-tracking",
      { maintenanceTicketId: 10, globalSearchMaintenance: true }],
    ["/api/customers/installment-search", { id: 11, customerId: 5, contractNumber: "INS-00042" }, "installment-payment", { installmentContractId: 11, installmentCustomerId: 5 }],
    ["/api/expenses/vouchers/directory-search", { id: 12, number: "EXP-000042" }, "expense-add", { globalSearchExpenseId: 12 }],
    ["/api/treasury/accounts/directory-search", { id: 13, name: "البنك" }, "accounts-list", { globalSearchTreasuryId: 13 }],
    ["/api/payment-methods/history", { invoiceId: 7, invoiceNumber: "00042" }, "payment-history", { globalSearchPaymentInvoiceId: 7, globalSearchPaymentInvoiceNumber: "00042" }],
    ["/api/payment-methods/instruments?type=wallet", { id: 14, name: "محفظة" }, "payment-wallets", { globalSearchInstrumentId: 14 }],
    ["/api/payment-methods/instruments?type=card", { id: 15, name: "بطاقة" }, "payment-terminals", { globalSearchInstrumentId: 15 }],
    ["/api/workforce/shifts", { id: 16, cashierName: "كاشير" }, "shift-details", { shiftId: 16 }],
    ["/api/workforce/attendance", { id: 18, employeeId: 17, employeeName: "موظف" }, "attendance-history",
      { globalSearchAttendanceId: 18, attendanceQuery: { employeeId: 17, search: "موظف", status: "", page: 1 } }],
    ["/api/workforce/employees", { id: 17, name: "موظف", code: "EMP-000017" }, "employees-list",
      { globalSearchEmployeeId: 17, employeeQuery: { search: "EMP-000017", status: "", page: 1 } }]
  ];
  const all = window.AlKhayalGlobalSearch.create({
    canNavigate: () => true, navigate: (route, state) => routes.push([route, JSON.parse(JSON.stringify(state))]),
    request: async url => {
      const c = cases.find(candidate => {
        const end = candidate[0].includes("?") ? "&" : "?";
        return url.startsWith(candidate[0] + end);
      });
      assert.ok(c, url);
      return { items: [c[1], c[1]] };
    }
  });
  const results = await all.discover("00042");
  assert.equal(results.rows.length, cases.length, "typed IDs are deduplicated independently by provider");
  for (const [, , route, expected] of cases) {
    assert.ok(all.open(results.rows.find(row => row.route === route)), route);
    assert.deepEqual(routes.at(-1), [route, expected]);
  }
  console.log("Global Search: shared requests, bounds, identity, both inputs, stale results, retries and permission invalidation passed.");
};
if (require.main === module) module.exports().catch(error => { console.error(error); process.exitCode = 1; });
