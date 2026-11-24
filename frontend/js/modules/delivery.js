(function (g) {
  "use strict";
  var R = g.AlKhayalRenderers, U = g.AlKhayalUtils, UI = g.AlKhayalUI, A = g.AlKhayalApi;
  var statuses = { pending: "بانتظار التوصيل", out_for_delivery: "خرج للتوصيل", delivered: "تم التسليم", failed: "فشل التوصيل" };
  var statusStyles = { pending: "warning", out_for_delivery: "info", delivered: "success", failed: "danger" };
  function esc(value) { return U.escape(value == null ? "" : String(value)); }
  function errorMessage(error) { return esc(error && error.message || "تعذر تحميل البيانات. أعد المحاولة."); }

  function drivers(screen, container, context) {
    var selectedDriver = context && context.state && context.state.globalSearchDriverId;
    var query = "", rows = [], page = 1, pageSize = 25, loading = false, error = "", token = 0;
    function load() {
      var current = ++token;
      loading = true; error = ""; draw();
      A.request("/api/delivery/drivers?search=" + encodeURIComponent(query) + (selectedDriver ? "&id=" + selectedDriver + "&limit=1" : "")).then(function (result) {
        if (current !== token) return;
        rows = Array.isArray(result) ? result : [];
        if (selectedDriver && !rows.some(function (row) { return row.id === selectedDriver; })) error = "لا يمكن فتح هذا السجل";
        page = Math.min(page, Math.max(1, Math.ceil(rows.length / pageSize)));
        loading = false; draw();
      }).catch(function (failure) {
        if (current !== token) return;
        loading = false; error = errorMessage(failure); draw();
      });
    }
    function openEditor(driver) {
      driver = driver || { id: 0, isActive: true };
      var saving = false;
      UI.modal({
        title: driver.id ? "تعديل طيار" : "إضافة طيار", compact: true, focusSelector: "[data-name]",
        body: '<div class="driver-editor"><div class="field"><label for="deliveryDriverName">اسم الطيار</label><input id="deliveryDriverName" class="input" data-name maxlength="160" value="' + esc(driver.name) + '"></div>' +
          '<div class="field"><label for="deliveryDriverPhone">رقم الهاتف</label><input id="deliveryDriverPhone" class="input" data-phone maxlength="40" inputmode="tel" value="' + esc(driver.phone) + '"></div>' +
          '<label class="driver-active"><input type="checkbox" data-active ' + (driver.isActive ? "checked" : "") + '> <span>نشط</span></label><div class="field-error" data-error role="alert"></div></div>',
        actions: '<button type="button" class="button primary" data-save>حفظ</button><button type="button" class="button" data-modal-close>إلغاء</button>',
        beforeClose: function () { return !saving; },
        onOpen: function (modal) {
          var save = modal.querySelector("[data-save]"), message = modal.querySelector("[data-error]");
          save.onclick = function () {
            if (saving) return;
            var name = modal.querySelector("[data-name]").value.trim(), phone = modal.querySelector("[data-phone]").value.trim();
            if (!name || !phone || phone.length < 3) { message.textContent = "أدخل اسم الطيار ورقم هاتف من 3 أحرف على الأقل."; modal.querySelector("[data-name]").setAttribute("aria-invalid", String(!name)); modal.querySelector("[data-phone]").setAttribute("aria-invalid", String(!phone || phone.length < 3)); return; }
            var body = JSON.stringify({ id: driver.id || 0, name: name, phone: phone, isActive: modal.querySelector("[data-active]").checked });
            function setSaving(value) {
              saving = value;
              modal.setAttribute("aria-busy", String(value));
              Array.prototype.forEach.call(modal.querySelectorAll("input, button"), function (control) { control.disabled = value; });
              save.textContent = value ? "جارٍ الحفظ…" : "حفظ";
            }
            setSaving(true); message.textContent = ""; modal.querySelector("[data-name]").removeAttribute("aria-invalid"); modal.querySelector("[data-phone]").removeAttribute("aria-invalid");
            A.request("/api/delivery/drivers", { method: "POST", body: body }).then(function () {
              if (modal.isConnected !== false) UI.closeModal();
              load();
            }, function (failure) { setSaving(false); message.textContent = failure.message || "تعذر حفظ الطيار."; save.focus(); });
          };
        }
      });
    }
    function draw() {
      var pages = Math.max(1, Math.ceil(rows.length / pageSize)), visible = rows.slice((page - 1) * pageSize, page * pageSize);
      container.innerHTML = UI.pageHeader(screen, '<button type="button" class="button primary" data-add>إضافة طيار</button>') +
        '<section class="card driver-management"><div class="card-head driver-toolbar"><input class="input" data-search aria-label="بحث الطيارين" placeholder="بحث بالاسم أو الهاتف" value="' + esc(query) + '"></div>' +
        (loading ? '<div class="empty-state">جارٍ تحميل الطيارين...</div>' : error ? '<div class="empty-state" role="alert">' + error + '<br><button type="button" class="button" data-retry>إعادة المحاولة</button></div>' :
        !rows.length ? '<div class="empty-state">لا يوجد طيارون' + (query ? " مطابقون للبحث" : "") + '.</div>' :
        '<div class="table-wrap"><table class="data-table driver-management-table"><thead><tr><th>الاسم</th><th>الهاتف</th><th>الحالة</th><th>الإجراء</th></tr></thead><tbody>' +
        visible.map(function (driver) { return '<tr><td class="driver-name">' + esc(driver.name) + '</td><td class="driver-phone"><bdi>' + esc(driver.phone) + '</bdi></td><td><span class="badge ' + (driver.isActive ? 'success' : 'neutral') + '">' + (driver.isActive ? "نشط" : "موقوف") + '</span></td><td><div class="driver-row-actions"><button type="button" class="button" data-edit="' + driver.id + '">تعديل</button><button type="button" class="button" data-orders="' + driver.id + '">طلبات التوصيل</button></div></td></tr>'; }).join("") + '</tbody></table></div>' +
        '<div class="card-head driver-pagination"><span>إجمالي النتائج: ' + rows.length + ' — صفحة ' + page + ' من ' + pages + '</span><div class="driver-pagination-actions"><button type="button" class="button" data-prev ' + (page <= 1 ? "disabled" : "") + '>السابق</button><button type="button" class="button" data-next ' + (page >= pages ? "disabled" : "") + '>التالي</button></div></div>') + '</section>';
      container.querySelector("[data-search]").oninput = U.debounce(function () { selectedDriver = null; query = this.value; page = 1; load(); }, 180);
      container.onclick = function (event) {
        var target = event.target, edit = target.closest("[data-edit]"), orders = target.closest("[data-orders]");
        if (target.closest("[data-add]")) openEditor();
        else if (target.closest("[data-retry]")) load();
        else if (target.closest("[data-prev]") && page > 1) { page--; draw(); }
        else if (target.closest("[data-next]") && page < pages) { page++; draw(); }
        else if (edit) openEditor(rows.filter(function (item) { return String(item.id) === edit.getAttribute("data-edit"); })[0]);
        else if (orders && context && context.navigate) context.navigate("delivery-orders", { driverId: Number(orders.getAttribute("data-orders")) });
      };
    }
    load();
  }

  function orders(screen, container, context) {
    var query = "", status = "", driverId = context && context.state && context.state.driverId != null ? String(context.state.driverId) : "", driverRows = [], data = { items: [], total: 0, page: 1, pageSize: 25 }, token = 0, busy = false, error = "";
    var selectedId = context && context.state && context.state.globalSearchDeliveryId;
    if (selectedId) query = context.state.globalSearchDeliveryNumber;
    function options() { return '<option value="">كل الطيارين</option>' + driverRows.map(function (driver) { return '<option value="' + driver.id + '"' + (String(driverId) === String(driver.id) ? " selected" : "") + '>' + esc(driver.name) + '</option>'; }).join(""); }
    function load(reset) {
      if (reset) { data.page = 1; data.items = []; data.total = 0; }
      var current = ++token;
      busy = true; error = ""; draw();
      A.request("/api/delivery/orders?search=" + encodeURIComponent(selectedId ? "" : query) + (selectedId ? "&invoiceId=" + selectedId : "") + "&status=" + encodeURIComponent(status) + "&driverId=" + encodeURIComponent(driverId) + "&page=" + data.page + "&pageSize=" + data.pageSize).then(function (result) {
        if (current !== token) return;
        data = result || { items: [], total: 0, page: 1, pageSize: data.pageSize };
        if (selectedId) {
          data.items = data.items.filter(function (item) { return item.salesInvoiceId === selectedId; });
          data.total = data.items.length;
          if (!data.total) error = "لا يمكن فتح هذا السجل";
        }
        busy = false; draw();
      }).catch(function (failure) { if (current !== token) return; busy = false; error = errorMessage(failure); draw(); });
    }
    function draw() {
      var pages = Math.max(1, Math.ceil((data.total || 0) / data.pageSize)), rows = data.items || [];
      container.innerHTML = UI.pageHeader(screen) + '<section class="card"><div class="card-head">' +
        '<input class="input" data-q placeholder="رقم الفاتورة أو العميل أو الهاتف" value="' + esc(query) + '" ' + (busy ? "disabled" : "") + '>' +
        '<select class="select" data-f ' + (busy ? "disabled" : "") + '><option value="">كل الحالات</option>' + Object.keys(statuses).map(function (key) { return '<option value="' + key + '"' + (status === key ? " selected" : "") + '>' + statuses[key] + '</option>'; }).join("") + '</select>' +
        '<select class="select" data-driver ' + (busy ? "disabled" : "") + '>' + options() + '</select><select class="select" data-size ' + (busy ? "disabled" : "") + '>' + [25, 50, 100].map(function (size) { return '<option value="' + size + '"' + (data.pageSize === size ? " selected" : "") + '>' + size + '</option>'; }).join("") + '</select></div>' +
        (error ? '<div class="empty-state" role="alert">تعذر تحميل طلبات التوصيل<br>' + error + '<br><button class="button" data-retry>إعادة المحاولة</button></div>' :
        '<div class="table-wrap"><table class="data-table delivery-orders-table"><thead><tr><th>الفاتورة</th><th>العميل</th><th>العنوان</th><th>المندوب</th><th>الحالة</th><th>الإجمالي</th><th>الإجراءات</th></tr></thead><tbody>' +
        rows.map(function (order) { var tone = statusStyles[order.status] || "neutral", label = statuses[order.status] || order.status || "حالة غير معروفة", id=Number(order.salesInvoiceId); return '<tr class="delivery-order delivery-order--' + tone + '"><td><bdi>' + esc(order.invoiceNumber) + '</bdi></td><td>' + esc(order.customerName) + '<br><small><bdi>' + esc(order.primaryPhone) + '</bdi></small></td><td><span class="delivery-address" title="' + esc(order.address) + '" aria-label="' + esc(order.address) + '">' + esc(order.address) + '</span></td><td>' + esc(order.driverName||"تعيين لاحقًا") + '</td><td><span class="badge delivery-status-badge ' + (tone === "neutral" ? "" : tone) + '" aria-label="حالة التوصيل: ' + esc(label) + '">' + esc(label) + '</span></td><td>' + U.money(order.totalScaled / 10000) + '</td><td><div class="delivery-row-actions"><button class="button" type="button" data-view-invoice="' + id + '" aria-label="عرض فاتورة ' + esc(order.invoiceNumber) + '">عرض</button><button class="button" type="button" data-print-invoice="' + id + '" aria-label="طباعة الفاتورة ' + esc(order.invoiceNumber) + '">فاتورة</button><button class="button" type="button" data-print-delivery="' + id + '" aria-label="طباعة بون التوصيل ' + esc(order.invoiceNumber) + '">بون</button><select class="select" data-s="' + id + '" aria-label="تحديث حالة التوصيل للفاتورة ' + esc(order.invoiceNumber) + '" ' + (busy ? "disabled" : "") + '><option value="">الحالة</option>' + Object.keys(statuses).filter(function (key) { return key !== order.status; }).map(function (key) { return '<option value="' + key + '">' + statuses[key] + '</option>'; }).join("") + '</select></div></td></tr>'; }).join("") + '</tbody></table></div>' +
        (busy ? '<div class="empty-state">جارٍ تحميل طلبات التوصيل...</div>' : !rows.length ? '<div class="empty-state">لا توجد طلبات توصيل مطابقة.</div>' : "") +
        '<div class="card-head"><span>إجمالي النتائج: ' + (data.total || 0) + ' — صفحة ' + data.page + ' من ' + pages + '</span><button class="button" data-prev ' + (busy || data.page <= 1 ? "disabled" : "") + '>السابق</button><button class="button" data-next ' + (busy || data.page >= pages ? "disabled" : "") + '>التالي</button></div>') + '</section>';
      var input = container.querySelector("[data-q]");
      if (input) input.oninput = U.debounce(function () { selectedId = null; query = this.value; load(true); }, 180);
      container.onchange = function (event) {
        var target = event.target;
        if (target.hasAttribute("data-f")) { status = target.value; load(true); }
        else if (target.hasAttribute("data-driver")) { driverId = target.value; load(true); }
        else if (target.hasAttribute("data-size")) { data.pageSize = Number(target.value); load(true); }
        else {
          var id = target.getAttribute("data-s");
          if (id && target.value && !busy) {
            busy = true; A.request("/api/delivery/orders/" + id + "/status", { method: "POST", body: JSON.stringify({ status: target.value }) }).then(function () { load(false); }).catch(function (failure) { busy = false; UI.toast(failure.message || "تعذر تحديث الحالة.", "danger"); draw(); });
          }
        }
      };
      container.onclick = function (event) {
        var invoice = event.target.closest("[data-print-invoice]"), ticket = event.target.closest("[data-print-delivery]"), view = event.target.closest("[data-view-invoice]");
        if (invoice && g.AlKhayalPrinting) { g.AlKhayalPrinting.submit("sale", Number(invoice.getAttribute("data-print-invoice")), false, invoice); return; }
        if (ticket && g.AlKhayalPrinting) { g.AlKhayalPrinting.submit("delivery", Number(ticket.getAttribute("data-print-delivery")), false, ticket); return; }
        if (view && context) { context.navigate("sales-management", { saleInvoiceId: Number(view.getAttribute("data-view-invoice")) }); return; }
        if (event.target.closest("[data-retry]")) load(false);
        else if (event.target.closest("[data-prev]") && data.page > 1) { data.page--; load(false); }
        else if (event.target.closest("[data-next]") && data.page < pages) { data.page++; load(false); }
      };
    }
    A.request("/api/delivery/drivers").then(function (result) { driverRows = result || []; }).catch(function () { driverRows = []; }).then(function () { load(true); });
  }
  R["drivers-management"] = drivers;
  R["delivery-orders"] = orders;
}(window));
