(function (global) {
  "use strict";
  var U = global.AlKhayalUtils, UI = global.AlKhayalUI;
  function esc(value) { return U.escape(value == null ? "" : String(value)); }
  function icon(kind) {
    var paths = {
      sales: "M3 5h18l-2 13H5L3 5ZM8 5a4 4 0 0 1 8 0M8 10h8",
      customers: "M16 20v-2a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4v2M9.5 10a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z",
      suppliers: "M3 6h11v12H3zM14 10h4l3 3v5h-7M6 18a2 2 0 1 0 0-4 2 2 0 0 0 0 4M17 18a2 2 0 1 0 0-4 2 2 0 0 0 0 4",
      purchases: "M5 7h14l-1 13H6L5 7ZM8 7a4 4 0 0 1 8 0",
      inventory: "M3 8h18v13H3zM6 8V5h12v3M7 12h10M7 16h6",
      products: "M4 5h16v14H4zM8 9h8M8 13h5",
      alerts: "M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4",
      reports: "M4 19V5M4 19h17M8 16v-5M12 16V8M16 16v-3",
      payments: "M3 5h18v14H3zM3 9h18M6 15h4",
      treasury: "M3 20h18M5 20V9h14v11M3 9l9-6 9 6M9 13h2M13 13h2M9 17h2M13 17h2",
      expenses: "M5 3h14v18H5zM8 7h8M8 11h8M8 15h5",
      returns: "M4 7h10a5 5 0 1 1-1 10H8M8 4 4 7l4 3",
      delivery: "M3 6h11v12H3zM14 10h4l3 3v5h-7M7 18h.01M18 18h.01",
      manufacturing: "M4 20V8l5 4V8l5 4V4h6v16H4z",
      maintenance: "M14 4a5 5 0 0 0-6 6l-5 5 4 4 5-5a5 5 0 0 0 6-6l-3 3-4-4 3-3Z",
      installments: "M4 6h16M4 12h16M4 18h16M8 3v18M16 3v18",
      employees: "M16 20v-2a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4v2M9.5 10a4 4 0 1 0 0-8 4 4 0 0 0 0 8M17 8h4M19 6v4",
      settings: "M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M18.4 5.6l-2.1 2.1M7.7 16.3l-2.1 2.1M12 16a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z",
      grid: "M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z"
    };
    return '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="' + (paths[kind] || paths.grid) + '"></path></svg>';
  }
  function visible(screen, context) {
    return !!(screen && !screen.navigationHidden && !screen.redirectTo && context.canNavigate && context.canNavigate(screen.route));
  }
  function screenMap(context) {
    var map = {};
    ((context.registry && context.registry.screens) || []).forEach(function (screen) { map[screen.route] = screen; });
    return map;
  }
  function routeButton(route, label, kind, context, primary) {
    var screen = screenMap(context)[route];
    if (!visible(screen, context)) return "";
    return '<button class="home-action' + (primary ? ' home-action-primary' : '') + '" type="button" data-dashboard-route="' + esc(route) + '"><span class="home-action-icon">' + icon(kind) + '</span><span>' + esc(label) + '</span></button>';
  }
  function shortcutCards(context) {
    var screens = screenMap(context), modules = (context.registry && context.registry.modules) || [];
    var definitions = {
      sales: ["المبيعات", "sales", "sales-management"], customers: ["العملاء", "customers"],
      suppliers: ["الموردون", "suppliers"], purchases: ["المشتريات", "purchases"],
      inventory: ["المخازن", "inventory", "warehouses-list"], alerts: ["التنبيهات", "alerts"],
      reports: ["التقارير", "reports"], "payment-methods": ["وسائل الدفع", "payments"],
      treasury: ["الخزنة والبنك", "treasury"], expenses: ["المصروفات", "expenses"],
      returns: ["المرتجعات", "returns"], installments: ["الأقساط", "installments"],
      employees: ["الموظفون", "employees"], maintenance: ["الصيانة", "maintenance"],
      manufacturing: ["التصنيع", "manufacturing"]
    };
    var cards = [];
    modules.forEach(function (module) {
      if (module.id === "home" || module.id === "branches") return;
      var definition = definitions[module.id];
      if (!definition) return;
      var preferred = definition[2], entries = (module.items || []).map(function (route) { return screens[route]; }).filter(function (screen) { return visible(screen, context); });
      var chosen = entries.filter(function (screen) { return screen.route === preferred; })[0] || entries[0];
      if (chosen) cards.push({ route: chosen.route, label: definition[0], kind: definition[1] });
      if (module.id === "inventory") {
        var product = screens["products-list"];
        if ((module.items || []).indexOf("products-list") >= 0 && visible(product, context) && (!chosen || chosen.route !== product.route)) cards.push({ route: product.route, label: "المنتجات", kind: "products" });
      }
      if (module.id === "sales") {
        var delivery = screens["delivery-orders"];
        if ((module.items || []).indexOf("delivery-orders") >= 0 && visible(delivery, context) && (!chosen || chosen.route !== delivery.route)) cards.push({ route: delivery.route, label: "الطلبات", kind: "delivery" });
      }
    });
    var settings = screens["settings-program"];
    if (visible(settings, context)) cards.push({ route: settings.route, label: "الإعدادات", kind: "settings" });
    return cards;
  }
  function alertCount() {
    var alerts = global.AlKhayalAlerts, count = alerts && alerts.count ? Number(alerts.count()) : 0;
    return isFinite(count) ? Math.max(0, Math.floor(count)) : 0;
  }
  function shortcutMarkup(cards, area) {
    return '<div class="home-shortcut-grid home-shortcut-grid-' + area + '">' + cards.map(function (card) {
      var count = card.route === "alerts-list" ? alertCount() : 0;
      var badge = card.route === "alerts-list" ? '<span class="home-alert-count" data-home-alert-count aria-label="' + count + ' تنبيه غير مقروء"' + (count ? '' : ' hidden') + '>' + count + '</span>' : '';
      return '<button class="home-shortcut home-shortcut-' + esc(card.kind) + (card.kind === "settings" ? ' home-shortcut-settings' : '') + '" type="button" data-dashboard-route="' + esc(card.route) + '"><span class="home-shortcut-icon">' + icon(card.kind) + badge + '</span><span>' + esc(card.label) + '</span></button>';
    }).join("") + '</div>';
  }
  function updateAlertBadge(container) {
    var badge = container.querySelector && container.querySelector("[data-home-alert-count]");
    if (!badge) return;
    var count = alertCount();
    badge.hidden = count === 0;
    badge.textContent = String(count);
    badge.setAttribute("aria-label", count + " تنبيه غير مقروء");
  }
  function hero(context) {
    return '<div class="home-identity" aria-label="هوية The Cashier"><img class="home-emblem" src="assets/the-cashier-mark.png" alt=""><span class="home-kicker">THE CASHIER</span><h1 class="home-wordmark"><img src="assets/the-cashier-wordmark.png" alt="The Cashier"></h1><h2>نظام نقاط البيع وإدارة الأعمال</h2><p>إدارة متكاملة لمبيعاتك ومخزونك وعملياتك اليومية</p></div>';
  }
  function allScreens(context) {
    UI.modal({ title: "عرض كل الشاشات", body: '<label class="field"><span>بحث عن شاشة</span><input class="input" data-dashboard-menu-search placeholder="ابحث باسم الشاشة"></label><div class="dashboard-menu-list" data-dashboard-menu-list></div>', actions: '<button class="button" data-modal-close>إغلاق</button>', onOpen: function (modal) {
      var input = modal.querySelector("[data-dashboard-menu-search]"), list = modal.querySelector("[data-dashboard-menu-list]");
      function draw() {
        var term = U.normalize(input.value || "");
        var routes = ((context.registry && context.registry.screens) || []).filter(function (screen) { return visible(screen, context) && U.normalize(screen.title + " " + screen.module).indexOf(term) >= 0; });
        list.innerHTML = routes.map(function (screen) { return '<button class="dashboard-menu-item" type="button" data-dashboard-menu-route="' + esc(screen.route) + '"><strong>' + esc(screen.title) + '</strong><small>' + esc(screen.module) + '</small></button>'; }).join("") || '<div class="empty-state">لا توجد شاشة مطابقة.</div>';
      }
      input.oninput = draw;
      list.onclick = function (event) {
        var button = event.target.closest("[data-dashboard-menu-route]");
        if (!button) return;
        var route = button.getAttribute("data-dashboard-menu-route"), screen = screenMap(context)[route];
        if (!visible(screen, context)) { draw(); return; }
        UI.closeModal(); context.navigate(route);
      };
      draw(); input.focus();
    } });
  }
  function dashboard(screen, container, context) {
    container.rerenderHome = function () { dashboard(screen, container, context); };
    if (!container._homeEventsBound) {
      container._homeEventsBound = true;
      ["alkhayal:access-changed", "alkhayal:features-changed"].forEach(function (eventName) {
        global.addEventListener(eventName, function () { if (container.rerenderHome) container.rerenderHome(); });
      });
      global.addEventListener("alkhayal:alerts-changed", function () { updateAlertBadge(container); });
    }
    var cards = shortcutCards(context), rightCount = Math.ceil(cards.length / 2);
    var actions = routeButton("pos", "ابدأ البيع", "sales", context, true) + routeButton("sales-management", "فواتير البيع", "products", context) + routeButton("purchase-management", "فاتورة شراء", "purchases", context) + '<button class="home-action" type="button" data-dashboard-all><span class="home-action-icon">' + icon("grid") + '</span><span>عرض كل الشاشات</span></button>';
    container.innerHTML = '<div class="dashboard home-page">' +
      '<section class="home-left home-shortcuts" aria-label="اختصارات الشاشات">' + shortcutMarkup(cards.slice(rightCount), "left") + '</section>' +
      '<div class="home-center">' + hero(context) + '</div>' +
      '<section class="home-right" aria-label="اختصارات العمل"><div class="home-actions">' + actions + '</div>' + shortcutMarkup(cards.slice(0, rightCount), "right") + '</section></div>';
    container.onclick = function (event) {
      var route = event.target.closest("[data-dashboard-route]");
      if (route) {
        var destination = route.getAttribute("data-dashboard-route");
        if (visible(screenMap(context)[destination], context)) context.navigate(destination);
        else container.rerenderHome();
        return;
      }
      if (event.target.closest("[data-dashboard-all]")) allScreens(context);
    };
  }
  global.AlKhayalRenderers.dashboard = dashboard;
}(window));
