(function () {
  "use strict";
  var U = window.AlKhayalUtils, UI = window.AlKhayalUI, renderers = window.AlKhayalRenderers, Features = window.AlKhayalFeatures, Access = window.AlKhayalAccess, Settings = window.AlKhayalSettings, registry, screensByRoute = {}, tabs = [], navigationHistory = [], restoringNavigation = false, navigationReady = false, activeRoute = null, currentUser = null, renderGeneration = 0;
  var HOME_ROUTE = "dashboard", workspace = document.getElementById("workspace"), tabStrip = document.getElementById("workspaceTabs"), shell = document.getElementById("appShell"), sidebarNavigation = document.getElementById("sidebarNavigation"), quickbar = document.getElementById("quickbar");
  var sidebarFlyout = null, quickbarResizeObserver = null, tabContextMenu = null, tabContextMenuTarget = null, tabContextMenuOutsideHandler = null, tabContextMenuKeyHandler = null;
  UI.initialize();
  function implementationRenderer(screen) { return screen.implemented === true ? renderers[screen.renderer] : renderers["under-construction"]; }
  function roleLabel(role) { return role === "administrator" ? "مسؤول النظام" : "كاشير"; }
  var iconPaths = { "payment-methods": "M3 5h18v14H3zM3 9h18M6 15h4", home: "M3 10.5 12 3l9 7.5M5 9v11h14V9M9 20v-6h6v6", customers: "M16 20v-2a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4v2M9.5 10a4 4 0 1 0 0-8 4 4 0 0 0 0 8ZM19 8v6M22 11h-6", suppliers: "M3 6h11v12H3zM14 10h4l3 3v5h-7M6 18a2 2 0 1 0 0-4 2 2 0 0 0 0 4M17 18a2 2 0 1 0 0-4 2 2 0 0 0 0 4", purchases: "M5 7h14l-1 13H6L5 7ZM8 7a4 4 0 0 1 8 0", sales: "M3 5h18l-2 13H5L3 5ZM8 5a4 4 0 0 1 8 0M8 10h8", returns: "M4 7h10a5 5 0 1 1-1 10H8M8 4 4 7l4 3", inventory: "M3 8h18v13H3zM6 8V5h12v3M7 12h10M7 16h6", treasury: "M3 20h18M5 20V9h14v11M3 9l9-6 9 6M9 13h2M13 13h2M9 17h2M13 17h2", expenses: "M5 3h14v18H5zM8 7h8M8 11h8M8 15h5M8 19h4", alerts: "M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4", employees: "M16 20v-2a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4v2M9.5 10a4 4 0 1 0 0-8 4 4 0 0 0 0 8M17 8h4M19 6v4", permissions: "M12 3 20 6v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6l8-3ZM9 12l2 2 4-4", reports: "M4 19V5M4 19h17M8 16v-5M12 16V8M16 16v-3", installments: "M4 6h16M4 12h16M4 18h16M8 3v18M16 3v18", maintenance: "M14 4a5 5 0 0 0-6 6l-5 5 4 4 5-5a5 5 0 0 0 6-6l-3 3-4-4 3-3Z", branches: "M3 21h18M5 21V7l7-4 7 4v14M9 21v-6h6v6M8 9h.01M16 9h.01", printing: "M6 9V3h12v6M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2M6 14h12v7H6z", settings: "M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M18.4 5.6l-2.1 2.1M7.7 16.3l-2.1 2.1M12 16a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z", quick: "M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z", more: "M5 12h.01M12 12h.01M19 12h.01" };
  function iconSvg(key) { return '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="' + (iconPaths[key] || iconPaths.quick) + '"></path></svg>'; }
  function quickIcon(route, more) { return iconSvg(more ? "more" : route === "pos" ? "sales" : route.indexOf("purchase") >= 0 ? "purchases" : route.indexOf("product") >= 0 || route.indexOf("warehouse") >= 0 ? "inventory" : route.indexOf("customer") >= 0 ? "customers" : route.indexOf("supplier") >= 0 ? "suppliers" : route.indexOf("return") >= 0 ? "returns" : route.indexOf("account") >= 0 ? "treasury" : route.indexOf("financial") >= 0 ? "expenses" : route.indexOf("stock") >= 0 || route.indexOf("alert") >= 0 ? "alerts" : "quick"); }
  function loadRegistry() { return fetch("/data/screens.json", { credentials: "same-origin", cache: "no-store" }).then(function (response) { if (!response.ok) throw new Error("تعذر تحميل سجل الشاشات."); return response.json(); }).then(function (data) { registry = data; data.screens.forEach(function (screen) { screensByRoute[screen.route] = screen; }); var missing=data.screens.filter(function(screen){return typeof renderers[screen.renderer]!=="function";});if(missing.length)throw new Error("منفذ العرض غير محمل: "+missing.map(function(screen){return screen.route+" → "+screen.renderer;}).join("، "));data.screens.filter(function(screen){return screen.redirectTo;}).forEach(function(screen){if(!screensByRoute[screen.redirectTo])throw new Error("مسار التوافق غير صالح: "+screen.route);}); }); }
  function authenticate() { return AlKhayalApi.request("/api/auth/me").then(function (user) { currentUser = user; var name = user.displayName || user.username; document.getElementById("userDisplayName").textContent = name; document.getElementById("statusUser").textContent = name; document.getElementById("userRole").textContent = roleLabel(user.role); document.getElementById("userAvatar").textContent = name.charAt(0); return user; }).catch(function () { location.replace("/login.html"); throw new Error("unauthorized"); }); }
  function routeEnabled(screen) { if (!screen || !Features.routeEnabled(screen)) return false; if (screen.route === "report-overview") return Access.routeEnabled(screen) || !!(registry && registry.screens.some(function (item) { return item.route.indexOf("report-") === 0 && item.route !== "report-overview" && Features.routeEnabled(item) && Access.routeEnabled(item); })); return Access.routeEnabled(screen); }
  function resolveRoute(route) { var current=route,visited={};while(screensByRoute[current]&&screensByRoute[current].redirectTo&&!visited[current]){visited[current]=true;current=screensByRoute[current].redirectTo;}return current; }
  function enabledRoutes(routes) { return routes.filter(function (route) { return routeEnabled(screensByRoute[route]); }); }
  function buildSidebar() {
    if (sidebarFlyout) sidebarFlyout.close(false);
    var open = JSON.parse(localStorage.getItem("alkhayal_open_nav") || "[\"home\"]");
    sidebarNavigation.innerHTML = registry.modules.map(function (module) {
      var visibleRoutes = enabledRoutes(module.items);
      if (!visibleRoutes.length) return "";
      if (module.id === "reports") return '<button class="sidebar-group-toggle" type="button" data-route="' + U.escape(visibleRoutes[0]) + '" title="' + U.escape(module.title) + '" aria-label="' + U.escape(module.title) + '"><span class="nav-icon">' + iconSvg(module.id) + '</span><span class="nav-label">' + U.escape(module.title) + '</span></button>';
      var items = visibleRoutes.map(function (route) { var screen = screensByRoute[route]; return screen ? '<button class="sidebar-link" type="button" data-route="' + U.escape(route) + '" title="' + U.escape(screen.title) + '"><span class="nav-label">' + U.escape(screen.title) + '</span></button>' : ""; }).join("");
      var isOpen = open.indexOf(module.id) >= 0;
      return '<section class="sidebar-group' + (isOpen ? " open" : "") + '" data-module="' + U.escape(module.id) + '"><button class="sidebar-group-toggle" type="button" data-module-toggle aria-label="' + U.escape(module.title) + '" aria-expanded="' + isOpen + '" title="' + U.escape(module.title) + '"><span class="nav-icon">' + iconSvg(module.id) + '</span><span class="nav-label">' + U.escape(module.title) + '</span><span class="nav-arrow" aria-hidden="true">‹</span></button><div class="sidebar-children">' + items + '</div></section>';
    }).join("");
    sidebarNavigation.onclick = function (event) { var groupToggle = event.target.closest("[data-module-toggle]"), route = event.target.closest("[data-route]"); if (groupToggle) { var group = groupToggle.closest(".sidebar-group"), moduleId = group.getAttribute("data-module"); if (shell.classList.contains("sidebar-collapsed") || window.innerWidth <= 1080) { sidebarFlyout.open(groupToggle, true); return; } var isOpen = group.classList.toggle("open"); groupToggle.setAttribute("aria-expanded", String(isOpen)); var ids = Array.prototype.map.call(sidebarNavigation.querySelectorAll(".sidebar-group.open"), function (item) { return item.getAttribute("data-module"); }); localStorage.setItem("alkhayal_open_nav", JSON.stringify(ids)); } if (route) navigateFromSidebar(route.getAttribute("data-route")); };
  }
  function quickbarItem(item, menu) {
    return '<button class="quick-tile' + (item.accent ? " accent" : "") + '" type="button" data-quick-route="' + U.escape(item.route) + '" title="' + U.escape(item.label) + '"' + (menu ? ' role="menuitem"' : '') + '>' +
      '<span class="quick-icon">' + quickIcon(item.route, false) + (item.route === "alerts-list" ? '<span class="quick-alert-count" data-alert-count aria-live="polite" hidden></span>' : '') + '</span>' +
      '<span class="quick-label">' + U.escape(item.label) + '</span></button>';
  }
  function closeQuickbarMenu(restoreFocus) {
    var button = quickbar.querySelector("[data-quickbar-more]"), menu = quickbar.querySelector("#quickbarMenu");
    if (!button || !menu || menu.hidden) return;
    menu.hidden = true;
    button.setAttribute("aria-expanded", "false");
    if (restoreFocus) button.focus();
  }
  function layoutQuickbar() {
    var row = quickbar.querySelector("#quickbarTiles"), more = quickbar.querySelector("#quickbarMore"), menu = quickbar.querySelector("#quickbarMenu");
    if (!row || !row.clientWidth) return;
    if (!menu.hidden) closeQuickbarMenu(true);
    var buttons = Array.prototype.slice.call(row.querySelectorAll("[data-quick-route]"));
    buttons.forEach(function (button) { button.hidden = false; });
    more.hidden = false;
    var widths = buttons.map(function (button) { return Math.ceil(button.getBoundingClientRect().width); });
    var gap = parseFloat(getComputedStyle(row).gap) || 0;
    var count = window.AlKhayalQuickbarLayout.visibleCount(widths, row.clientWidth, Math.ceil(more.getBoundingClientRect().width), gap);
    more.hidden = count === buttons.length;
    buttons.forEach(function (button, index) { button.hidden = index >= count; });
    menu.innerHTML = registry.quickAccess.filter(function (item) { return routeEnabled(screensByRoute[item.route]); }).slice(count).map(function (item) { return quickbarItem(item, true); }).join("");
    updateAlertBadge();
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
    var items = registry.quickAccess.filter(function (item) { return routeEnabled(screensByRoute[item.route]); });
    quickbar.innerHTML =
      '<div class="quickbar-tiles" id="quickbarTiles">' +
        items.map(function (item) { return quickbarItem(item, false); }).join("") +
        '<div class="quickbar-more" id="quickbarMore" hidden><button class="quickbar-more-button" type="button" data-quickbar-more aria-expanded="false" aria-controls="quickbarMenu" aria-haspopup="menu" title="المزيد">' + quickIcon("", true) + '<span>المزيد</span></button><div class="quickbar-menu" id="quickbarMenu" role="menu" aria-label="المزيد من الاختصارات" hidden></div></div>' +
      '</div>' +
      '<button class="quickbar-back" id="navigationBack" type="button" aria-label="رجوع خطوة" title="رجوع خطوة" disabled>' +
        '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M5 12h14M13 6l6 6-6 6"></path></svg><span>رجوع</span>' +
      '</button>';
    layoutQuickbar();
    if (quickbarResizeObserver) quickbarResizeObserver.disconnect();
    quickbarResizeObserver = new ResizeObserver(layoutQuickbar);
    quickbarResizeObserver.observe(quickbar);
    window.AlKhayalAlerts.load();
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
      if (tile) { closeQuickbarMenu(false); navigateFromShortcut(tile.getAttribute("data-quick-route")); }
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
    var badges=quickbar.querySelectorAll("[data-alert-count]");if(!badges.length)return;
    var count=window.AlKhayalAlerts.count(),failed=!!window.AlKhayalAlerts.error();
    Array.prototype.forEach.call(badges,function(badge){badge.hidden=!count&&!failed;badge.textContent=failed?"!":String(count);badge.setAttribute("aria-label",failed?"تعذر تحديث التنبيهات":count+" تنبيه غير مقروء");});
  }
  window.addEventListener("alkhayal:alerts-changed",function(){
    updateAlertBadge();var tab=tabs.find(function(item){return item.route==="alerts-list";});
    if(tab && tab.view && tab.view.refreshAlerts)tab.view.refreshAlerts();
  });
  function saveActiveScroll() { var current = tabs.find(function (tab) { return tab.route === activeRoute; }); if (current) current.scrollTop = workspace.scrollTop; }
  function ensureHomeTab() { if (!screensByRoute[HOME_ROUTE] || !routeEnabled(screensByRoute[HOME_ROUTE])) return false; if (!tabs.some(function (tab) { return tab.route === HOME_ROUTE; })) tabs.unshift({ route: HOME_ROUTE, title: screensByRoute[HOME_ROUTE].title, state: {}, scrollTop: 0 }); return true; }
  function activateTab(route) { saveActiveScroll(); activeRoute = route; localStorage.setItem("alkhayal_last_route",route); AlKhayalApi.setActivePage(route); workspace.classList.toggle("touch-cashier-active",route==="pos"); renderTabs(); renderActive(); updateNavigation(); }
  function navigate(route, routeState) { route=resolveRoute(route);var screen = screensByRoute[route]; if (!screen) { UI.toast("هذه الشاشة غير مسجلة.", "danger"); return; } if (!routeEnabled(screen)) { UI.toast("لا تملك صلاحية فتح هذه الشاشة أو أن وحدتها معطلة.", "warning"); return; } var existing = tabs.find(function (tab) { return tab.route === route; }); if(existing&&routeState&&existing.view&&typeof existing.view.beforeRouteReplace==="function"&&!existing.view.beforeRouteReplace(routeState))return; if(activeRoute&&activeRoute!==route&&!restoringNavigation){navigationHistory.push(activeRoute);if(navigationHistory.length>100)navigationHistory.shift();}if (!existing) { existing = { route: route, title: screen.title, state: {}, scrollTop: 0 }; tabs.push(existing); } if(routeState){existing.state=Object.assign({},route==="pos"&&existing.state.posCart?{posCart:existing.state.posCart}:{},routeState);existing.freshnessCheckId=(existing.freshnessCheckId||0)+1;existing.view=null;existing.viewGeneration=null;existing.initialized=false;} activateTab(route); localStorage.setItem("alkhayal_last_route", route); }
  function navigateFromSidebar(route) { route = resolveRoute(route); var screen = screensByRoute[route], existing = tabs.find(function (tab) { return tab.route === route; }); if (!screen || !routeEnabled(screen) || !existing) { navigate(route); return; } if (existing.view && typeof existing.view.beforeRouteReplace === "function" && !existing.view.beforeRouteReplace(null)) return; if (route === HOME_ROUTE) { existing.state = {}; existing.scrollTop = 0; existing.freshnessCheckId = (existing.freshnessCheckId || 0) + 1; existing.view = null; existing.viewGeneration = null; existing.initialized = false; navigate(route); return; } saveActiveScroll(); tabs.splice(tabs.indexOf(existing), 1); if (activeRoute === route) activeRoute = null; navigate(route); }
  function navigateFromShortcut(route, routeState) { if (routeState) { navigate(route, routeState); return; } navigateFromSidebar(route); }
  function navigateBack() { while(navigationHistory.length){var route=navigationHistory.pop();if(route===activeRoute||!screensByRoute[route]||!routeEnabled(screensByRoute[route]))continue;restoringNavigation=true;navigate(route);restoringNavigation=false;return;}updateNavigation(); }
  function closeTab(route) { var index = tabs.findIndex(function (tab) { return tab.route === route; }); if (index < 0 || route === HOME_ROUTE) return; var closing=tabs[index];if(closing.view&&typeof closing.view.beforeRouteReplace==="function"&&!closing.view.beforeRouteReplace(null))return; saveActiveScroll(); tabs.splice(index, 1); ensureHomeTab(); if (activeRoute === route) activeRoute = tabs.length ? tabs[Math.max(0, index - 1)].route : HOME_ROUTE; if(activeRoute)localStorage.setItem("alkhayal_last_route",activeRoute); workspace.classList.toggle("touch-cashier-active",activeRoute==="pos"); renderTabs(); renderActive(); updateNavigation(); }
  function closeTabContextMenu(restoreFocus) { if (!tabContextMenu) return; document.removeEventListener("pointerdown", tabContextMenuOutsideHandler, true); document.removeEventListener("keydown", tabContextMenuKeyHandler, true); tabContextMenu.remove(); tabContextMenu = null; tabContextMenuOutsideHandler = null; tabContextMenuKeyHandler = null; var target = tabContextMenuTarget; tabContextMenuTarget = null; if (restoreFocus && target && target.isConnected) target.focus(); }
  function closeAllTabs() { for (var i = 0; i < tabs.length; i++) { var tab = tabs[i]; if (tab.route !== HOME_ROUTE && tab.view && typeof tab.view.beforeRouteReplace === "function" && !tab.view.beforeRouteReplace(null)) return; } saveActiveScroll(); navigationHistory = []; var homeAvailable = ensureHomeTab(), remaining = homeAvailable ? tabs.find(function (tab) { return tab.route === HOME_ROUTE; }) : null; if (!remaining) { var first = enabledRoutes(registry.screens.filter(function (item) { return !item.navigationHidden; }).map(function (item) { return item.route; }))[0]; if (first) remaining = tabs.find(function (tab) { return tab.route === first; }) || { route: first, title: screensByRoute[first].title, state: {}, scrollTop: 0 }; } tabs = remaining ? [remaining] : []; activeRoute = remaining ? remaining.route : null; if (activeRoute) { localStorage.setItem("alkhayal_last_route", activeRoute); AlKhayalApi.setActivePage(activeRoute); } workspace.classList.toggle("touch-cashier-active", activeRoute === "pos"); renderTabs(); renderActive(); updateNavigation(); }
  function showTabContextMenu(event) { var source = event.target.closest && event.target.closest("[data-tab-route]"); if (!source) return; event.preventDefault(); closeTabContextMenu(false); var menu = document.createElement("div"), hasClosableTabs = tabs.some(function (tab) { return tab.route !== HOME_ROUTE; }); menu.className = "workspace-tab-context-menu"; menu.setAttribute("role", "menu"); menu.setAttribute("aria-label", "خيارات الصفحات"); menu.innerHTML = '<button type="button" role="menuitem" data-close-all-tabs' + (hasClosableTabs ? "" : " disabled") + '>إغلاق كل التبويبات</button>'; document.body.appendChild(menu); tabContextMenu = menu; tabContextMenuTarget = source; var bounds = menu.getBoundingClientRect(), left = Math.max(4, Math.min(event.clientX, window.innerWidth - bounds.width - 4)), top = Math.max(4, Math.min(event.clientY, window.innerHeight - bounds.height - 4)); menu.style.left = left + "px"; menu.style.top = top + "px"; tabContextMenuOutsideHandler = function (outsideEvent) { if (!menu.contains(outsideEvent.target)) closeTabContextMenu(false); }; tabContextMenuKeyHandler = function (keyEvent) { if (keyEvent.key === "Escape") { keyEvent.preventDefault(); keyEvent.stopPropagation(); closeTabContextMenu(true); } }; menu.onfocusout = function (focusEvent) { if (!menu.contains(focusEvent.relatedTarget)) closeTabContextMenu(false); }; document.addEventListener("pointerdown", tabContextMenuOutsideHandler, true); document.addEventListener("keydown", tabContextMenuKeyHandler, true); menu.onclick = function (clickEvent) { if (clickEvent.target.closest("[data-close-all-tabs]") && hasClosableTabs) { closeTabContextMenu(false); closeAllTabs(); } }; var action = menu.querySelector("[data-close-all-tabs]"); if (action && !action.disabled) action.focus(); }
  function renderTabs() { closeTabContextMenu(false); tabStrip.innerHTML = tabs.map(function (tab) { return '<button class="workspace-tab' + (tab.route === activeRoute ? " active" : "") + '" type="button" data-tab-route="' + U.escape(tab.route) + '" title="' + U.escape(tab.title) + '" aria-label="' + U.escape(tab.title) + '" aria-haspopup="menu" aria-selected="' + (tab.route === activeRoute) + '"><span class="tab-title">' + U.escape(tab.title) + '</span>' + (tab.route !== HOME_ROUTE ? '<span class="tab-close" data-close-tab="' + U.escape(tab.route) + '" aria-label="إغلاق">×</span>' : "") + '</button>'; }).join(""); tabStrip.onclick = function (event) { var close = event.target.closest("[data-close-tab]"), tab = event.target.closest("[data-tab-route]"); if (close) { event.stopPropagation(); closeTab(close.getAttribute("data-close-tab")); } else if (tab) { var route=tab.getAttribute("data-tab-route");if(activeRoute!==route&&!restoringNavigation){navigationHistory.push(activeRoute);if(navigationHistory.length>100)navigationHistory.shift();}activateTab(route); } }; tabStrip.oncontextmenu = showTabContextMenu; var activeTab = tabStrip.querySelector(".workspace-tab.active"); if (activeTab) activeTab.scrollIntoView({ block: "nearest", inline: "nearest" }); }
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
    if (!activeRoute) { workspace.innerHTML = '<div class="empty-state"><strong>لا توجد شاشات متاحة لهذا الحساب</strong><span>اطلب من مسؤول النظام منح صلاحية شاشة واحدة على الأقل.</span></div>'; document.title="لا توجد صلاحيات | The Cashier"; return; }
    if (!screen || !renderer || !routeEnabled(screen)) { workspace.innerHTML = '<div class="empty-state"><strong>تعذر عرض الشاشة</strong><span>لا تملك صلاحية الوصول إلى هذا المسار.</span></div>'; return; }
    if (!tab.view) { tab.view = document.createElement("div"); tab.view.className = "workspace-page"; tab.initialized = false; }
    workspace.innerHTML = ""; workspace.appendChild(tab.view); document.title = screen.title + " | The Cashier";
    if (tab.initialized && !force) { if (tab.route === "report-overview" && tab.view.refreshReportsCenter) tab.view.refreshReportsCenter(); if (tab.route !== HOME_ROUTE && (tab.route !== "report-overview" || Access.routeEnabled(screen))) { var checkId = tab.freshnessCheckId = (tab.freshnessCheckId || 0) + 1; AlKhayalApi.checkPageSnapshot(tab.route).then(function (changed) { if (checkId !== tab.freshnessCheckId || activeRoute !== tab.route || tabs.indexOf(tab) < 0) return; if (changed) showStaleNotice(tab); }).catch(function () { if (checkId === tab.freshnessCheckId && activeRoute === tab.route) UI.toast("تعذر التحقق من بيانات الصفحة الحالية.", "warning"); }); } requestAnimationFrame(function () { if (activeRoute === tab.route && tab.view && tab.view.isConnected) workspace.scrollTop = tab.route === HOME_ROUTE ? 0 : tab.scrollTop || 0; }); return; }
    var generation = ++renderGeneration, view = tab.view; tab.viewGeneration = generation;
    try {
      AlKhayalApi.resetPageSnapshot(tab.route);
      renderer(screen, view, { state: tab.state, navigate: navigate, navigateFromShortcut: navigateFromShortcut, canNavigate: function(route){return routeEnabled(screensByRoute[route]);}, registry: registry, user: currentUser, invalidateCurrentPage: function () { tab.initialized=false;tab.freshnessCheckId=(tab.freshnessCheckId||0)+1;if(tab.view&&tab.view.pageRefreshObserver)tab.view.pageRefreshObserver.disconnect();tab.view=null;tab.viewGeneration=null; }, isActive: function () { return activeRoute === tab.route && tabs.indexOf(tab) >= 0 && tab.view === view && tab.viewGeneration === generation; } });
      ensurePageRefreshControl(view, tab.route);
      if (tab.route !== HOME_ROUTE && !view.pageRefreshObserver) { view.pageRefreshObserver = new MutationObserver(function () { ensurePageRefreshControl(view, tab.route); }); view.pageRefreshObserver.observe(view, { childList: true, subtree: true }); }
      if (tab.route !== HOME_ROUTE && !view.pageRefreshBound) { view.pageRefreshBound = true; view.addEventListener("click", function (event) { var button = event.target.closest("[data-page-refresh]"); if (!button || !view.contains(button)) return; event.preventDefault(); if (button.disabled) return; refreshPageData(tab); }); }
      tab.initialized = true;
      requestAnimationFrame(function () { if (activeRoute === tab.route && tab.view === view && view.isConnected) workspace.scrollTop = tab.route === HOME_ROUTE ? 0 : tab.scrollTop || 0; });
    } catch (error) {
      console.error(error); tab.initialized = false; view.innerHTML = '<div class="empty-state"><strong>حدث خطأ في عرض الشاشة</strong><span>أعد المحاولة، وإذا استمرت المشكلة فتواصل مع مسؤول النظام.</span><br><button class="button" data-retry-render>إعادة المحاولة</button></div>'; var retry = view.querySelector("[data-retry-render]"); if (retry) retry.onclick = function(){tab.view=null;tab.initialized=false;activateTab(tab.route);};
    }
  }
  function updateNavigation() { var backButton=document.getElementById("navigationBack"),canGoBack=navigationHistory.some(function(route){return route!==activeRoute&&screensByRoute[route]&&routeEnabled(screensByRoute[route]);});if(backButton)backButton.disabled=!canGoBack;sidebarNavigation.querySelectorAll("[data-route]").forEach(function (button) { button.classList.toggle("active", button.getAttribute("data-route") === activeRoute); }); sidebarNavigation.querySelectorAll("[data-module-toggle]").forEach(function(button){button.classList.remove("active");}); var screen = screensByRoute[activeRoute]; if (screen) { var group = Array.prototype.find.call(sidebarNavigation.querySelectorAll(".sidebar-group"), function (item) { return item.querySelector('[data-route="' + activeRoute + '"]'); }); if (group) { group.classList.add("open"); var toggle=group.querySelector("[data-module-toggle]"); if(toggle)toggle.classList.add("active"); } } updateQuickbarActive(); if (sidebarFlyout) sidebarFlyout.update(); }
  function setupSearch() { var input = document.getElementById("globalSearch"), results = document.getElementById("globalSearchResults"); input.addEventListener("input", function () { var needle = U.normalize(input.value); if (!needle) { results.hidden = true; return; } var matches = registry.screens.filter(function (screen) { return !screen.navigationHidden && routeEnabled(screen) && U.normalize(screen.title + " " + screen.module + " " + screen.description).indexOf(needle) >= 0; }).slice(0, 10); results.innerHTML = matches.length ? matches.map(function (screen) { return '<button class="search-result" type="button" data-search-route="' + U.escape(screen.route) + '"><span>' + U.escape(screen.title) + '</span><small class="muted">' + U.escape(screen.module) + '</small></button>'; }).join("") : '<div class="empty-state"><strong>لا توجد شاشة مطابقة</strong></div>'; results.hidden = false; }); results.onclick = function (event) { var result = event.target.closest("[data-search-route]"); if (result) { navigate(result.getAttribute("data-search-route")); input.value = ""; results.hidden = true; } }; document.addEventListener("click", function (event) { if (!event.target.closest(".global-search")) results.hidden = true; }); }
  function setupGlobalShortcuts() { function touchElement(selector) { return activeRoute === "pos" ? workspace.querySelector(".touch-pos " + selector) : null; } function clickTouch(selector) { var button = touchElement(selector); if (button && !button.disabled) button.click(); } function focusTouch(selector, open) { var element = touchElement(selector); if (element && !element.disabled) { element.focus(); if (open && element.click) element.click(); } } window.AlKhayalKeyboardShortcuts.install({ navigate: navigate, openShift: function () { var button = document.getElementById("headerShift"); if (button && !button.disabled) button.click(); }, focusSearch: function () { var input = document.getElementById("globalSearch"); if (input) input.focus(); }, showHelp: function (body) { var layer = document.getElementById("modalLayer"); if (!layer || !layer.hidden) return; UI.modal({ title: "اختصارات لوحة المفاتيح", body: body, compact: true }); }, getModalLayer: function () { return document.getElementById("modalLayer"); }, isTouchPos: function () { return activeRoute === "pos"; }, captureTouchBarcode: function (event) { return window.AlKhayalTouchPosScanner && window.AlKhayalTouchPosScanner.capture ? window.AlKhayalTouchPosScanner.capture(event) : false; }, focusTouchSearch: function () { focusTouch("[data-search]"); }, focusTouchCustomer: function () { focusTouch("[data-customer-search]", true); }, newTouchInvoice: function () { clickTouch("[data-new-touch-invoice]"); }, postTouchSale: function () { clickTouch("[data-post]"); }, focusTouchPayment: function () { focusTouch("[data-payment]", true); } }); }
  function setupShell() { sidebarFlyout = window.AlKhayalSidebarFlyout({ navigation: sidebarNavigation, shell: shell, navigate: navigateFromSidebar, activeRoute: function () { return activeRoute; } }); if(window.AlKhayalWorkforce)window.AlKhayalWorkforce.header({user:currentUser,state:{},navigate:navigate}); var collapsed = localStorage.getItem("alkhayal_sidebar_collapsed") === "true", toggle = document.getElementById("sidebarToggle"), settingsButton=document.querySelector('.sidebar-footer [data-route="settings-program"]'); if(settingsButton)settingsButton.hidden=!routeEnabled(screensByRoute["settings-program"]);shell.classList.toggle("sidebar-collapsed", collapsed); toggle.setAttribute("aria-expanded", String(!collapsed)); toggle.setAttribute("aria-label", collapsed ? "توسيع القائمة الجانبية" : "طي القائمة الجانبية"); toggle.title = collapsed ? "توسيع القائمة الجانبية" : "طي القائمة الجانبية"; toggle.onclick = function () { sidebarFlyout.close(false); var next = !shell.classList.contains("sidebar-collapsed"); shell.classList.toggle("sidebar-collapsed", next); localStorage.setItem("alkhayal_sidebar_collapsed", String(next)); toggle.setAttribute("aria-expanded", String(!next)); toggle.setAttribute("aria-label", next ? "توسيع القائمة الجانبية" : "طي القائمة الجانبية"); toggle.title = next ? "توسيع القائمة الجانبية" : "طي القائمة الجانبية"; sidebarFlyout.update(); }; document.querySelector(".sidebar-footer").addEventListener("click", function (event) { var route = event.target.closest("[data-route]"); if (route) navigateFromSidebar(route.getAttribute("data-route")); }); document.getElementById("logoutButton").onclick = async function () { try { await AlKhayalApi.request("/api/auth/logout", { method: "POST", body: "{}" }); } catch (error) { if (error.code !== "invalid_csrf" && error.status !== 401) { UI.toast(error.message, "danger"); return; } } AlKhayalApi.clearSession(); location.replace("/login.html"); }; var updateClock = function () { document.getElementById("topbarClock").textContent = new Intl.DateTimeFormat("ar-EG-u-nu-latn", { weekday: "short", hour: "2-digit", minute: "2-digit" }).format(new Date()); }; updateClock(); setInterval(updateClock, 60000); setupSearch(); setupGlobalShortcuts(); }
  function reconcileAccess(message) {
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
    buildSidebar(); buildQuickbar(); renderTabs();
    var reportTab = tabs.find(function (tab) { return tab.route === "report-overview"; });
    if (reportTab && reportTab.view && typeof reportTab.view.refreshReportsCenter === "function") reportTab.view.refreshReportsCenter();
    if (removedActive || !activeRoute) renderActive();
    updateNavigation();
  }
  window.addEventListener("alkhayal:features-changed",function(){if(!registry||!navigationReady)return;Access.load({notify:false}).then(function(){reconcileAccess("أُغلقت شاشة لم تعد متاحة.");}).catch(function(error){if(error.status===401)location.replace("/login.html");});});
  window.addEventListener("alkhayal:access-changed",function(){if(registry&&navigationReady)reconcileAccess("تم تحديث صلاحيات الشاشات.");});
  document.addEventListener("click", function (event) { if (!event.target.closest("#quickbarMore")) closeQuickbarMenu(false); });
  window.addEventListener("focus",function(){if(registry)Access.load();});
  Promise.all([loadRegistry(),authenticate(),Features.load(),Access.load({notify:false}),Settings.load({notify:false})]).then(function(){buildSidebar();buildQuickbar();setupShell();var initial=HOME_ROUTE;if(!routeEnabled(screensByRoute[initial]))initial=enabledRoutes(registry.screens.filter(function(item){return !item.navigationHidden;}).map(function(item){return item.route;}))[0];if(initial)navigate(initial);else renderActive();renderTabs();navigationReady=true;}).catch(function(error){if(error.message!=="unauthorized"){console.error(error);workspace.innerHTML='<div class="empty-state"><strong>تعذر بدء التطبيق</strong><span>تحقق من تشغيل الخادم ثم أعد فتح التطبيق.</span></div>';}});
}());
