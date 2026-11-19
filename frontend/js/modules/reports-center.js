(function (global) {
  "use strict";
  var U = global.AlKhayalUtils;
  var metricLabels = { Sales: "إجمالي المبيعات المنشورة", NetProfit: "صافي ربح المبيعات التشغيلي", Purchases: "إجمالي المشتريات المنشورة", Returns: "مرتجعات المبيعات", Cash: "مقبوضات نقدية", Wallet: "مقبوضات المحافظ", Card: "مقبوضات البطاقات", Credit: "مبيعات آجلة", Unclassified: "وسائل دفع غير مصنفة", customers: "مديونيات العملاء حاليًا", suppliers: "مستحقات الموردين حاليًا", maintenance: "تذاكر الصيانة المفتوحة حاليًا", installments: "أقساط متأخرة حاليًا" };
  var primaryKeys = ["Sales", "NetProfit", "Purchases", "Returns"];
  var categoryIcons = { "المبيعات": "M4 19V5M4 19h16M8 15v-4M12 15V8M16 15v-6", "المشتريات": "M5 7h14l-1 13H6L5 7ZM8 7a4 4 0 0 1 8 0", "الأصناف والمخزون": "M3 8h18v13H3zM6 8V5h12v3M7 12h10M7 16h6", "العملاء": "M16 20v-2a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4v2M9.5 10a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z", "الموردون": "M3 6h11v12H3zM14 10h4l3 3v5h-7M6 18a2 2 0 1 0 0-4 2 2 0 0 0 0 4M17 18a2 2 0 1 0 0-4 2 2 0 0 0 0 4", "الخزنة والبنك": "M3 20h18M5 20V9h14v11M3 9l9-6 9 6M9 13h2M13 13h2", "المصروفات": "M5 3h14v18H5zM8 7h8M8 11h8M8 15h5", "المرتجعات": "M4 7h10a5 5 0 1 1-1 10H8M8 4 4 7l4 3", "الموظفون": "M16 20v-2a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4v2M9.5 10a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z", "الوحدات الإضافية": "M14 4a5 5 0 0 0-6 6l-5 5 4 4 5-5a5 5 0 0 0 6-6l-3 3-4-4 3-3Z" };
  function categoryIcon(name) { return '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="' + (categoryIcons[name] || categoryIcons["المبيعات"]) + '"></path></svg>'; }
  function esc(value) { return U.escape(value == null ? "" : String(value)); }
  function localDate(date) { return date.getFullYear() + "-" + String(date.getMonth() + 1).padStart(2, "0") + "-" + String(date.getDate()).padStart(2, "0"); }
  function dates(period) {
    var now = new Date(), start = new Date(now.getFullYear(), now.getMonth(), now.getDate()), end = new Date(start);
    if (period === "yesterday") { start.setDate(start.getDate() - 1); end = new Date(start); }
    if (period === "week") start.setDate(start.getDate() - 6);
    if (period === "thirty") start.setDate(start.getDate() - 29);
    if (period === "month") start.setDate(1);
    return { from: localDate(start), to: localDate(end) };
  }
  function reports(context) {
    return context.registry.screens.filter(function (screen) { return screen.route.indexOf("report-") === 0 && screen.route !== "report-overview" && screen.reportCategory && !screen.redirectTo && context.canNavigate(screen.route); });
  }
  function filterReports(available, search, category) { var term = U.normalize(search || ""); return available.filter(function (screen) { return (!category || screen.reportCategory === category) && (!term || U.normalize(screen.title + " " + screen.description + " " + screen.reportCategory + " " + (screen.reportKeywords || "")).indexOf(term) >= 0); }); }
  function partitionMetrics(metrics) { return { primary: primaryKeys.map(function (key) { return metrics.find(function (metric) { return metric.key === key; }); }).filter(Boolean), secondary: metrics.filter(function (metric) { return primaryKeys.indexOf(metric.key) < 0; }) }; }
  function money(value) { return U.money(value); }
  function metricValue(metric) {
    if (!metric.complete || metric.value == null) return "غير مكتمل — راجع التقرير";
    if (metric.key === "maintenance") return U.number(metric.value);
    return money(metric.value);
  }
  function metricCard(metric, tier) {
    return '<button type="button" class="reports-kpi reports-kpi-' + tier + '" data-open-report="' + esc(metric.route) + '"><span class="reports-kpi-label">' + esc(metricLabels[metric.key] || metric.key) + '</span><strong>' + esc(metricValue(metric)) + '</strong>' + (metric.key === 'NetProfit' && Number(metric.estimatedCost) > 0 ? '<small>يتضمن تكلفة تقديرية</small>' : '') + '<span class="reports-kpi-action" aria-hidden="true">←</span></button>';
  }
  function metricMarkup(metrics) {
    var groups = partitionMetrics(metrics);
    return (groups.primary.length ? '<div class="reports-kpi-group"><h3>المؤشرات الرئيسية</h3><div class="reports-kpis-primary">' + groups.primary.map(function (metric) { return metricCard(metric, "primary"); }).join("") + '</div></div>' : '') +
      (groups.secondary.length ? '<div class="reports-kpi-group reports-kpi-group-secondary"><h3>مؤشرات إضافية</h3><div class="reports-kpis-secondary">' + groups.secondary.map(function (metric) { return metricCard(metric, "secondary"); }).join("") + '</div></div>' : '');
  }
  function renderDirectory(host, context) {
    var field = host.querySelector("[data-report-search]"), select = host.querySelector("[data-report-category]"), target = host.querySelector("[data-report-directory]");
    var available = reports(context), categories = [];
    available.forEach(function (screen) { if (categories.indexOf(screen.reportCategory) < 0) categories.push(screen.reportCategory); });
    var selected = select.value;
    select.innerHTML = '<option value="">كل التصنيفات</option>' + categories.map(function (name) { return '<option value="' + esc(name) + '">' + esc(name) + '</option>'; }).join("");
    if (categories.indexOf(selected) >= 0) select.value = selected;
    var filtered = filterReports(available, field.value, select.value);
    if (!available.length) { target.innerHTML = '<div class="reports-empty" role="status">لا توجد تقارير متاحة بصلاحيات هذا الحساب أو الوحدات المفعلة.</div>'; return; }
    if (!filtered.length) { target.innerHTML = '<div class="reports-empty" role="status">لا توجد تقارير تطابق البحث.</div>'; return; }
    target.innerHTML = categories.filter(function (category) { return filtered.some(function (screen) { return screen.reportCategory === category; }); }).map(function (category) {
      var items = filtered.filter(function (screen) { return screen.reportCategory === category; });
      return '<section class="reports-category"><div class="reports-category-head"><span class="reports-category-icon">' + categoryIcon(category) + '</span><h3>' + esc(category) + '</h3><small class="reports-count">' + U.number(items.length) + '</small></div><div class="reports-links">' + items.map(function (screen) { return '<button type="button" class="reports-link" data-open-report="' + esc(screen.route) + '"><span>' + esc(screen.title) + '</span><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M19 12H5m6-6-6 6 6 6"></path></svg></button>'; }).join("") + '</div></section>';
    }).join("");
  }
  global.AlKhayalRenderers["reports-center"] = function (screen, host, context) {
    var token = 0;
    host.innerHTML = '<main class="reports-center"><header class="reports-hero"><div class="reports-hero-mark" aria-hidden="true">▥</div><div><span class="reports-eyebrow">مركز التقارير</span><h1>التقارير والإحصائيات</h1><p>نظرة شاملة على أداء نشاطك التجاري</p></div></header><section class="reports-overview" aria-labelledby="reports-overview-title"><div class="reports-section-head"><div><h2 id="reports-overview-title">نظرة عامة</h2><p>أرقام من التقارير المحفوظة حسب الفترة المحددة</p></div><button type="button" class="button" data-report-refresh>تحديث</button></div><form class="reports-period"><label>الفترة<select class="select" name="period"><option value="today">اليوم</option><option value="yesterday">أمس</option><option value="week">آخر 7 أيام</option><option value="thirty">آخر 30 يوم</option><option value="month">هذا الشهر</option><option value="custom">فترة مخصصة</option></select></label><label>من<input class="input" name="from" type="date" required></label><label>إلى<input class="input" name="to" type="date" required></label><button class="button primary" type="submit">عرض</button></form><div data-report-metrics aria-live="polite"></div></section><section class="reports-directory" aria-labelledby="reports-directory-title"><div class="reports-section-head"><div><h2 id="reports-directory-title">استعرض التقارير</h2><p>افتح التقرير مباشرة في مساحة العمل</p></div></div><div class="reports-toolbar"><label class="sr-only" for="reportsSearch">ابحث عن تقرير</label><input id="reportsSearch" class="input" type="search" data-report-search placeholder="ابحث عن تقرير..." autocomplete="off"><label class="sr-only" for="reportsCategory">تصنيف التقارير</label><select id="reportsCategory" class="select" data-report-category></select></div><div class="reports-grid" data-report-directory></div></section></main>';
    var form = host.querySelector(".reports-period"), metrics = host.querySelector("[data-report-metrics]");
    function setDates() { if (form.elements.period.value !== "custom") { var range = dates(form.elements.period.value); form.elements.from.value = range.from; form.elements.to.value = range.to; } }
    function refreshMetrics() {
      var id = ++token;
      if (!global.AlKhayalAccess.routeEnabled(screen)) { metrics.innerHTML = '<div class="reports-empty" role="status">إحصائيات الفترة تتطلب صلاحية ملخص النشاط. يمكنك فتح التقارير المصرح بها أدناه.</div>'; return; }
      if (!form.elements.from.value || !form.elements.to.value || form.elements.from.value > form.elements.to.value) { metrics.innerHTML = '<div class="field-error" role="alert">تحقق من تاريخ البداية والنهاية.</div>'; return; }
      metrics.innerHTML = '<div class="reports-empty" role="status">جارٍ تحميل الإحصائيات...</div>';
      var query = '?from=' + encodeURIComponent(form.elements.from.value) + '&to=' + encodeURIComponent(form.elements.to.value);
      global.AlKhayalApi.request('/api/reports/overview' + query).then(function (data) {
        if (id !== token || (context.isActive && !context.isActive())) return;
        var visible = data.filter(function (metric) { return context.canNavigate(metric.route); });
        metrics.innerHTML = visible.length ? metricMarkup(visible) + '<p class="reports-data-note">الأرصدة والحالات الموسومة «حاليًا» تعكس الوضع الحالي. صافي الربح يخص المبيعات بعد المصروفات المسجلة، ولا يشمل ربح الصيانة أو التقسيط المستقل.</p>' : '<div class="reports-empty" role="status">لا توجد مؤشرات متاحة بصلاحياتك خلال هذه الفترة.</div>';
      }).catch(function (error) { if (id !== token || (context.isActive && !context.isActive())) return; metrics.innerHTML = '<div class="reports-empty" role="alert">تعذر تحميل الإحصائيات: ' + esc(error.message) + ' <button type="button" class="button" data-retry-metrics>إعادة المحاولة</button></div>'; });
    }
    setDates(); renderDirectory(host, context); refreshMetrics();
    form.elements.period.onchange = function () { setDates(); refreshMetrics(); };
    ["from", "to"].forEach(function (name) { form.elements[name].onchange = function () { form.elements.period.value = "custom"; }; });
    form.onsubmit = function (event) { event.preventDefault(); refreshMetrics(); };
    host.querySelector("[data-report-refresh]").onclick = refreshMetrics;
    host.querySelector("[data-report-search]").oninput = function () { renderDirectory(host, context); };
    host.querySelector("[data-report-category]").onchange = function () { renderDirectory(host, context); };
    host.onclick = function (event) { var link = event.target.closest("[data-open-report]"); if (link && context.canNavigate(link.getAttribute("data-open-report"))) context.navigate(link.getAttribute("data-open-report")); if (event.target.closest("[data-retry-metrics]")) refreshMetrics(); };
    host.refreshReportsCenter = function () { renderDirectory(host, context); if (context.isActive && context.isActive()) refreshMetrics(); };
  };
  global.AlKhayalReportsCenter = { dates: dates, reports: reports, filterReports: filterReports, partitionMetrics: partitionMetrics, metricMarkup: metricMarkup };
}(window));

