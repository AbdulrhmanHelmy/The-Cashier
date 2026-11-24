(function () {
  "use strict";
  var U = window.AlKhayalUtils, UI = window.AlKhayalUI, renderers = window.AlKhayalRenderers, Features = window.AlKhayalFeatures, Access = window.AlKhayalAccess, Settings = window.AlKhayalSettings, registry, screensByRoute = {}, tabs = [], navigationHistory = [], restoringNavigation = false, navigationReady = false, activeRoute = null, currentUser = null, renderGeneration = 0;
  var HOME_ROUTE = "dashboard", workspace = document.getElementById("workspace"), tabStrip = document.getElementById("workspaceTabs"), shell = document.getElementById("appShell"), sidebarNavigation = document.getElementById("sidebarNavigation"), quickbar = document.getElementById("quickbar");
  var manualGroup = null, revealedRoute = null, sidebarFlyout = null, quickbarResizeObserver = null, tabContextMenu = null, tabContextMenuTarget = null, tabContextMenuOutsideHandler = null, tabContextMenuKeyHandler = null;
  UI.initialize();
  function implementationRenderer(screen) { return screen.implemented === true ? renderers[screen.renderer] : renderers["under-construction"]; }
  function roleLabel(role) { return role === "administrator" ? "مسؤول النظام" : "كاشير"; }
  var iconPaths = { "payment-methods": "M3 5h18v14H3zM3 9h18M6 15h4", home: "M3 10.5 12 3l9 7.5M5 9v11h14V9M9 20v-6h6v6", customers: "M16 20v-2a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4v2M9.5 10a4 4 0 1 0 0-8 4 4 0 0 0 0 8ZM19 8v6M22 11h-6", suppliers: "M3 6h11v12H3zM14 10h4l3 3v5h-7M6 18a2 2 0 1 0 0-4 2 2 0 0 0 0 4M17 18a2 2 0 1 0 0-4 2 2 0 0 0 0 4", purchases: "M5 7h14l-1 13H6L5 7ZM8 7a4 4 0 0 1 8 0", sales: "M3 5h18l-2 13H5L3 5ZM8 5a4 4 0 0 1 8 0M8 10h8", returns: "M4 7h10a5 5 0 1 1-1 10H8M8 4 4 7l4 3", inventory: "M3 8h18v13H3zM6 8V5h12v3M7 12h10M7 16h6", treasury: "M3 20h18M5 20V9h14v11M3 9l9-6 9 6M9 13h2M13 13h2M9 17h2M13 17h2", expenses: "M5 3h14v18H5zM8 7h8M8 11h8M8 15h5M8 19h4", alerts: "M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4", employees: "M16 20v-2a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4v2M9.5 10a4 4 0 1 0 0-8 4 4 0 0 0 0 8M17 8h4M19 6v4", permissions: "M12 3 20 6v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6l8-3ZM9 12l2 2 4-4", reports: "M4 19V5M4 19h17M8 16v-5M12 16V8M16 16v-3", installments: "M4 6h16M4 12h16M4 18h16M8 3v18M16 3v18", maintenance: "M14 4a5 5 0 0 0-6 6l-5 5 4 4 5-5a5 5 0 0 0 6-6l-3 3-4-4 3-3Z", branches: "M3 21h18M5 21V7l7-4 7 4v14M9 21v-6h6v6M8 9h.01M16 9h.01", printing: "M6 9V3h12v6M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2M6 14h12v7H6z", settings: "M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M18.4 5.6l-2.1 2.1M7.7 16.3l-2.1 2.1M12 16a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z", quick: "M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z", more: "M5 12h.01M12 12h.01M19 12h.01" };
  function iconSvg(key) { return '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="' + (iconPaths[key] || iconPaths.quick) + '"></path></svg>'; }
  function quickIcon(route, more) { return iconSvg(more ? "more" : route === "alerts-list" ? "alerts" : route === "pos" ? "sales" : route.indexOf("purchase") >= 0 ? "purchases" : route.indexOf("product") >= 0 || route.indexOf("warehouse") >= 0 ? "inventory" : route.indexOf("customer") >= 0 ? "customers" : route.indexOf("supplier") >= 0 ? "suppliers" : route.indexOf("return") >= 0 ? "returns" : route.indexOf("account") >= 0 ? "treasury" : route.indexOf("financial") >= 0 ? "expenses" : route.indexOf("stock") >= 0 || route.indexOf("alert") >= 0 ? "alerts" : "quick"); }
  function loadRegistry() { return fetch("/data/screens.json", { credentials: "same-origin", cache: "no-store" }).then(function (response) { if (!response.ok) throw new Error("تعذر تحميل سجل الشاشات."); return response.json(); }).then(function (data) { registry = data; data.screens.forEach(function (screen) { screensByRoute[screen.route] = screen; }); var missing=data.screens.filter(function(screen){return typeof renderers[screen.renderer]!=="function";});if(missing.length)throw new Error("منفذ العرض غير محمل: "+missing.map(function(screen){return screen.route+" → "+screen.renderer;}).join("، "));data.screens.filter(function(screen){return screen.redirectTo;}).forEach(function(screen){if(!screensByRoute[screen.redirectTo])throw new Error("مسار التوافق غير صالح: "+screen.route);}); }); }
  function authenticate() { return AlKhayalApi.request("/api/auth/me").then(function (user) { currentUser = user; var name = user.displayName || user.username; document.getElementById("userDisplayName").textContent = name; document.getElementById("userRole").textContent = roleLabel(user.role); document.getElementById("userAvatar").textContent = name.charAt(0); var account = document.querySelector(".sidebar-account"); account.title = name + " — " + roleLabel(user.role); account.setAttribute("aria-label", account.title); return user; }).catch(function () { location.replace("/login.html"); throw new Error("unauthorized"); }); }
  function routeEnabled(screen) { if (!screen || !Features.routeEnabled(screen)) return false; if (screen.route === "report-overview") return Access.routeEnabled(screen) || !!(registry && registry.screens.some(function (item) { return item.route.indexOf("report-") === 0 && item.route !== "report-overview" && Features.routeEnabled(item) && Access.routeEnabled(item); })); return Access.routeEnabled(screen); }
  function resolveRoute(route) { var current=route,visited={};while(screensByRoute[current]&&screensByRoute[current].redirectTo&&!visited[current]){visited[current]=true;current=screensByRoute[current].redirectTo;}return current; }
  function enabledRoutes(routes) { return routes.filter(function (route) { return routeEnabled(screensByRoute[route]); }); }
  function navigationPreference(key) {
    return key + ":" + (currentUser.id || currentUser.username);
  }
  function buildSidebar() {
    if (sidebarFlyout) sidebarFlyout.close(false);
    var scroll = sidebarNavigation.scrollTop;
    var sections = [
      ["العمليات اليومية", ["home", "sales", "inventory", "purchases", "customers"]],
      ["إدارة الأعمال", ["suppliers", "payment-methods", "treasury", "expenses", "returns", "employees", "branches"]],
      ["العمليات الإضافية", ["installments", "maintenance"]],
      ["التقارير والمتابعة", ["reports", "alerts"]]
    ];
    var grouped = sections.flatMap(function (section) { return section[1]; });
    sections[2][1] = sections[2][1].concat(registry.modules.filter(function (module) {
      return grouped.indexOf(module.id) < 0;
    }).map(function (module) { return module.id; }));
    function renderModule(id) {
      var module = registry.modules.find(function (item) { return item.id === id; });
      if (!module) return "";
      var routes = enabledRoutes(module.items);
      if (!routes.length) return "";
      var label = U.escape(module.title), icon = '<span class="nav-icon">' + iconSvg(id) + '</span>';
      var caption = icon + '<span class="nav-label">' + label + '</span>';
      if (routes.length === 1) return '<button class="sidebar-group-toggle" type="button" data-route="' + U.escape(routes[0]) + '" title="' + U.escape(screensByRoute[routes[0]].title) + '" aria-label="' + U.escape(screensByRoute[routes[0]].title) + '">' + caption + '</button>';
      var landing = routes.indexOf(module.landingRoute) >= 0 ? module.landingRoute : null;
      var landingButton = landing ? '<button class="sidebar-landing" type="button" data-route="' + U.escape(landing) + '">' + caption + '</button>' : '';
      var children = routes.map(function (route) {
        var title = U.escape(screensByRoute[route].title);
        return '<button class="sidebar-link' + (route === landing ? ' sidebar-flyout-landing' : '') + '" type="button" data-route="' + U.escape(route) + '" title="' + title + '">' + title + '</button>';
      }).join("");
      return '<section class="sidebar-group' + (landing ? ' sidebar-group-with-landing' : '') + '" data-module="' + U.escape(id) + '"><div class="sidebar-group-heading">' + landingButton + '<button class="sidebar-group-toggle" type="button" data-module-toggle aria-label="' + label + '" aria-expanded="false" title="' + label + '">' + (landing ? '<span class="nav-icon rail-icon">' + iconSvg(id) + '</span>' : caption) + '<span class="nav-arrow" aria-hidden="true">‹</span></button></div><div class="sidebar-children">' + children + '</div></section>';
    }
    sidebarNavigation.innerHTML = sections.map(function (section) {
      var items = section[1].map(renderModule).join("");
      return items ? '<div class="sidebar-section"><h2>' + section[0] + '</h2>' + items + '</div>' : "";
    }).join("");
    sidebarNavigation.scrollTop = scroll;
    sidebarNavigation.onclick = function (event) {
      var toggle = event.target.closest("[data-module-toggle]"), route = event.target.closest("[data-route]");
      if (toggle) {
        if (shell.classList.contains("sidebar-collapsed")) { sidebarFlyout.open(toggle, true); return; }
        var id = toggle.closest(".sidebar-group").getAttribute("data-module");
        manualGroup = manualGroup === id ? null : id;
        localStorage.setItem(navigationPreference("alkhayal_open_nav"), manualGroup || "");
        updateNavigation();
      }
      if (route) navigateFromSidebar(route.getAttribute("data-route"));
    };
  }
  function quickbarItem(item, menu) {
    return '<button class="quick-tile' + (item.accent ? " accent" : "") + '" type="button" data-quick-route="' + U.escape(item.route) + '" title="' + U.escape(item.label) + '" aria-label="' + U.escape(item.label) + '"' + (menu ? ' role="menuitem"' : '') + '>' +
      '<span class="quick-icon">' + quickIcon(item.route, false) + '</span>' +
      '<span class="quick-label">' + U.escape(item.label) + '</span></button>';
  }
  function closeQuickbarMenu(restoreFocus) {
    var button = quickbar.querySelector("[data-quickbar-more]"), menu = quickbar.querySelector("#quickbarMenu");
    if (!button || !menu || menu.hidden) return;
    menu.hidden = true;
    button.setAttribute("aria-expanded", "false");
    if (restoreFocus) button.focus();
  }
  function quickActions() {
    return registry.quickAccess.filter(function (item) {
      return item.route !== "alerts-list" && routeEnabled(screensByRoute[item.route]);
    });
  }
  function layoutQuickbar() {
    var row = quickbar.querySelector("#quickbarTiles"), more = quickbar.querySelector("#quickbarMore"), menu = quickbar.querySelector("#quickbarMenu");
    if (!row || !row.clientWidth) return;
    var buttons = Array.from(row.querySelectorAll("[data-quick-route]"));
    var gap = parseFloat(getComputedStyle(row).gap) || 0;
    var count = window.AlKhayalQuickbarLayout.visibleCount(buttons.map(function () { return 42; }), row.clientWidth, 42, gap);
    count = Math.min(4, count);
    if (row.dataset.visibleCount === String(count)) return;
    row.dataset.visibleCount = count;
    var focused = document.activeElement;
    var focusedRoute = focused && focused.dataset.quickRoute;
    more.hidden = count === buttons.length;
    buttons.forEach(function (button, index) { button.hidden = index >= count; });
    menu.innerHTML = quickActions().slice(count).map(function (item) { return quickbarItem(item, true); }).join("");
    if (more.hidden) closeQuickbarMenu(false);
    if (focusedRoute && (!focused.isConnected || focused.hidden)) {
      var replacement = buttons.find(function (button) { return !button.hidden && button.dataset.quickRoute === focusedRoute; });
      (replacement || more.querySelector("button")).focus();
    }
    updateQuickbarActive();
  }
  function updateQuickbarActive() {
    Array.prototype.forEach.call(quickbar.querySelectorAll("[data-quick-route]"), function (button) {
      var active = button.getAttribute("data-quick-route") === activeRoute;
      button.classList.toggle("active", active);
      if (active) button.setAttribute("aria-current", "page"); else button.removeAttribute("aria-current");
    });
    var more = quickbar.querySelector("[data-quickbar-more]");
    if (more) more.classList.toggle("active", !!quickbar.querySelector("#quickbarMenu .quick-tile.active"));
  }
  function buildQuickbar() {
    localStorage.removeItem("alkhayal_quickbar_collapsed");
    var items = quickActions();
    quickbar.innerHTML =
      '<div class="quickbar-tiles" id="quickbarTiles">' +
        items.map(function (item) { return quickbarItem(item, false); }).join("") +
        '<div class="quickbar-more" id="quickbarMore" hidden><button class="quickbar-more-button" type="button" data-quickbar-more aria-expanded="false" aria-controls="quickbarMenu" aria-haspopup="menu" title="المزيد" aria-label="المزيد">' + quickIcon("", true) + '<span>المزيد</span></button><div class="quickbar-menu" id="quickbarMenu" role="menu" aria-label="المزيد من الاختصارات" hidden></div></div>' +
      '</div>' +
      '<button class="quickbar-back" id="navigationBack" type="button" aria-label="رجوع خطوة" title="رجوع خطوة" disabled>' +
        '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M5 12h14M13 6l6 6-6 6"></path></svg><span>رجوع</span>' +
      '</button>';
    layoutQuickbar();
    if (quickbarResizeObserver) quickbarResizeObserver.disconnect();
    quickbarResizeObserver = new ResizeObserver(layoutQuickbar);
    quickbarResizeObserver.observe(quickbar);
    var alertButton = document.getElementById("headerAlerts");
    alertButton.hidden = !routeEnabled(screensByRoute["alerts-list"]);
    alertButton.innerHTML = '<span class="quick-icon">' + iconSvg("alerts") + '<span class="quick-alert-count" data-alert-count hidden></span></span>';
    alertButton.onclick = function () { navigate("alerts-list"); };
    if (!alertButton.hidden) window.AlKhayalAlerts.load();
    updateAlertBadge();
    quickbar.onclick = function (event) {
      var more = event.target.closest("[data-quickbar-more]");
      if (more) {
        var menu = quickbar.querySelector("#quickbarMenu"), opening = menu.hidden;
        menu.hidden = !opening;
        more.setAttribute("aria-expanded", String(opening));
        if (opening) menu.querySelector("button").focus();
        return;
      }
      if (event.target.closest("#navigationBack")) { navigateBack(); return; }
      var tile = event.target.closest("[data-quick-route]");
      if (tile) { closeQuickbarMenu(!!tile.closest("#quickbarMenu")); runQuickAction(tile.dataset.quickRoute); }
    };
    quickbar.onkeydown = function (event) {
      var menu = quickbar.querySelector("#quickbarMenu");
      if (event.key === "Escape" && !menu.hidden) { closeQuickbarMenu(true); event.stopPropagation(); return; }
      if (menu.hidden || (event.key !== "ArrowDown" && event.key !== "ArrowUp")) return;
      var buttons = Array.prototype.slice.call(menu.querySelectorAll("button")), index = buttons.indexOf(document.activeElement);
      if (!buttons.length) return;
      event.preventDefault();
      buttons[(index + (event.key === "ArrowDown" ? 1 : buttons.length - 1)) % buttons.length].focus();
    };
  }
  function updateAlertBadge() {
    var badges=document.querySelectorAll("[data-alert-count]");if(!badges.length)return;
    var count=window.AlKhayalAlerts.count(),failed=!!window.AlKhayalAlerts.error();
    var label = failed ? "التنبيهات، تعذر التحديث" : count ? "التنبيهات، " + count + " غير مقروء" : "التنبيهات";
    document.getElementById("headerAlerts").setAttribute("aria-label", label);
    Array.prototype.forEach.call(badges,function(badge){badge.hidden=!count&&!failed;badge.textContent=failed?"!":String(count);badge.setAttribute("aria-label",failed?"تعذر تحديث التنبيهات":count+" تنبيه غير مقروء");});
  }
  window.addEventListener("alkhayal:alerts-changed",function(){
    updateAlertBadge();var tab=tabs.find(function(item){return item.route==="alerts-list";});
    if(tab && tab.view && tab.view.refreshAlerts)tab.view.refreshAlerts();
  });
  function saveActiveScroll() { var current = tabs.find(function (tab) { return tab.route === activeRoute; }); if (current) current.scrollTop = workspace.scrollTop; }
  function ensureHomeTab() { if (!screensByRoute[HOME_ROUTE] || !routeEnabled(screensByRoute[HOME_ROUTE])) return false; if (!tabs.some(function (tab) { return tab.route === HOME_ROUTE; })) tabs.unshift({ route: HOME_ROUTE, title: screensByRoute[HOME_ROUTE].title, state: {}, scrollTop: 0 }); return true; }
  function activateTab(route) { if (universalSearch) universalSearch.invalidate(); saveActiveScroll(); activeRoute = route; localStorage.setItem("alkhayal_last_route",route); AlKhayalApi.setActivePage(route); workspace.classList.toggle("touch-cashier-active",route==="pos"); renderTabs(); renderActive(); updateNavigation(); }
  function navigate(route, routeState) { route=resolveRoute(route);var screen = screensByRoute[route]; if (!screen) { UI.toast("هذه الشاشة غير مسجلة.", "danger"); return; } if (!routeEnabled(screen)) { UI.toast("لا تملك صلاحية فتح هذه الشاشة أو أن وحدتها معطلة.", "warning"); return; } var existing = tabs.find(function (tab) { return tab.route === route; }); if(existing&&routeState&&existing.view&&typeof existing.view.beforeRouteReplace==="function"&&!existing.view.beforeRouteReplace(routeState))return; if(activeRoute&&activeRoute!==route&&!restoringNavigation){navigationHistory.push(activeRoute);if(navigationHistory.length>100)navigationHistory.shift();}if (!existing) { existing = { route: route, title: screen.title, state: {}, scrollTop: 0 }; tabs.push(existing); } if(routeState){existing.state=Object.assign({},route==="pos"&&existing.state.posCart?{posCart:existing.state.posCart}:{},routeState);existing.freshnessCheckId=(existing.freshnessCheckId||0)+1;existing.view=null;existing.viewGeneration=null;existing.initialized=false;} if (activeRoute !== route || routeState || !existing.initialized) activateTab(route); }
  function navigateFromSidebar(route) { navigate(route); }
  function runQuickAction(route) {
    if (!routeEnabled(screensByRoute[route])) return;
    navigate(route);
    var item = registry.quickAccess.find(function (entry) { return entry.route === route; });
    if (item && item.action === "create-customer" && activeRoute === route) {
      var create = workspace.querySelector("[data-list-add]");
      if (create) create.click();
      else renderers.openCustomerModal(function () {
        var tab = tabs.find(function (item) { return item.route === route; });
        refreshPageData(tab);
      });
    }
  }
  function navigateFromShortcut(route, routeState) { if (routeState) { navigate(route, routeState); return; } navigateFromSidebar(route); }
  function navigateBack() { while(navigationHistory.length){var route=navigationHistory.pop();if(route===activeRoute||!screensByRoute[route]||!routeEnabled(screensByRoute[route]))continue;restoringNavigation=true;navigate(route);restoringNavigation=false;return;}updateNavigation(); }
  function closeTab(route) {
    var index = tabs.findIndex(function (tab) { return tab.route === route; });
    if (index < 0 || route === HOME_ROUTE) return;
    var closing = tabs[index];
    if (closing.view && typeof closing.view.beforeRouteReplace === "function" && !closing.view.beforeRouteReplace(null)) return;
    var wasActive = activeRoute === route;
    if (closing.view && closing.view.pageRefreshObserver) closing.view.pageRefreshObserver.disconnect();
    if (wasActive && universalSearch) universalSearch.invalidate();
    tabs.splice(index, 1);
    ensureHomeTab();
    if (wasActive) {
      activeRoute = tabs.length ? tabs[Math.max(0, index - 1)].route : null;
      if (activeRoute) localStorage.setItem("alkhayal_last_route", activeRoute);
      else localStorage.removeItem("alkhayal_last_route");
      AlKhayalApi.setActivePage(activeRoute);
      workspace.classList.toggle("touch-cashier-active", activeRoute === "pos");
    }
    renderTabs();
    if (wasActive) renderActive();
    updateNavigation();
  }
  function closeTabContextMenu(restoreFocus) { if (!tabContextMenu) return; document.removeEventListener("pointerdown", tabContextMenuOutsideHandler, true); document.removeEventListener("keydown", tabContextMenuKeyHandler, true); tabContextMenu.remove(); tabContextMenu = null; tabContextMenuOutsideHandler = null; tabContextMenuKeyHandler = null; var target = tabContextMenuTarget; tabContextMenuTarget = null; if (restoreFocus && target && target.isConnected) target.focus(); }
  function closeAllTabs() { for (var i = 0; i < tabs.length; i++) { var tab = tabs[i]; if (tab.route !== HOME_ROUTE && tab.view && typeof tab.view.beforeRouteReplace === "function" && !tab.view.beforeRouteReplace(null)) return; } if (universalSearch) universalSearch.invalidate(); saveActiveScroll(); navigationHistory = []; var homeAvailable = ensureHomeTab(), remaining = homeAvailable ? tabs.find(function (tab) { return tab.route === HOME_ROUTE; }) : null; if (!remaining) { var first = enabledRoutes(registry.screens.filter(function (item) { return !item.navigationHidden; }).map(function (item) { return item.route; }))[0]; if (first) remaining = tabs.find(function (tab) { return tab.route === first; }) || { route: first, title: screensByRoute[first].title, state: {}, scrollTop: 0 }; } tabs = remaining ? [remaining] : []; activeRoute = remaining ? remaining.route : null; if (activeRoute) { localStorage.setItem("alkhayal_last_route", activeRoute); AlKhayalApi.setActivePage(activeRoute); } workspace.classList.toggle("touch-cashier-active", activeRoute === "pos"); renderTabs(); renderActive(); updateNavigation(); }
  function showTabContextMenu(event) { var source = event.target.closest && event.target.closest("[data-tab-route]"); if (!source) return; event.preventDefault(); closeTabContextMenu(false); var menu = document.createElement("div"), hasClosableTabs = tabs.some(function (tab) { return tab.route !== HOME_ROUTE; }); menu.className = "workspace-tab-context-menu"; menu.setAttribute("role", "menu"); menu.setAttribute("aria-label", "خيارات الصفحات"); menu.innerHTML = '<button type="button" role="menuitem" data-close-all-tabs' + (hasClosableTabs ? "" : " disabled") + '>إغلاق كل التبويبات</button>'; document.body.appendChild(menu); tabContextMenu = menu; tabContextMenuTarget = source; var bounds = menu.getBoundingClientRect(), left = Math.max(4, Math.min(event.clientX, window.innerWidth - bounds.width - 4)), top = Math.max(4, Math.min(event.clientY, window.innerHeight - bounds.height - 4)); menu.style.left = left + "px"; menu.style.top = top + "px"; tabContextMenuOutsideHandler = function (outsideEvent) { if (!menu.contains(outsideEvent.target)) closeTabContextMenu(false); }; tabContextMenuKeyHandler = function (keyEvent) { if (keyEvent.key === "Escape") { keyEvent.preventDefault(); keyEvent.stopPropagation(); closeTabContextMenu(true); } }; menu.onfocusout = function (focusEvent) { if (!menu.contains(focusEvent.relatedTarget)) closeTabContextMenu(false); }; document.addEventListener("pointerdown", tabContextMenuOutsideHandler, true); document.addEventListener("keydown", tabContextMenuKeyHandler, true); menu.onclick = function (clickEvent) { if (clickEvent.target.closest("[data-close-all-tabs]") && hasClosableTabs) { closeTabContextMenu(false); closeAllTabs(); } }; var action = menu.querySelector("[data-close-all-tabs]"); if (action && !action.disabled) action.focus(); }
  function renderTabs() {
    closeTabContextMenu(false);
    var switcher = document.getElementById("workspaceSwitcher"), list = document.getElementById("workspaceTabList");
    switcher.open = false;
    switcher.hidden = tabs.length < 2;
    switcher.querySelector("summary").innerHTML = iconSvg("quick");
    function tabButton(tab, compact) {
      var title = U.escape(tab.title), route = U.escape(tab.route), active = tab.route === activeRoute;
      var dirty = tab.view && tab.view.isWorkspaceDirty && tab.view.isWorkspaceDirty();
      return '<button class="tab-select" type="button" data-tab-route="' + route + '" title="' + title + '" aria-label="' + title + (dirty ? '، تغييرات غير محفوظة' : '') + '"' + (active ? ' aria-current="page"' : '') + '>' +
        '<span class="tab-icon">' + iconSvg((registry.modules.find(function (module) { return module.items.indexOf(tab.route) >= 0; }) || {}).id || (tab.route.indexOf("report-") === 0 ? "reports" : "quick")) + '</span><span class="tab-title">' + title + '</span>' +
        (!compact ? '<span class="tab-dirty" aria-hidden="true"' + (dirty ? '' : ' hidden') + '></span>' : '') + '</button>';
    }
    tabStrip.innerHTML = tabs.map(function (tab) {
      return '<div class="workspace-tab' + (tab.route === activeRoute ? ' active' : '') + '">' + tabButton(tab, false) +
        (tab.route !== HOME_ROUTE ? '<button class="tab-close" type="button" data-close-tab="' + U.escape(tab.route) + '" aria-label="إغلاق ' + U.escape(tab.title) + '">×</button>' : '') + '</div>';
    }).join("");
    list.innerHTML = tabs.map(function (tab) { return tabButton(tab, true); }).join("");
    function focusActive() {
      var button = tabStrip.querySelector('[aria-current="page"]');
      if (button) button.focus({ preventScroll: true });
      else workspace.focus({ preventScroll: true });
    }
    tabStrip.onclick = list.onclick = function (event) {
      var close = event.target.closest("[data-close-tab]"), tab = event.target.closest("[data-tab-route]");
      if (close) {
        closeTab(close.dataset.closeTab);
        if (!close.isConnected) focusActive();
      } else if (tab) {
        navigate(tab.dataset.tabRoute);
        switcher.open = false;
        if (!tab.isConnected || list.contains(tab)) focusActive();
      }
    };
    tabStrip.oncontextmenu = showTabContextMenu;
    tabStrip.onkeydown = function (event) {
      if (!event.target.matches("[data-tab-route]")) return;
      var buttons = Array.from(tabStrip.querySelectorAll("[data-tab-route]")), index = buttons.indexOf(event.target);
      var next = { ArrowLeft: index + 1, ArrowRight: index - 1, Home: 0, End: buttons.length - 1 }[event.key];
      if (next === undefined) return;
      event.preventDefault();
      buttons[(next + buttons.length) % buttons.length].focus();
    };
    switcher.onkeydown = function (event) {
      if (event.key !== "Escape" || !switcher.open) return;
      event.stopPropagation();
      switcher.open = false;
      switcher.querySelector("summary").focus();
    };
    var active = tabStrip.querySelector(".workspace-tab.active");
    if (active) active.scrollIntoView({ block: "nearest", inline: "nearest" });
  }
  function updateActiveDirtyState() {
    var tab = tabs.find(function (item) { return item.route === activeRoute; });
    if (!tab || !tab.view || !tab.view.isWorkspaceDirty) return;
    var button = tabStrip.querySelector('[aria-current="page"]');
    if (!button) return;
    var dirty = tab.view.isWorkspaceDirty(), marker = button.querySelector(".tab-dirty");
    if (marker.hidden === !dirty) return;
    marker.hidden = !dirty;
    button.setAttribute("aria-label", tab.title + (dirty ? "، تغييرات غير محفوظة" : ""));
  }
  workspace.addEventListener("input", updateActiveDirtyState);
  workspace.addEventListener("change", updateActiveDirtyState);
  workspace.addEventListener("click", updateActiveDirtyState);
  function refreshPageData(tab) {
    if (!tab || activeRoute !== tab.route || !tab.view) return;
    if(typeof tab.view.beforeRouteReplace==="function"&&!tab.view.beforeRouteReplace(tab.state))return;
    var view = tab.view, searchSelector = 'input[type="search"], input[id*="search" i], input[data-search]', searchValues = Array.prototype.map.call(view.querySelectorAll(searchSelector), function (input) { return input.value; });
    tab.initialized = false; renderActive(true);
    Array.prototype.forEach.call(view.querySelectorAll(searchSelector), function (input, index) { if (index < searchValues.length) { input.value = searchValues[index]; input.dispatchEvent(new Event("input", { bubbles: true })); } });
  }
  function showStaleNotice(tab) {
    if (!tab || tab.route === HOME_ROUTE || !tab.view || !tab.view.isConnected || tab.view.querySelector("[data-page-stale]")) return;
    var notice = document.createElement("div"); notice.className = "page-stale-notice"; notice.setAttribute("data-page-stale", ""); notice.setAttribute("role", "status");
    notice.innerHTML = '<span>بيانات الصفحة اتغيرت. حدّث الصفحة لعرض البيانات الحالية.</span><button class="button" type="button" data-page-refresh>تحديث البيانات</button>';
    tab.view.insertBefore(notice, tab.view.firstChild);
  }
  function ensurePageRefreshControl(view, route) {
    if (!view) return;
    var fallback = view.querySelector(".page-refresh-fallback");
    if (route === HOME_ROUTE) { if (fallback) fallback.remove(); return; }
    var existing = Array.prototype.some.call(view.querySelectorAll("[data-page-refresh]"), function (button) { return !button.closest(".page-refresh-fallback"); }) || route === "report-overview" && !!view.querySelector("[data-report-refresh]");
    if (existing) { if (fallback) fallback.remove(); return; }
    if (!fallback) { fallback = document.createElement("div"); fallback.className = "page-refresh-fallback"; fallback.innerHTML = '<button class="button" type="button" data-page-refresh aria-label="تحديث البيانات">تحديث البيانات</button>'; view.insertBefore(fallback, view.firstChild); }
  }
  function renderActive(force) {
    var screen = screensByRoute[activeRoute], tab = tabs.find(function (item) { return item.route === activeRoute; }), renderer = screen && implementationRenderer(screen);
    document.onkeydown = null;
    workspace.classList.toggle("home-active", activeRoute === HOME_ROUTE);
    if (!activeRoute) { workspace.innerHTML = registry.screens.some(function (item) { return !item.navigationHidden && routeEnabled(item); })
      ? '<div class="empty-state"><strong>لا توجد مساحة عمل مفتوحة</strong><span>اختر شاشة من القائمة الجانبية أو البحث.</span></div>'
      : '<div class="empty-state"><strong>لا توجد شاشات متاحة لهذا الحساب</strong><span>اطلب من مسؤول النظام منح صلاحية شاشة واحدة على الأقل.</span></div>';
      document.title = "The Cashier";
      return;
    }
    if (!screen || !renderer || !routeEnabled(screen)) { workspace.innerHTML = '<div class="empty-state"><strong>تعذر عرض الشاشة</strong><span>لا تملك صلاحية الوصول إلى هذا المسار.</span></div>'; return; }
    if (!tab.view) { tab.view = document.createElement("div"); tab.view.className = "workspace-page"; tab.initialized = false; }
    workspace.innerHTML = ""; workspace.appendChild(tab.view); document.title = screen.title + " | The Cashier";
    if (tab.initialized && !force) { if (tab.route === "report-overview" && tab.view.refreshReportsCenter) tab.view.refreshReportsCenter(); if (tab.route !== HOME_ROUTE && (tab.route !== "report-overview" || Access.routeEnabled(screen))) { var checkId = tab.freshnessCheckId = (tab.freshnessCheckId || 0) + 1; AlKhayalApi.checkPageSnapshot(tab.route).then(function (changed) { if (checkId !== tab.freshnessCheckId || activeRoute !== tab.route || tabs.indexOf(tab) < 0) return; if (changed) showStaleNotice(tab); }).catch(function () { if (checkId === tab.freshnessCheckId && activeRoute === tab.route) UI.toast("تعذر التحقق من بيانات الصفحة الحالية.", "warning"); }); } requestAnimationFrame(function () { if (activeRoute === tab.route && tab.view && tab.view.isConnected) workspace.scrollTop = tab.route === HOME_ROUTE ? 0 : tab.scrollTop || 0; }); return; }
    var generation = ++renderGeneration, view = tab.view; tab.viewGeneration = generation;
    try {
      AlKhayalApi.resetPageSnapshot(tab.route);
      renderer(screen, view, { state: tab.state, navigate: navigate, navigateFromShortcut: navigateFromShortcut, canNavigate: function(route){return routeEnabled(screensByRoute[route]);}, mountSettingsSearch: function (input, panel) {
        panel.innerHTML = searchPanel("settingsGlobalSearchList");
        return universalSearch.bind(input, panel, { settings: true, active: function () {
          return activeRoute === tab.route && tab.view === view && tab.viewGeneration === generation;
        } });
      }, registry: registry, user: currentUser, invalidateCurrentPage: function () { tab.initialized=false;tab.freshnessCheckId=(tab.freshnessCheckId||0)+1;if(tab.view&&tab.view.pageRefreshObserver)tab.view.pageRefreshObserver.disconnect();tab.view=null;tab.viewGeneration=null; }, isCurrent: function () { return tabs.indexOf(tab) >= 0 && tab.view === view && tab.viewGeneration === generation; }, isActive: function () { return activeRoute === tab.route && tabs.indexOf(tab) >= 0 && tab.view === view && tab.viewGeneration === generation; } });
      ensurePageRefreshControl(view, tab.route);
      if (tab.route !== HOME_ROUTE && !view.pageRefreshObserver) { view.pageRefreshObserver = new MutationObserver(function () { ensurePageRefreshControl(view, tab.route); if (activeRoute === tab.route) updateActiveDirtyState(); }); view.pageRefreshObserver.observe(view, { childList: true, subtree: true }); }
      if (tab.route !== HOME_ROUTE && !view.pageRefreshBound) { view.pageRefreshBound = true; view.addEventListener("click", function (event) { var button = event.target.closest("[data-page-refresh]"); if (!button || !view.contains(button)) return; event.preventDefault(); if (button.disabled) return; refreshPageData(tab); }); }
      tab.initialized = true;
      requestAnimationFrame(function () { if (activeRoute === tab.route && tab.view === view && view.isConnected) workspace.scrollTop = tab.route === HOME_ROUTE ? 0 : tab.scrollTop || 0; });
    } catch (error) {
      console.error(error); tab.initialized = false; view.innerHTML = '<div class="empty-state"><strong>حدث خطأ في عرض الشاشة</strong><span>أعد المحاولة، وإذا استمرت المشكلة فتواصل مع مسؤول النظام.</span><br><button class="button" data-retry-render>إعادة المحاولة</button></div>'; var retry = view.querySelector("[data-retry-render]"); if (retry) retry.onclick = function(){tab.view=null;tab.initialized=false;activateTab(tab.route);};
    }
  }
  function updateNavigation() {
    var back = document.getElementById("navigationBack");
    if (back) back.disabled = !navigationHistory.some(function (route) {
      return route !== activeRoute && routeEnabled(screensByRoute[route]);
    });
    document.querySelectorAll(".sidebar [data-route]").forEach(function (button) {
      var active = !button.classList.contains("sidebar-flyout-landing") && button.dataset.route === activeRoute;
      button.classList.toggle("active", active);
      if (active) button.setAttribute("aria-current", "page");
      else button.removeAttribute("aria-current");
    });
    sidebarNavigation.querySelectorAll(".sidebar-group").forEach(function (group) {
      var active = !!group.querySelector('[aria-current="page"]');
      var activeChild = !!group.querySelector('.sidebar-children [aria-current="page"]');
      var activeLanding = !!group.querySelector('.sidebar-landing[aria-current="page"]');
      group.classList.toggle("open", (activeChild && !activeLanding) || group.dataset.module === manualGroup);
      group.querySelector("[data-module-toggle]").classList.toggle("active", active);
    });
    var isAlert = activeRoute === "alerts-list", alertButton = document.getElementById("headerAlerts");
    if (isAlert) alertButton.setAttribute("aria-current", "page");
    else alertButton.removeAttribute("aria-current");
    if (activeRoute !== revealedRoute) {
      revealedRoute = activeRoute;
      var current = sidebarNavigation.querySelector('[aria-current="page"]');
      if (current) {
        var group = current.closest(".sidebar-group");
        var heading = group && group.querySelector(".sidebar-group-heading");
        if (heading) heading.scrollIntoView({ block: "nearest", inline: "nearest" });
        if (!heading || !shell.classList.contains("sidebar-collapsed")) current.scrollIntoView({ block: "nearest", inline: "nearest" });
      }
    }
    updateQuickbarActive();
    if (sidebarFlyout) sidebarFlyout.update();
  }
  var universalSearch;
  var searchAliases = {
    "settings-company": "company profile اسم الشركة بيانات الشركة",
    "settings-printers": "printing settings receipt designer barcode settings إعدادات الطباعة مصمم الإيصالات مصمم الفواتير الباركود",
    "settings-backup": "backup restore النسخ الاحتياطي استعادة نسخة",
    "users-list": "users permissions المستخدمين صلاحيات الكاشير",
    "feature-management": "feature management إدارة الميزات إدارة الوحدات",
    "business-activities": "scales scale barcode موازين باركود الميزان",
    "report-overview": "reports center مركز التقارير"
  };
  function searchPanel(id) {
    return '<div id="' + id + '" role="listbox" aria-label="نتائج البحث" data-search-list></div>' +
      '<div class="global-search-status" role="status" data-search-status></div>' +
      '<button class="button" type="button" data-search-retry hidden>إعادة المحاولة</button>';
  }
  function setupSearch() {
    universalSearch = window.AlKhayalGlobalSearch.create({
      canNavigate: function (route) { return routeEnabled(screensByRoute[route]); },
      request: function (url) { return AlKhayalApi.request(url, { skipPageTrack: true }); },
      navigate: function (route, state) { navigate(route, state); workspace.focus({ preventScroll: true }); },
      unavailable: function () { UI.toast("لا يمكن فتح هذا السجل", "warning"); },
      icon: function (row) { return quickIcon(row.route, false); },
      openScreen: function (row) {
        if (row.action) runQuickAction(row.route);
        else { navigate(row.route); workspace.focus({ preventScroll: true }); }
        return true;
      },
      screens: function (query, settings) {
        var words = U.normalize(query).split(/\s+/).filter(Boolean);
        var entries = registry.screens.filter(function (screen) {
          return !screen.navigationHidden && routeEnabled(screen);
        }).map(function (screen) { return { screen: screen, title: screen.title }; });
        quickActions().forEach(function (item) {
          var screen = screensByRoute[item.route];
          if (screen && routeEnabled(screen) && (item.action || screen.navigationHidden)) entries.push({ screen: screen, title: item.label, action: true });
        });
        return entries.map(function (entry) {
          var screen = entry.screen, title = U.normalize(entry.title), route = U.normalize(screen.route.replace(/-/g, " "));
          var metadata = U.normalize([title, route, screen.module, screen.description, screen.aliases, searchAliases[screen.route]].join(" "));
          if (!words.every(function (word) { return metadata.includes(word); })) return null;
          return { route: screen.route, title: entry.title, subtitle: screen.module, type: "screen", action: entry.action,
            group: "الشاشات والإعدادات", rank: (settings && screen.settingsGroup ? 0 : 2) +
              (words.every(function (word) { return title.includes(word) || route.includes(word); }) ? 0 : 1) };
        }).filter(Boolean).sort(function (a, b) { return a.rank - b.rank; }).slice(0, 10);
      }
    });
    var panel = document.getElementById("globalSearchResults");
    panel.innerHTML = searchPanel("globalSearchList");
    universalSearch.bind(document.getElementById("globalSearch"), panel, { active: function () { return true; } });
  }
  function setupGlobalShortcuts() { function touchElement(selector) { return activeRoute === "pos" ? workspace.querySelector(".touch-pos " + selector) : null; } function clickTouch(selector) { var button = touchElement(selector); if (button && !button.disabled) button.click(); } function focusTouch(selector, open) { var element = touchElement(selector); if (element && !element.disabled) { element.focus(); if (open && element.click) element.click(); } } window.AlKhayalKeyboardShortcuts.install({ navigate: navigate, openShift: function () { var button = document.getElementById("headerShift"); if (button && !button.disabled) button.click(); }, focusSearch: function () { var input = document.getElementById("globalSearch"); if (input) input.focus(); }, showHelp: function (body) { var layer = document.getElementById("modalLayer"); if (!layer || !layer.hidden) return; UI.modal({ title: "اختصارات لوحة المفاتيح", body: body, compact: true }); }, getModalLayer: function () { return document.getElementById("modalLayer"); }, isTouchPos: function () { return activeRoute === "pos"; }, captureTouchBarcode: function (event) { return window.AlKhayalTouchPosScanner && window.AlKhayalTouchPosScanner.capture ? window.AlKhayalTouchPosScanner.capture(event) : false; }, focusTouchSearch: function () { focusTouch("[data-search]"); }, focusTouchCustomer: function () { focusTouch("[data-customer-search]", true); }, newTouchInvoice: function () { clickTouch("[data-new-touch-invoice]"); }, postTouchSale: function () { clickTouch("[data-post]"); }, focusTouchPayment: function () { focusTouch("[data-payment]", true); } }); }
  function setupShell() {
    sidebarFlyout = window.AlKhayalSidebarFlyout({ navigation: sidebarNavigation, shell: shell, navigate: navigateFromSidebar, activeRoute: function () { return activeRoute; } });
    if (window.AlKhayalWorkforce) window.AlKhayalWorkforce.header({ user: currentUser, state: {}, navigate: navigate });
    var key = navigationPreference("alkhayal_sidebar_collapsed"), stored = localStorage.getItem(key);
    var legacy = localStorage.getItem("alkhayal_sidebar_collapsed");
    if (stored === null && legacy !== null) {
      stored = legacy;
      localStorage.setItem(key, stored);
      localStorage.removeItem("alkhayal_sidebar_collapsed");
    }
    var groupKey = navigationPreference("alkhayal_open_nav");
    manualGroup = localStorage.getItem(groupKey);
    var legacyGroups = localStorage.getItem("alkhayal_open_nav");
    if (manualGroup === null && legacyGroups !== null) {
      try { var groups = JSON.parse(legacyGroups); manualGroup = Array.isArray(groups) ? groups[groups.length - 1] : null; }
      catch (error) { manualGroup = null; }
      localStorage.setItem(groupKey, manualGroup || "");
      localStorage.removeItem("alkhayal_open_nav");
    }
    var narrow = matchMedia("(max-width: 1080px)"), toggle = document.getElementById("sidebarToggle");
    function applySidebar() {
      sidebarFlyout.close(false);
      var collapsed = stored === null ? narrow.matches : stored === "true";
      shell.classList.toggle("sidebar-collapsed", collapsed);
      toggle.setAttribute("aria-expanded", String(!collapsed));
      toggle.title = collapsed ? "توسيع القائمة الجانبية" : "طي القائمة الجانبية";
      toggle.setAttribute("aria-label", toggle.title);
      sidebarFlyout.update();
    }
    applySidebar();
    narrow.addEventListener("change", applySidebar);
    toggle.onclick = function () {
      stored = String(!shell.classList.contains("sidebar-collapsed"));
      localStorage.setItem(key, stored);
      applySidebar();
    };
    var footer = document.querySelector(".sidebar-footer"), settings = footer.querySelector("[data-route]");
    settings.hidden = !routeEnabled(screensByRoute["settings-program"]);
    settings.onclick = function () { navigate("settings-program"); };
    document.getElementById("logoutButton").onclick = async function () {
      try { await AlKhayalApi.request("/api/auth/logout", { method: "POST", body: "{}" }); }
      catch (error) { if (error.code !== "invalid_csrf" && error.status !== 401) { UI.toast(error.message, "danger"); return; } }
      AlKhayalApi.clearSession();
      location.replace("/login.html");
    };
    function updateClock() {
      document.getElementById("topbarClock").textContent = new Intl.DateTimeFormat("ar-EG-u-nu-latn", { weekday: "short", hour: "2-digit", minute: "2-digit" }).format(new Date());
    }
    updateClock();
    setInterval(updateClock, 60000);
    setupSearch();
    setupGlobalShortcuts();
  }
  function reconcileAccess(message) {
    if (universalSearch) universalSearch.invalidate();
    var removedActive = activeRoute && !routeEnabled(screensByRoute[activeRoute]), previousTabCount = tabs.length;
    tabs = tabs.filter(function (tab) { return routeEnabled(screensByRoute[tab.route]); });
    var removedTab = tabs.length < previousTabCount, homeAvailable = ensureHomeTab();
    if (removedActive || !activeRoute) {
      var first = homeAvailable ? HOME_ROUTE : enabledRoutes(registry.screens.map(function (item) { return item.route; }))[0];
      activeRoute = first || null;
    }
    if (message && (removedActive || removedTab)) UI.toast(message, "warning");
    if (activeRoute && !tabs.some(function (tab) { return tab.route === activeRoute; })) tabs.unshift({ route: activeRoute, title: screensByRoute[activeRoute].title, state: {}, scrollTop: 0 });
    var settingsButton = document.querySelector('.sidebar-footer [data-route="settings-program"]');
    if (settingsButton) settingsButton.hidden = !routeEnabled(screensByRoute["settings-program"]);
    var search = document.getElementById("globalSearch");
    if (document.activeElement === search) search.dispatchEvent(new Event("input"));
    buildSidebar(); buildQuickbar(); renderTabs();
    var reportTab = tabs.find(function (tab) { return tab.route === "report-overview"; });
    if (reportTab && reportTab.view && typeof reportTab.view.refreshReportsCenter === "function") reportTab.view.refreshReportsCenter();
    if (removedActive || !activeRoute) {
      AlKhayalApi.setActivePage(activeRoute);
      workspace.classList.toggle("touch-cashier-active", activeRoute === "pos");
      renderActive();
    }
    updateNavigation();
  }
  window.addEventListener("alkhayal:features-changed",function(){if(!registry||!navigationReady)return;if(universalSearch)universalSearch.invalidate();Access.load({notify:false}).then(function(){reconcileAccess("أُغلقت شاشة لم تعد متاحة.");}).catch(function(error){if(error.status===401)location.replace("/login.html");});});
  window.addEventListener("alkhayal:access-changed",function(){if(registry&&navigationReady)reconcileAccess("تم تحديث صلاحيات الشاشات.");});
  document.addEventListener("click", function (event) { if (!event.target.closest("#quickbarMore")) closeQuickbarMenu(false); if (!event.target.closest("#workspaceSwitcher")) document.getElementById("workspaceSwitcher").open = false; });
  Promise.all([loadRegistry(),authenticate(),Features.load(),Access.load({notify:false}),Settings.load({notify:false})]).then(function(){buildSidebar();buildQuickbar();setupShell();var initial=HOME_ROUTE;if(!routeEnabled(screensByRoute[initial]))initial=enabledRoutes(registry.screens.filter(function(item){return !item.navigationHidden;}).map(function(item){return item.route;}))[0];if(initial)navigate(initial);else renderActive();renderTabs();navigationReady=true;}).catch(function(error){if(error.message!=="unauthorized"){console.error(error);workspace.innerHTML='<div class="empty-state"><strong>تعذر بدء التطبيق</strong><span>تحقق من تشغيل الخادم ثم أعد فتح التطبيق.</span></div>';}});
}());
