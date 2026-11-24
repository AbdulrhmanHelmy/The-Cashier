(function (global) {
  "use strict";
  var U = global.AlKhayalUtils;
  var labels = { product: "المنتجات", customer: "العملاء", supplier: "الموردون", sale: "فواتير البيع",
    purchase: "فواتير الشراء", returns: "المرتجعات", delivery: "التوصيل", employee: "الموظفون",
    driver: "الطيارون", maintenance: "الصيانة", installment: "عقود التقسيط",
    expense: "سندات الصرف", account: "الخزنة والبنك", payment: "سجل المدفوعات", shift: "الورديات",
    wallet: "المحافظ الإلكترونية", terminal: "ماكينات البطاقات", attendance: "الحضور والانصراف",
    movement: "حركة المخزون" };
  function provider(type, route, path, identify, context, gate, accepts) {
    return { type: type, route: route, path: path, identify: identify, context: context, gate: gate || route, accepts: accepts };
  }
  function identity(id, title, subtitle, keys, variant) {
    return { id: id, title: String(title || ""), subtitle: subtitle, keys: [title].concat(keys || []), variant: variant };
  }
  var providers = [
    provider("product", "products-list", "/api/catalog/products?directorySearch=true&page=1&pageSize=20&search=",
      function (x) { return identity(x.id, x.name, [x.primaryBarcode || x.primarySku, x.categoryName].filter(Boolean).join(" · "), [x.primaryBarcode, x.primarySku]); },
      function (x) { return { globalSearchProductId: x.id }; }),
    provider("movement", "warehouses-list", "/api/inventory/movements?page=1&pageSize=20&reference=",
      function (x) { return Number.isSafeInteger(x.warehouseId) && x.warehouseId > 0 ?
        identity(x.documentId, "INV-" + x.documentId, x.warehouseName, [x.documentId], x.warehouseId) : null; },
      function (x) { return { warehouseId: x.variant, view: "history", reference: String(x.id), globalSearchMovement: true }; },
      null, function (term) { return /^(INV-)?\d{1,8}$/i.test(term); }),
    provider("customer", "customers-list", "/api/customers/directory-search?query=",
      function (x) { return identity(x.id, x.name, x.code || x.phone, [x.code, x.phone]); },
      function (x) { return { globalSearchContactId: x.id }; }),
    provider("supplier", "suppliers-list", "/api/suppliers/directory-search?query=",
      function (x) { return identity(x.id, x.name, x.code || x.phone, [x.code, x.phone]); },
      function (x) { return { globalSearchContactId: x.id }; }),
    provider("sale", "sales-management", "/api/sales?searchLimit=20&search=",
      function (x) { return identity(x.id, x.invoiceNumber, x.customerName, [x.customerName]); },
      function (x) { return { saleInvoiceId: x.id }; }),
    provider("purchase", "purchase-details", "/api/purchases/directory-search?query=",
      function (x) { return identity(x.id, x.invoiceNumber, x.supplierName, [x.supplierName]); },
      function (x) { return { invoiceId: x.id }; }, "purchase-management"),
    provider("returns", "return-details", "/api/returns?searchLimit=20&search=",
      function (x) { return ["sales", "purchase"].indexOf(x.type) >= 0 ? identity(x.id, x.number, x.invoiceNumber, [x.invoiceNumber, x.partyName], x.type) : null; },
      function (x) { return { returnType: x.variant, returnId: x.id, returnDetailSearch: x.title }; }, "returns-overview"),
    provider("delivery", "delivery-orders", "/api/delivery/orders?page=1&pageSize=10&search=",
      function (x) { return identity(x.salesInvoiceId, x.invoiceNumber, x.customerName, [x.primaryPhone]); },
      function (x) { return { globalSearchDeliveryId: x.id, globalSearchDeliveryNumber: x.title }; }),
    provider("driver", "drivers-management", "/api/delivery/drivers?limit=20&search=",
      function (x) { return identity(x.id, x.name, x.phone, [x.phone]); },
      function (x) { return { globalSearchDriverId: x.id }; }),
    provider("maintenance", "maintenance-tracking", "/api/maintenance/tickets/directory-search?query=",
      function (x) { return identity(x.id, x.ticketNumber, x.device, [x.customerName, x.device]); },
      function (x) { return { maintenanceTicketId: x.id, globalSearchMaintenance: true }; }),
    provider("installment", "installment-payment", "/api/customers/installment-search?query=",
      function (x) { return identity(x.id, x.contractNumber, x.customerName, [x.customerName], x.customerId); },
      function (x) { return { installmentContractId: x.id, installmentCustomerId: x.variant }; }),
    provider("expense", "expense-add", "/api/expenses/vouchers/directory-search?query=",
      function (x) { return identity(x.id, x.number, x.categoryName, [x.payee, x.categoryName]); },
      function (x) { return { globalSearchExpenseId: x.id }; }),
    provider("account", "accounts-list", "/api/treasury/accounts/directory-search?query=",
      function (x) { return identity(x.id, x.name, x.code, [x.code]); },
      function (x) { return { globalSearchTreasuryId: x.id }; }),
    provider("payment", "payment-history", "/api/payment-methods/history?page=1&pageSize=20&invoiceNumber=",
      function (x) { return identity(x.invoiceId, x.invoiceNumber, x.cashierName); },
      function (x) { return { globalSearchPaymentInvoiceId: x.id, globalSearchPaymentInvoiceNumber: x.title }; },
      null, function (term) { return /[0-9]/.test(term); }),
    provider("wallet", "payment-wallets", "/api/payment-methods/instruments?type=wallet&search=", instrument, instrumentContext),
    provider("terminal", "payment-terminals", "/api/payment-methods/instruments?type=card&search=", instrument, instrumentContext),
    provider("shift", "shift-details", "/api/workforce/shifts?page=1&pageSize=10&search=",
      function (x) { return identity(x.id, "SHIFT-" + x.id, x.cashierName, [x.cashierName, x.id]); },
      function (x) { return { shiftId: x.id }; }, "shifts-list"),
    provider("attendance", "attendance-history", "/api/workforce/attendance?page=1&pageSize=10&search=",
      function (x) { return identity(x.id, "ATT-" + String(x.id).padStart(6, "0"), x.employeeName, [x.employeeName], x.employeeId); },
      function (x) { return { globalSearchAttendanceId: x.id, attendanceQuery: { employeeId: x.variant, search: x.subtitle, status: "", page: 1 } }; }),
    provider("employee", "employees-list", "/api/workforce/employees?page=1&pageSize=10&search=",
      function (x) { return identity(x.id, x.name, x.code, [x.code, x.phone]); },
      function (x) { return { globalSearchEmployeeId: x.id, employeeQuery: { search: x.subtitle, status: "", page: 1 } }; })
  ];
  function instrument(x) { return identity(x.id, x.name, x.number, [x.number]); }
  function instrumentContext(x) { return { globalSearchInstrumentId: x.id }; }
  function searchTerm(value) {
    var term = value.trim();
    if (!/^[0-9٠-٩۰-۹]+$/.test(term)) return term;
    return term.replace(/[٠-٩۰-۹]/g, function (digit) {
      var code = digit.charCodeAt(0);
      return String(code >= 1776 ? code - 1776 : code - 1632);
    });
  }
  function create(options) {
    var pending = new Map(), listeners = new Set(), revision = 0;
    function permitted(p) { return options.canNavigate(p.gate) && options.canNavigate(p.route); }
    function discover(query) {
      var term = searchTerm(query), enabled = providers.filter(function (p) {
        return permitted(p) && (!p.accepts || p.accepts(term));
      });
      var key = JSON.stringify([revision, term, enabled.map(function (p) { return p.type; })]);
      if (pending.has(key)) return pending.get(key);
      var request = Promise.all(enabled.map(function (p) {
        var value = p.type === "movement" ? term.toUpperCase() : term;
        return options.request(p.path + encodeURIComponent(value)).then(function (data) {
          if (!permitted(p)) return { rows: [] };
          var records = Array.isArray(data) ? data : (data.items || []);
          var seen = new Set();
          var found = records.map(p.identify).filter(function (row) {
            if (!row || !Number.isSafeInteger(row.id) || row.id <= 0 || !row.title) return false;
            var key = JSON.stringify([row.id, row.variant]);
            if (seen.has(key)) return false;
            seen.add(key);
            return true;
          });
          function exact(row) { return row.keys.some(function (value) { return String(value || "").toLowerCase() === term.toLowerCase(); }); }
          if (p.type !== "product") found.sort(function (a, b) { return Number(exact(b)) - Number(exact(a)); });
          return { more: found.length > 5 || Number(data.total || data.totalCount || data.invoiceCount || 0) > 5, rows: found.slice(0, 5).map(function (row) {
            return Object.assign(row, { route: p.route, type: p.type, group: labels[p.type] });
          }) };
        }).catch(function (error) {
          return { rows: [], failed: permitted(p) && !(error && (error.status === 401 || error.status === 403)) };
        });
      })).then(function (groups) {
        pending.delete(key);
        return groups.reduce(function (all, group) {
          return { rows: all.rows.concat(group.rows), failed: all.failed || group.failed, more: all.more || group.more };
        }, { rows: [], failed: false, more: false });
      });
      pending.set(key, request);
      return request;
    }
    function open(row) {
      if (row.type === "screen") return options.canNavigate(row.route) && options.openScreen(row);
      var p = providers.find(function (value) { return value.type === row.type && value.route === row.route; });
      if (!p || !permitted(p) || !Number.isSafeInteger(row.id) || row.id < 1) return false;
      options.navigate(p.route, p.context(row));
      return true;
    }
    function invalidate() {
      revision++;
      listeners.forEach(function (close) { close(); });
    }
    function bind(input, panel, config) {
      var list = panel.querySelector("[data-search-list]");
      var status = panel.querySelector("[data-search-status]");
      var generation = 0, timer, rows = [], selection = -1, query;
      function cancel() { generation++; clearTimeout(timer); }
      function close() {
        cancel();
        panel.hidden = true;
        rows = [];
        query = null;
        list.innerHTML = "";
        input.setAttribute("aria-expanded", "false");
        input.removeAttribute("aria-activedescendant");
        listeners.delete(close);
      }
      function draw(items, message, retry, preserveSelection) {
        var previous = preserveSelection ? rows[selection] : null;
        rows = items;
        selection = previous ? items.findIndex(function (row) {
          return row.type === previous.type && row.route === previous.route &&
            row.id === previous.id && row.variant === previous.variant && row.title === previous.title;
        }) : -1;
        input.removeAttribute("aria-activedescendant");
        var groups = new Map();
        items.forEach(function (row, index) {
          var entries = groups.get(row.group) || [];
          entries.push('<div id="' + list.id + '-' + index + '" role="option" aria-selected="' + (selection === index) + '" class="search-result" data-search-index="' + index +
            '" data-search-route="' + U.escape(row.route) + '"><span>' + options.icon(row) + '<bdi>' + U.escape(row.title) +
            '</bdi></span><small><bdi>' + U.escape(row.subtitle || "") + '</bdi></small></div>');
          groups.set(row.group, entries);
        });
        list.innerHTML = Array.from(groups, function (entry) {
          return '<div class="global-search-group" role="group" aria-label="' + U.escape(entry[0]) + '"><div class="global-search-heading" aria-hidden="true">' +
            U.escape(entry[0]) + '</div>' + entry[1].join("") + '</div>';
        }).join("");
        status.textContent = message;
        panel.querySelector("[data-search-retry]").hidden = !retry;
        panel.hidden = false;
        input.setAttribute("aria-expanded", "true");
        if (selection >= 0) input.setAttribute("aria-activedescendant", list.id + "-" + selection);
        listeners.add(close);
      }
      function search(force) {
        if (!config.active()) { close(); return; }
        var next = searchTerm(input.value).slice(0, 120);
        if (!force && next === query && !panel.hidden) return;
        cancel();
        query = next;
        var token = generation;
        if (!next) { draw([], "ابدأ الكتابة للبحث"); return; }
        var screens = options.screens(next, config.settings);
        draw(screens, "جاري البحث…");
        timer = setTimeout(function () {
          discover(next).then(function (data) {
            if (token !== generation || !config.active()) return;
            var results = screens.filter(function (row) { return options.canNavigate(row.route); }).concat(data.rows);
            var message = data.failed ? "حدث خطأ أثناء البحث" : !results.length ? "لا توجد نتائج" :
              data.more ? "نتائج إضافية متاحة — ضيّق البحث" : String(results.length) + " نتائج";
            draw(results, message, data.failed, true);
          });
        }, 300);
      }
      function choose(index) {
        var row = rows[index];
        if (!row || !config.active()) return;
        input.value = "";
        close();
        if (!open(row)) options.unavailable();
      }
      input.setAttribute("role", "combobox");
      input.setAttribute("aria-controls", list.id);
      input.setAttribute("aria-autocomplete", "list");
      input.setAttribute("aria-expanded", "false");
      input.oninput = input.onfocus = function () { search(false); };
      input.onblur = function (event) { if (!panel.contains(event.relatedTarget)) close(); };
      input.onkeydown = function (event) {
        if (event.isComposing) return;
        if (event.key === "Escape" && !panel.hidden) { event.preventDefault(); event.stopPropagation(); close(); return; }
        if (event.key === "ArrowDown" && panel.hidden) search(false);
        if (panel.hidden || !rows.length) return;
        if (event.key === "Enter") { event.preventDefault(); choose(Math.max(selection, 0)); return; }
        if (event.key !== "ArrowUp" && event.key !== "ArrowDown") return;
        event.preventDefault();
        selection = event.key === "ArrowDown" ? (selection + 1) % rows.length : (selection <= 0 ? rows.length - 1 : selection - 1);
        list.querySelectorAll("[role=option]").forEach(function (option) {
          var selected = Number(option.getAttribute("data-search-index")) === selection;
          option.setAttribute("aria-selected", String(selected));
          if (selected) { input.setAttribute("aria-activedescendant", option.id); option.scrollIntoView({ block: "nearest" }); }
        });
      };
      panel.onmousedown = function (event) { if (event.target.closest("[role=option]")) event.preventDefault(); };
      panel.onclick = function (event) {
        var row = event.target.closest("[data-search-index]");
        if (row) choose(Number(row.getAttribute("data-search-index")));
        if (event.target.closest("[data-search-retry]")) { input.focus(); search(true); }
      };
      panel.onfocusout = function (event) { if (event.relatedTarget !== input && !panel.contains(event.relatedTarget)) close(); };
      return close;
    }
    return { bind: bind, discover: discover, open: open, invalidate: invalidate };
  }
  global.AlKhayalGlobalSearch = { create: create };
}(window));
