(function (global) {
  "use strict";
  var R = global.AlKhayalRenderers, U = global.AlKhayalUtils, UI = global.AlKhayalUI, Api = global.AlKhayalApi, L = global.AlKhayalShiftLocalization;
  var base = "/api/workforce/";
  function esc(x) { return U.escape(x == null ? "" : x); }
  function key() { var b = new Uint8Array(16); global.crypto.getRandomValues(b); return Array.from(b).map(function (v) { return ("0" + v.toString(16)).slice(-2); }).join(""); }
  function active(c) { return !c.isActive || c.isActive(); }
  var timeFormat = { format: function (value) { return U.dateTime ? U.dateTime(value) : String(value == null ? "" : value); } };
  function time(x) { return x ? timeFormat.format(new Date(x)) : "—"; }
  function discrepancy(s) { return s.difference == null ? "—" : (s.difference === "0.00" ? "مطابق" : s.difference.charAt(0) === "-" ? "عجز" : "زيادة") + " — " + s.difference; }
  function enabled(feature) { return global.AlKhayalFeatures && global.AlKhayalFeatures.isEnabled(feature); }
  function money(value, optional, positive) {
    value = String(value == null ? '' : value).trim();
    if (optional && !value) return null;
    if (!/^\d+(?:\.\d{1,2})?$/.test(value) || positive && !/[1-9]/.test(value)) throw new Error('أدخل مبلغًا ' + (positive ? 'أكبر من صفر' : 'غير سالب') + ' بدقة خانتين عشريتين بحد أقصى.');
    return value;
  }
  function latinDigits(value) { return String(value == null ? '' : value).replace(/[٠-٩۰-۹]/g, function (digit) { var code = digit.charCodeAt(0); return String(code >= 1776 ? code - 1776 : code - 1632); }); }
  function field(name, label, value, type, required) { return UI.field({ id: name, label: label, type: type || "text", required: !!required }, value); }
  function select(name, label, value, options) { return UI.field({ id: name, label: label, type: "select", options: options }, value); }
  function yesNo(name, label, value) { return select(name, label, value ? "1" : "0", [{ value: "0", label: "لا" }, { value: "1", label: "نعم" }]); }
  function table(headers, rows) { return '<div class="table-wrap"><table class="data-table"><thead><tr>' + headers.map(function (h) { return '<th>' + esc(h) + '</th>'; }).join("") + '</tr></thead><tbody>' + (rows.join("") || '<tr><td colspan="' + headers.length + '"><div class="empty-state">لا توجد سجلات مطابقة.</div></td></tr>') + '</tbody></table></div>'; }
  function pager(page) { return '<div class="workforce-actions"><button class="button" data-page="' + (page.page - 1) + '"' + (page.page <= 1 ? ' disabled' : '') + '>السابق</button><span>صفحة ' + page.page + ' — ' + page.total + ' سجل</span><button class="button" data-page="' + (page.page + 1) + '"' + (page.page * page.pageSize >= page.total ? ' disabled' : '') + '>التالي</button></div>'; }
  function qs(q) { return Object.keys(q).filter(function (k) { return q[k] !== "" && q[k] != null; }).map(function (k) { return encodeURIComponent(k) + '=' + encodeURIComponent(q[k]); }).join('&'); }
  function showError(node, e) { node.hidden = false; node.textContent = e.message || String(e); }

  // Keep the command and key after an uncertain response. Retrying never creates a second event.
  function editModal(context, slot, title, body, build, path, saved, initial, saveLabel) {
    var drafts = context.state.workforceDrafts || (context.state.workforceDrafts = {}), draft = drafts[slot] || (drafts[slot] = { values: initial || {} });
    UI.modal({ title: title, wide: true, body: '<form data-workforce-form class="form-grid">' + body(draft.values) + '<p class="field-error span-2" data-error role="alert" hidden></p></form>',
      actions: '<button class="button primary" data-save type="button">حفظ</button><button class="button" data-modal-close type="button">إلغاء</button>',
      beforeClose: function () { return !draft.busy && !draft.pending; },
      onOpen: function (modal) {
        var form = modal.querySelector('[data-workforce-form]'), button = modal.querySelector('[data-save]'), error = modal.querySelector('[data-error]');
        function capture() {
          Array.from(form.elements).forEach(function(n){if(n.name)draft.values[n.name]=n.type==='checkbox'?n.checked:n.value;});
          var preview=form.querySelector('[data-close-difference]');
          if(preview){var value=String(draft.values.amount||'');if(/^\d+(?:\.\d{1,2})?$/.test(value)){var scaled=BigInt(value.split('.')[0]+((value.split('.')[1]||'')+'00').slice(0,2))*100n;var diff=scaled-BigInt(preview.getAttribute('data-expected'));var absolute=diff<0n?-diff:diff;preview.textContent='الفرق (زيادة / عجز): '+(diff<0n?'-':'')+(absolute/10000n).toString()+'.'+((absolute%10000n)/100n).toString().padStart(2,'0')+' ج.م';}else preview.textContent='أدخل النقد المعدود لحساب الفرق.';}
        }
        function lock() { form.querySelectorAll('input,select,textarea').forEach(function (n) { n.disabled = !!(draft.busy || draft.pending); }); button.disabled = !!draft.busy; button.textContent = draft.busy ? "جارٍ الحفظ…" : draft.pending ? "إعادة المحاولة للتحقق من الحفظ" : (saveLabel || "حفظ"); }
        form.oninput = capture; form.onchange = capture;
        function submit(e) {
          if (e) e.preventDefault(); if (draft.busy) return;
          if (!draft.pending && !form.reportValidity()) return;
          capture();
          try { draft.pending = draft.pending || Object.assign(build(draft.values), { operationKey: key() }); }
          catch (reason) { showError(error, reason); return; }
          draft.busy = true; lock(); error.hidden = true;
          Api.request(base + path, { method: "POST", body: JSON.stringify(draft.pending) }).then(function (result) {
            draft.busy = false; draft.pending = null; delete drafts[slot]; if(path.indexOf('shifts/')===0)refresh().catch(function(e){UI.toast(e.message,'danger');});
            if (!active(context) || !modal.isConnected) return;
            UI.closeModal(); UI.toast(saveLabel === 'أرشفة الموظف' ? "تمت أرشفة الموظف مع الاحتفاظ بسجلاته." : "تم الحفظ.", "success"); saved(result);
          }).catch(function (reason) {
            draft.busy = false;
            if (reason.status >= 400 && reason.status < 500) draft.pending = null;
            if (!active(context) || !modal.isConnected) return;
            lock(); showError(error, reason);
          });
        }
        button.onclick = submit; form.onsubmit = submit; lock();
        if (draft.pending && !draft.busy) showError(error, "النتيجة غير مؤكدة. أعد المحاولة بنفس البيانات للتحقق من الحفظ.");
      }
    });
  }
  function pageHost(screen, container, extra, filters) {
    container.innerHTML = UI.pageHeader(screen, extra || "") + '<section class="card"><div class="card-body"><form class="workforce-filters" data-filters>' + filters + '<button type="submit" class="button">تحديث</button></form></div><div data-results aria-live="polite"></div></section>';
    return container.querySelector('[data-results]');
  }
  function loader(context, host, request, draw) {
    var sequence = 0;
    return function () {
      var token = ++sequence;
      host.setAttribute('aria-busy', 'true');
      if (!host.innerHTML) host.innerHTML = '<div class="empty-state">جارٍ التحميل…</div>';
      request().then(function (data) { if (!active(context) || token !== sequence) return; host.setAttribute('aria-busy', 'false'); draw(data); }).catch(function (reason) {
        if (!active(context) || token !== sequence) return;
        host.setAttribute('aria-busy', 'false'); host.innerHTML = '<div class="empty-state" role="alert">' + esc(reason.message) + '</div>';
      });
    };
  }
  function employees(screen, container, context) {
    var q = context.state.employeeQuery || (context.state.employeeQuery = { search: "", status: "", page: 1 }), rows = [], users = [], autoAdd = screen.route === "employee-add";
    var host = pageHost(screen, container, '<button class="button primary" data-new>إضافة موظف جديد</button>', field('search', 'بحث بالاسم أو الهاتف أو الكود', q.search, 'search') + select('status', 'الحالة', q.status, [{ value: '', label: 'الكل' }, { value: 'active', label: 'نشط' }, { value: 'inactive', label: 'مؤرشف' }]));
    var load = loader(context, host, function () { return Api.request(base + 'employees?' + qs(q)); }, function (page) {
      rows = page.items;
      if (context.state.globalSearchEmployeeId) {
        rows = rows.filter(function (row) { return row.id === context.state.globalSearchEmployeeId; });
        context.state.globalSearchEmployeeId = null;
        if (!rows.length) { host.innerHTML = '<div class="empty-state" role="alert">لا يمكن فتح هذا السجل</div>'; return; }
      }
      host.innerHTML = table(['رقم الموظف', 'الموظف', 'الهاتف', 'الوظيفة', 'الحالة', ''], rows.map(function (e) {
        return '<tr><td>' + esc(e.code) + '</td><td>' + esc(e.name) + '</td><td>' + esc(e.phone) + '</td><td>' + esc(e.jobTitle) + '</td><td>' + (e.isActive ? 'نشط' : 'مؤرشف') + '</td><td><button class="button" data-employee="' + e.id + '">تعديل</button>' + (e.isActive ? '<button class="button" data-archive="' + e.id + '">مسح / أرشفة</button>' : '') + '</td></tr>';
      })) + pager(page);
      if (autoAdd) { autoAdd = false; edit(null); }
    });
    function edit(e) {
      var data = e ? Object.assign({}, e, { isActive: e.isActive ? '1' : '0', attendanceEnabled: e.attendanceEnabled ? '1' : '0' }) : { isActive: '1', attendanceEnabled: '0' };
      editModal(context, 'employee-' + (e ? e.id : 'new'), e ? 'بيانات ' + e.code : 'إضافة موظف', function (v) {
        return (e ? '<p class="muted span-2">رقم الموظف: ' + esc(e.code) + ' · تاريخ الإنشاء: ' + esc(time(e.createdUtc)) + '</p>' : '<p class="muted span-2">رقم الموظف يتولد تلقائيًا عند الحفظ.</p>') + field('name', 'اسم الموظف', v.name, 'text', true) + field('phone', 'الهاتف', v.phone, 'tel', true) + field('jobTitle', 'الوظيفة', v.jobTitle, 'text', true) + field('hireDate', 'تاريخ التعيين (اختياري)', v.hireDate, 'date') +
          field('address', 'العنوان', v.address, 'text', true) + field('nationalId', 'الرقم القومي', v.nationalId, 'text', true) + field('notes', 'ملاحظات (اختياري)', v.notes, 'textarea') + select('linkedUserId', 'حساب الدخول (اختياري)', v.linkedUserId || '', [{ value: '', label: 'دون حساب دخول' }].concat(users.map(function (u) { return { value: u.id, label: u.displayName + ' — ' + u.username }; }))) +
          yesNo('isActive', 'نشط', v.isActive === '1') + yesNo('attendanceEnabled', 'تتبع الحضور لهذا الموظف (اختياري)', v.attendanceEnabled === '1') + '<p class="muted span-2">البيانات التالية معلومات اختيارية للمدير، ولا تسجل صرف راتب أو خصمًا نقديًا.</p>' +
          field('salary', 'الراتب الشهري', v.salary) + select('salaryFrequency', 'دورية الراتب', v.salaryFrequency || 'monthly', [{ value: 'monthly', label: 'شهري' }, { value: 'weekly', label: 'أسبوعي' }, { value: 'other', label: 'أخرى' }]) + field('payday', 'يوم الدفع من الشهر 1–31 (اختياري)', v.payday, 'number') + field('compensationNotes', 'ملاحظات الراتب (اختياري)', v.compensationNotes, 'textarea');
      }, function (v) { var nationalId = latinDigits(v.nationalId).trim(); if (v.payday && (!/^\d+$/.test(v.payday) || Number(v.payday) < 1 || Number(v.payday) > 31)) throw new Error('يوم الدفع الاختياري يجب أن يكون من 1 إلى 31.'); if (!/^\d{14}$/.test(nationalId)) throw new Error('الرقم القومي يجب أن يتكون من 14 رقمًا.'); return { id: e ? e.id : 0, version: e ? e.version : 0, name: v.name, phone: v.phone, jobTitle: v.jobTitle, hireDate: v.hireDate, address: v.address, nationalId: nationalId, notes: v.notes, linkedUserId: v.linkedUserId ? Number(v.linkedUserId) : null, isActive: v.isActive === '1', attendanceEnabled: v.attendanceEnabled === '1', salary: money(v.salary, true), salaryFrequency: v.salaryFrequency, payday: v.payday ? Number(v.payday) : null, compensationNotes: v.compensationNotes }; }, 'employees/save', load, data);
    }
    container.querySelector('[data-new]').onclick = function () { edit(null); };
    wireFilters(container, q, load);
    host.onclick = function (event) { var a = event.target.closest('[data-archive]'), b = event.target.closest('[data-employee]'); if (a) { var row = rows.find(function (r) { return r.id === Number(a.getAttribute('data-archive')); }); editModal(context, 'employee-archive-' + row.id, 'أرشفة الموظف ' + row.name, function () { return '<p>سيتم إيقاف الموظف مع الاحتفاظ بسجلاته المالية والحضور.</p>'; }, function () { return { id: row.id, version: row.version }; }, 'employees/archive', load, null, 'أرشفة الموظف'); } else if (b) edit(rows.find(function (r) { return r.id === Number(b.getAttribute('data-employee')); })); else changePage(event, q, load); };
    Api.request('/api/users').then(function (value) { if (!active(context)) return; users = value; load(); }).catch(function (e) { showError(host, e); });
  }
  function wireFilters(container, q, load) {
    var form = container.querySelector('[data-filters]');
    function collect() {
      Array.from(form.elements).forEach(function (n) { if (!n.name) return; var value = n.value; if (n.name === 'fromUtc' || n.name === 'toUtc') value = value ? new Date(value + 'T00:00:00').toISOString() : ''; q[n.name] = value; }); q.page = 1;
    }
    form.onsubmit = function (e) { e.preventDefault(); collect(); load(); };
    var search = form.querySelector('[name="search"]'); if (search) search.oninput = U.debounce(function () { collect(); load(); }, 220);
    form.querySelectorAll('select,input[type="date"]').forEach(function (n) { n.onchange = function () { collect(); load(); }; });
  }
  function changePage(event, q, load) { var b = event.target.closest('[data-page]'); if (b && !b.disabled) { q.page = Number(b.getAttribute('data-page')); load(); } }
  function dateValue(utc) { if (!utc) return ''; var d = new Date(utc); return d.getFullYear() + '-' + ('0' + (d.getMonth() + 1)).slice(-2) + '-' + ('0' + d.getDate()).slice(-2); }
  function dates(q) { return field('fromUtc', 'من تاريخ', dateValue(q.fromUtc), 'date') + field('toUtc', 'إلى تاريخ (غير شامل)', dateValue(q.toUtc), 'date'); }
  function attendance(screen, container, context) {
    var q = context.state.attendanceQuery || (context.state.attendanceQuery = { search: '', status: '', page: 1 }), rows = [];
    var host = pageHost(screen, container, '<button class="button primary" data-in>تسجيل حضور</button>', field('search', 'بحث باسم الموظف', q.search, 'search') + dates(q) + select('status', 'الحالة', q.status, [{ value: '', label: 'الكل' }, { value: 'open', label: 'حاضر الآن' }, { value: 'closed', label: 'انصرف' }]));
    var load = loader(context, host, function () { return Api.request(base + 'attendance?' + qs(q)); }, function (page) {
      rows = page.items;
      if (context.state.globalSearchAttendanceId) {
        rows = rows.filter(function (row) { return row.id === context.state.globalSearchAttendanceId; });
        if (!rows.length) { host.innerHTML = '<div class="empty-state" role="alert">لا يمكن فتح هذا السجل</div>'; return; }
        page = { page: 1, pageSize: page.pageSize, total: 1 };
      }
      host.innerHTML = '<p class="muted workforce-note">وقت الحضور والتاريخ يسجلان تلقائيًا. سجّل مدة كل بريك بالدقائق؛ تُخصم من ساعات العمل الفعلية.</p>' + table(['رقم العملية', 'الموظف', 'الحضور', 'الانصراف', 'البريك', 'العمل الفعلي', ''], rows.map(function (r) {
        return '<tr><td>ATT-' + esc(String(r.id).padStart(6, '0')) + '</td><td>' + esc(r.employeeName) + '</td><td>' + esc(time(r.checkInUtc)) + '</td><td>' + esc(r.checkOutUtc ? time(r.checkOutUtc) : 'حاضر الآن') + '</td><td>' + esc(r.breakMinutes || 0) + ' د</td><td>' + Math.floor(r.workedSeconds / 3600) + ' س ' + Math.floor(r.workedSeconds % 3600 / 60) + ' د</td><td>' + (!r.checkOutUtc ? '<button class="button" data-break="' + r.id + '">تسجيل بريك</button><button class="button primary" data-out="' + r.id + '">انصراف</button>' : '') + '<button class="button" data-correct="' + r.id + '">تصحيح إداري</button><button class="button" data-attendance-audit="' + r.id + '">سجل التعديلات</button></td></tr>';
      })) + pager(page);
    });
    container.querySelector('[data-in]').onclick = function () { employeePicker(context, function (e) { editModal(context, 'attendance-in-' + e.id, 'حضور ' + e.name, function () { return '<p>تسجيل الحضور بالتوقيت الحالي.</p>'; }, function () { return { employeeId: e.id }; }, 'attendance/in', load); }); };
    wireFilters(container, q, function () {
      if (context.state.globalSearchAttendanceId) {
        delete context.state.globalSearchAttendanceId;
        delete q.employeeId;
      }
      load();
    });
    host.onclick = function (event) {
      var out = event.target.closest('[data-out]'), brk = event.target.closest('[data-break]'), correct = event.target.closest('[data-correct]'), audit = event.target.closest('[data-attendance-audit]');
      if (audit) { attendanceAudit(context, Number(audit.getAttribute('data-attendance-audit'))); return; }
      if (brk) { var bid = Number(brk.getAttribute('data-break')); editModal(context, 'attendance-break-' + bid, 'تسجيل مدة البريك', function () { return field('breakMinutes', 'مدة البريك بالدقائق', '', 'number', true); }, function (v) { var n = Number(v.breakMinutes); if (!Number.isInteger(n) || n < 1 || n > 1440) throw new Error('أدخل مدة بريك من دقيقة إلى 1440 دقيقة.'); return { id: bid, breakMinutes: n }; }, 'attendance/break', load); }
      else if (out) { var id = Number(out.getAttribute('data-out')); editModal(context, 'attendance-out-' + id, 'تسجيل الانصراف', function () { return '<p>سيُسجل وقت الانصراف تلقائيًا ويُخصم مجموع البريكات المسجلة من وقت العمل.</p>'; }, function () { return { id: id }; }, 'attendance/out', load); }
      else if (correct) {
        var row = rows.find(function (r) { return r.id === Number(correct.getAttribute('data-correct')); });
        editModal(context, 'attendance-correct-' + row.id, 'تصحيح حضور ' + row.employeeName, function (v) { return field('checkInUtc', 'الحضور بالتوقيت المحلي', v.checkInUtc, 'datetime-local', true) + field('checkOutUtc', 'الانصراف بالتوقيت المحلي (اختياري)', v.checkOutUtc, 'datetime-local') + field('reason', 'سبب التصحيح', v.reason, 'textarea', true); }, function (v) {
          return { id: row.id, version: row.version, checkInUtc: new Date(v.checkInUtc).toISOString(), checkOutUtc: v.checkOutUtc ? new Date(v.checkOutUtc).toISOString() : null, reason: v.reason };
        }, 'attendance/correct', load, { checkInUtc: localTimestamp(row.checkInUtc), checkOutUtc: localTimestamp(row.checkOutUtc) });
      } else changePage(event, q, load);
    }; load();
  }
  function employeeOptions(items, selected, includeInactive) { return [{ value: '', label: 'اختر الموظف' }].concat(items.filter(function (e) { return includeInactive || e.isActive; }).map(function (e) { return { value: e.id, label: e.code + ' — ' + e.name + (e.isActive ? '' : ' (مؤرشف)') }; })); }
  function advances(screen, container, context) {
    var staff = [], accounts = [], q = { employeeId: '', page: 1 }, loadVersion = 0;
    container.innerHTML = UI.pageHeader(screen, '<button class="button primary" data-add-advance>تسجيل سلفة</button>') + '<section class="card"><div class="card-body">' + select('employeeId', 'الموظف', '', [{ value: '', label: 'كل الموظفين' }]) + '</div><div data-advance-results></div></section>';
    var picker = container.querySelector('[name="employeeId"]'), host = container.querySelector('[data-advance-results]');
    function load() { var version = ++loadVersion, query = '?page=' + q.page + (q.employeeId ? '&employeeId=' + encodeURIComponent(q.employeeId) : ''); host.innerHTML = '<div class="empty-state">جارٍ التحميل…</div>'; Api.request(base + 'advances/history' + query).then(function (result) { if (!active(context) || version !== loadVersion) return; q.page = result.page; var rows = result.items; host.innerHTML = table(['رقم العملية', 'الموظف', 'التاريخ والوقت', 'المبلغ', 'حالة السلفة', 'حركة الخزنة', 'ملاحظات'], rows.map(function (r) { return '<tr><td>ADV-' + esc(String(r.id).padStart(6, '0')) + '</td><td>' + esc(r.employeeName) + '</td><td>' + esc(time(r.createdUtc)) + '</td><td>' + esc(r.amount) + '</td><td>' + (r.isSettled ? 'خُصمت من راتب' : 'مستحقة للخصم') + '</td><td>TR-' + esc(String(r.treasuryMovementId).padStart(6, '0')) + '</td><td>' + esc(r.notes || '—') + '</td></tr>'; })) + pager(result); }).catch(function (e) { if (active(context) && version === loadVersion) host.innerHTML = '<div class="empty-state" role="alert">' + esc(e.message) + '</div>'; }); }
    Promise.all([Api.request(base + 'employees?page=1&pageSize=100'), Api.request('/api/treasury/accounts')]).then(function (data) { if (!active(context)) return; staff = data[0].items; accounts = data[1].filter(function (a) { return !a.isDeleted; }); picker.innerHTML = [{ value: '', label: 'كل الموظفين' }].concat(employeeOptions(staff, '', true).slice(1)).map(function (o) { return '<option value="' + esc(o.value) + '">' + esc(o.label) + '</option>'; }).join(''); load(); }).catch(function (e) { host.innerHTML = '<div class="empty-state" role="alert">' + esc(e.message) + '</div>'; });
    picker.onchange = function () { q.employeeId = picker.value; q.page = 1; load(); };
    host.onclick = function (event) { var button = event.target.closest('[data-page]'); if (button && !button.disabled) { q.page = Number(button.getAttribute('data-page')); load(); } };
    container.querySelector('[data-add-advance]').onclick = function () { if (!accounts.length) { UI.toast('أضف خزنة أو بنكًا نشطًا قبل تسجيل السلفة.', 'warning'); return; } editModal(context, 'advance-new', 'تسجيل سلفة', function (v) { return select('employeeId', 'الموظف', v.employeeId, employeeOptions(staff)) + field('amount', 'مبلغ السلفة', v.amount, 'number', true) + select('treasuryAccountId', 'الخزنة أو البنك الذي صُرفت منه', v.treasuryAccountId || accounts[0].id, accounts.map(function (a) { return { value: a.id, label: a.name + ' — الرصيد ' + a.balance }; })) + field('notes', 'ملاحظات (اختياري)', v.notes, 'text'); }, function (v) { if (!v.employeeId) throw new Error('اختر الموظف.'); if (!v.treasuryAccountId) throw new Error('اختر الخزنة أو البنك.'); return { employeeId: Number(v.employeeId), amount: money(v.amount, false, true), treasuryAccountId: Number(v.treasuryAccountId), notes: v.notes }; }, 'advances', function (result) { UI.toast('تم تسجيل السلفة رقم ADV-' + String(result.id).padStart(6, '0') + ' وسحبها من الخزنة. ستظهر في حساب الراتب.', 'success'); load(); }); };
  }
  function payroll(screen, container, context) {
    var staff = [], accounts = [], selected = '', now = new Date(), period = now.getFullYear() + '-' + ('0' + (now.getMonth() + 1)).slice(-2);
    var loadVersion = 0, historyPage = 1;
    container.innerHTML = UI.pageHeader(screen) + '<section class="card"><div class="card-body form-grid">' + select('employeeId', 'الموظف', '', [{ value: '', label: 'جارٍ تحميل الموظفين…' }]) + field('period', 'شهر الراتب', period, 'month', true) + '<div class="span-2" data-payroll-quote></div><div class="span-2"><button type="button" class="button primary" data-pay disabled>دفع الراتب</button></div></div><div data-payroll-results></div></section>';
    var picker = container.querySelector('[name="employeeId"]'), month = container.querySelector('[name="period"]'), quote = container.querySelector('[data-payroll-quote]'), pay = container.querySelector('[data-pay]'), host = container.querySelector('[data-payroll-results]');
    function load() {
      var version = ++loadVersion;
      selected = picker.value; period = month.value; pay.disabled = true; quote.textContent = 'اختر الموظف لعرض الراتب والسلف المسجلة.';
      host.innerHTML = '';
      if (!selected) return;
      quote.textContent = 'جارٍ تحميل الراتب والسلف المسجلة…';
      Api.request(base + 'payroll/history?employeeId=' + encodeURIComponent(selected) + '&period=' + encodeURIComponent(period) + '&page=' + historyPage).then(function (data) {
        if (!active(context) || version !== loadVersion) return;
        var e = staff.find(function (x) { return String(x.id) === String(selected); }), history = data.history.items;
        historyPage = data.history.page;
        var pending = Math.round(Number(data.pendingAdvance) * 100);
        var salary = e && e.salary != null ? Math.round(Number(e.salary) * 100) : null, net = salary == null ? null : Math.max(0, salary - pending);
        quote.innerHTML = '<dl class="workforce-summary"><div><dt>الراتب الشهري</dt><dd>' + esc(e && e.salary != null ? e.salary : 'غير محدد') + '</dd></div><div><dt>السلف غير المخصومة</dt><dd>' + esc((pending / 100).toFixed(2)) + '</dd></div><div><dt>صافي المستحق</dt><dd>' + esc(net == null ? '—' : (net / 100).toFixed(2)) + '</dd></div></dl>';
        var already = data.alreadyPaid; pay.disabled = salary == null || !e.isActive || already || pending > salary || !period || !accounts.length;
        if (pending > salary) quote.innerHTML += '<p class="field-error">السلف المسجلة تتجاوز الراتب؛ لا يمكن اعتماد صافي سالب.</p>';
        host.innerHTML = table(['رقم العملية', 'الفترة', 'تاريخ الصرف', 'الراتب', 'السلف المخصومة', 'الصافي', 'رقم حركة الخزنة'], history.map(function (p) { return '<tr><td>PAY-' + esc(String(p.id).padStart(6, '0')) + '</td><td>' + esc(p.period) + '</td><td>' + esc(time(p.paidUtc)) + '</td><td>' + esc(p.salary) + '</td><td>' + esc(p.advances) + '</td><td>' + esc(p.net) + '</td><td>' + esc(p.treasuryMovementId ? 'TR-' + String(p.treasuryMovementId).padStart(6, '0') : 'صافي صفر') + '</td></tr>'; })) + pager(data.history);
      }).catch(function (e) { if (active(context) && version === loadVersion) quote.innerHTML = '<p class="field-error">' + esc(e.message) + '</p>'; });
    }
    Promise.all([Api.request(base + 'employees?page=1&pageSize=100'), Api.request('/api/treasury/accounts')]).then(function (data) { if (!active(context)) return; staff = data[0].items; accounts = data[1].filter(function (a) { return !a.isDeleted; }); picker.innerHTML = employeeOptions(staff, '', true).map(function (o) { return '<option value="' + esc(o.value) + '">' + esc(o.label) + '</option>'; }).join(''); }).catch(function (e) { quote.textContent = e.message; });
    picker.onchange = month.onchange = function () { historyPage = 1; load(); };
    host.onclick = function (event) { var button = event.target.closest('[data-page]'); if (button && !button.disabled) { historyPage = Number(button.getAttribute('data-page')); load(); } };
    pay.onclick = function () { if (!selected || !period || !accounts.length) return; var e = staff.find(function (x) { return String(x.id) === String(selected); }); editModal(context, 'payroll-' + selected + '-' + period, 'تأكيد صرف راتب ' + e.name, function (v) { return '<p>سيتم حفظ الراتب الشهري وخصم السلف غير المخصومة. يُسجل صافي المستحق كسحب من الخزنة المختارة في نفس المعاملة.</p>' + select('treasuryAccountId', 'الخزنة أو البنك', v.treasuryAccountId || accounts[0].id, accounts.map(function (a) { return { value: a.id, label: a.name + ' — الرصيد ' + a.balance }; })); }, function (v) { if (!v.treasuryAccountId) throw new Error('اختر الخزنة أو البنك.'); return { employeeId: Number(selected), period: period, treasuryAccountId: Number(v.treasuryAccountId) }; }, 'payroll/pay', function (result) { UI.toast('تم صرف الراتب برقم PAY-' + String(result.id).padStart(6, '0'), 'success'); load(); }); };
  }
  function attendanceAudit(context, id) {
    UI.modal({ title: 'سجل تعديلات الحضور', body: '<div data-attendance-audit-results></div>', onOpen: function (modal) {
      var host = modal.querySelector('[data-attendance-audit-results]'), page = 1;
      var labels = { 'attendance-in': 'حضور', 'attendance-out': 'انصراف', 'attendance-break': 'بريك', 'attendance-correct': 'تصحيح إداري' };
      var load = loader({ isActive: function () { return active(context) && modal.isConnected; } }, host, function () { return Api.request(base + 'attendance/' + id + '/audit?page=' + page); }, function (rows) {
        host.innerHTML = table(['الوقت', 'الإجراء', 'المستخدم', 'البيان'], rows.map(function (row) { var detail = (row.detail || '').replace('before=', 'السابق: ').replace('after=', 'الجديد: ').replace(/\d{4}-\d{2}-\d{2}T[\d:.]+Z/g, function (stamp) { return time(stamp); }); return '<tr><td>' + esc(time(row.createdUtc)) + '</td><td>' + esc(labels[row.action]) + '</td><td>' + esc(row.actorUserId) + '</td><td>' + esc(detail) + '</td></tr>'; })) + '<div class="workforce-actions"><button class="button" data-prev' + (page <= 1 ? ' disabled' : '') + '>السابق</button><button class="button" data-next' + (rows.length < 50 ? ' disabled' : '') + '>التالي</button></div>';
        host.querySelector('[data-prev]').onclick = function () { page--; load(); }; host.querySelector('[data-next]').onclick = function () { page++; load(); };
      }); load();
    } });
  }
  function localTimestamp(value) { if (!value) return ''; var d = new Date(value); return dateValue(value) + 'T' + ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2); }
  function employeePicker(context, selected) {
    UI.modal({ title: 'اختيار موظف لتسجيل الحضور', body: '<label class="field">بحث الموظفين<input class="input" data-pick-search type="search"></label><div data-pick-results></div>', onOpen: function (modal) {
      var input = modal.querySelector('[data-pick-search]'), host = modal.querySelector('[data-pick-results]'), q = { search: '', status: 'active', page: 1 }, rows = [];
      var load = loader({ isActive: function () { return active(context) && modal.isConnected; } }, host, function () { return Api.request(base + 'employees?' + qs(q)); }, function (page) {
        rows = page.items.filter(function (r) { return r.attendanceEnabled; }); host.innerHTML = rows.map(function (e) { return '<button class="button" data-pick="' + e.id + '">' + esc(e.code + ' — ' + e.name) + '</button>'; }).join(' ') + (!rows.length ? '<p>لا يوجد موظف مفعل للحضور في هذه الصفحة.</p>' : '') + pager(page);
      });
      input.oninput = U.debounce(function () { q.search = input.value; q.page = 1; load(); }, 220);
      host.onclick = function (event) { var b = event.target.closest('[data-pick]'); if (b) selected(rows.find(function (r) { return r.id === Number(b.getAttribute('data-pick')); })); else changePage(event, q, load); }; load();
    } });
  }
  function shiftOpen(context, saved) {
    function open(users) {
      editModal(context, 'shift-open', 'فتح وردية كاشير', function (v) { return field('amount', 'نقد افتتاح الدرج', v.amount || '0', 'text', true) + field('notes', 'ملاحظات الافتتاح (اختياري)', v.notes, 'textarea') + (users ? select('cashierId', 'الكاشير المسؤول', v.cashierId || context.user.id, users.filter(function (u) { return u.isActive; }).map(function (u) { return { value: u.id, label: u.displayName }; })) : '<p>المستخدم الحالي: ' + esc(context.user.displayName) + '</p>') + '<p class="muted span-2">جهاز مستقل واحد. مبلغ الافتتاح جرد نقدي ولا ينشئ إيداعًا في الخزنة.</p>'; }, function (v) { return { amount: money(v.amount), notes: v.notes, cashierId: users ? Number(v.cashierId || context.user.id) : null }; }, 'shifts/open', saved);
    }
    if (context.user.role === 'administrator') Api.request('/api/users').then(function (users) { if (active(context)) open(users); }).catch(function (e) { UI.toast(e.message, 'danger'); }); else open(null);
  }
  function shiftAction(context, shift, action, saved) {
    editModal(context, action + '-' + shift.id, action === 'close' ? 'إغلاق الوردية #' + shift.id : 'إدخال / إخراج نقد من الدرج', function (v) {
      return '<p class="span-2">النقد المتوقع: <strong>' + esc(shift.expected) + '</strong> ج.م</p>' + field('amount', action === 'close' ? 'النقد المعدود عند الإغلاق' : 'المبلغ', v.amount, 'text', true) + (action === 'close' ? field('notes', 'ملاحظات الإغلاق (اختياري)', v.notes, 'textarea') + '<p class="span-2" data-close-difference data-expected="' + shift.expectedScaled + '">أدخل النقد المعدود لحساب الفرق.</p><label class="span-2"><input type="checkbox" name="confirmClose" required> أؤكد جرد النقد وإغلاق الوردية ومنع تعديل فواتيرها</label>' : select('direction', 'الاتجاه', v.direction || 'in', [{ value: 'in', label: 'إدخال نقد' }, { value: 'out', label: 'إخراج نقد' }]) + field('reason', 'سبب الحركة', v.reason, 'textarea', true) + '<p class="muted span-2">حركة موثقة للدرج فقط. لا تنقل رصيد الخزنة أو البنك ولا تسجل مصروفًا.</p>');
    }, function (v) { if(action==='close'&&!v.confirmClose)throw new Error('أكد جرد النقد وإغلاق الوردية.'); return { expectedScaled: shift.expectedScaled, id: shift.id, amount: money(v.amount, false, action === 'adjust'), notes: v.notes, direction: v.direction, reason: v.reason }; }, 'shifts/' + action, saved, null, action==='close'?'تأكيد إغلاق الوردية':null);
  }
  function summary(s) {
    var values = [['الكاشير', s.cashierName], ['الجهاز', 'الجهاز المستقل'], ['الافتتاح', time(s.openedUtc)], ['الإغلاق', time(s.closedUtc)], ['نقد الافتتاح', s.opening], ['عدد المبيعات', s.salesCount], ['مبيعات نقدية', s.cashSales], ['مبيعات آجلة منشورة (إجمالي قبل الإلغاء)', s.creditSales], ['تحصيلات نقدية', s.collections], ['إدخال نقد', s.cashIn], ['إخراج نقد', s.cashOut], ['النقد المتوقع', s.expected], ['النقد المعدود', s.counted || '—'], ['فرق النقد', discrepancy(s)], ['ملاحظات الافتتاح', s.openingNotes], ['ملاحظات الإغلاق', s.closingNotes]];
    return '<dl class="workforce-summary">' + values.map(function (v) { return '<div><dt>' + esc(v[0]) + '</dt><dd>' + esc(v[1]) + '</dd></div>'; }).join('') + '</dl><p class="muted workforce-note">المتوقع = الافتتاح + المبيعات النقدية + التحصيل النقدي + الإدخال − الإخراج. المرتجعات من خزنة مستقلة؛ تحصيلات كشف حساب العميل والحركات العامة للخزنة غير مرتبطة بهذا الدرج.</p>';
  }
  function shifts(screen, container, context) {
    if (screen.route === 'shift-details') { details(screen, container, context); return; }
    var q = context.state.shiftQuery || (context.state.shiftQuery = { status: screen.route === 'shift-add' ? 'open' : '', page: 1 }), rows = [];
    var host = pageHost(screen, container, '<button class="button primary" data-open-shift>فتح وردية</button>', dates(q) + select('status', 'الحالة', q.status, [{ value: '', label: 'الكل' }, { value: 'open', label: 'مفتوحة' }, { value: 'closed', label: 'مغلقة' }]) + (context.user.role === 'administrator' ? field('cashierId', 'رقم مستخدم الكاشير (اختياري)', q.cashierId, 'number') : '') + select('discrepanciesOnly', 'تقرير الفروق', q.discrepanciesOnly || '', [{ value: '', label: 'جميع الورديات' }, { value: 'true', label: 'عجز أو زيادة فقط' }]));
    var load = loader(context, host, function () { return Api.request(base + 'shifts?' + qs(q)); }, function (page) {
      rows = page.items; host.innerHTML = table(['الوردية', 'الكاشير', 'عدد ورديات المستخدم', 'المدة بالدقائق', 'الافتتاح', 'الحالة', 'الافتتاح النقدي', 'النقدي', 'الآجل', 'التحصيل', 'المتوقع', 'المعدود', 'الفرق', ''], rows.map(function (s) {
        return '<tr><td>#' + s.id + '</td><td>' + esc(s.cashierName) + '</td><td>' + s.userShiftCount + '</td><td>' + s.durationMinutes + '</td><td>' + esc(time(s.openedUtc)) + '</td><td>' + (s.closedUtc ? 'مغلقة' : 'مفتوحة') + '</td><td>' + esc(s.opening) + '</td><td>' + esc(s.cashSales) + '</td><td>' + esc(s.creditSales) + '</td><td>' + esc(s.collections) + '</td><td>' + esc(s.expected) + '</td><td>' + esc(s.counted || '—') + '</td><td>' + esc(discrepancy(s)) + '</td><td><button class="button" data-shift="' + s.id + '">التفاصيل</button>' + (!s.closedUtc ? '<button class="button" data-close-shift="' + s.id + '">إغلاق</button>' : '') + '</td></tr>';
      })) + pager(page);
    });
    container.querySelector('[data-open-shift]').onclick = function () { shiftOpen(context, load); }; wireFilters(container, q, load);
    host.onclick = function (event) { var b = event.target.closest('[data-shift]'), close = event.target.closest('[data-close-shift]'); if (b) context.navigate('shift-details', { shiftId: Number(b.getAttribute('data-shift')) }); else if (close) shiftAction(context, rows.find(function (r) { return r.id === Number(close.getAttribute('data-close-shift')); }), 'close', load); else changePage(event, q, load); }; load();
  }
  function details(screen, container, context) {
    var id = context.state.shiftId, eventPage = 1;
    container.innerHTML = UI.pageHeader(screen) + '<section class="card"><div data-detail class="card-body"></div><div data-events></div><div data-activity></div></section>';
    var host = container.querySelector('[data-detail]'), events = container.querySelector('[data-events]');
    if (!id) { host.innerHTML = '<p>اختر وردية من السجل لعرض التفاصيل.</p><button class="button" data-history>سجل الورديات</button>'; host.querySelector('[data-history]').onclick = function () { context.navigate('shifts-list'); }; return; }
    var load = loader(context, host, function () { return Api.request(base + 'shifts/' + id); }, function (s) {
      host.innerHTML = summary(s) + (!s.closedUtc ? '<div class="workforce-actions"><button class="button" data-adjust>حركة نقد للدرج</button><button class="button primary" data-close>إغلاق الوردية</button></div>' : '');
      if(global.AlKhayalPrinting){global.AlKhayalPrinting.mount(host,"shift-summary",s.id);if(s.closedUtc)global.AlKhayalPrinting.mount(host,"shift-closing",s.id).textContent="طباعة إغلاق الوردية";}
      if (!s.closedUtc) { host.querySelector('[data-close]').onclick = function () { shiftAction(context, s, 'close', function () { load(); loadEvents(); }); }; host.querySelector('[data-adjust]').onclick = function () { shiftAction(context, s, 'adjust', function () { load(); loadEvents(); }); }; }
    });
    var loadEvents = loader(context, events, function () { return Api.request(base + 'shifts/' + id + '/events?page=' + eventPage); }, function (rows) {
      events.innerHTML = table(['الوقت', 'نوع الحركة', 'المبلغ', 'الفاتورة', 'الدفعة', 'المستخدم', 'السبب'], rows.map(function (e) { return '<tr><td>' + esc(time(e.createdUtc)) + '</td><td>' + esc(L.eventType(e.kind)) + '</td><td>' + esc(e.amount) + '</td><td>' + esc(e.salesInvoiceId) + '</td><td>' + esc(e.salesPaymentId) + '</td><td>' + esc(L.actor(e.actorDisplayName, e.actorUserId)) + '</td><td>' + esc(e.reason || '—') + '</td></tr>'; })) + '<div class="workforce-actions"><button class="button" data-event-prev' + (eventPage <= 1 ? ' disabled' : '') + '>السابق</button><span>صفحة ' + eventPage + '</span><button class="button" data-event-next' + (rows.length < 50 ? ' disabled' : '') + '>التالي</button></div>';
      events.querySelector('[data-event-prev]').onclick = function () { eventPage--; loadEvents(); }; events.querySelector('[data-event-next]').onclick = function () { eventPage++; loadEvents(); };
    });
    var activityPage=1, activity=container.querySelector('[data-activity]');
    var loadActivity=loader(context,activity,function(){return Api.request(base+'shifts/'+id+'/activity?page='+activityPage);},function(page){
      activity.innerHTML=table(['الوقت','المصدر','المستخدم','التفاصيل'],page.items.map(function(e){return '<tr><td>'+esc(time(e.createdUtc))+'</td><td>'+esc(L.activitySource(e.kind))+'</td><td>'+esc(L.actor(e.actorDisplayName,e.actorUserId))+'</td><td>'+esc(L.activityDescription(e.kind,e.detail))+(e.kind==='invoice'&&(!context.canNavigate||context.canNavigate('sales-management'))?' <button class="button" data-sale-invoice="'+e.id+'">فتح الفاتورة</button>':'')+'</td></tr>';}))+pager(page);
      activity.onclick=function(e){var invoice=e.target.closest('[data-sale-invoice]');if(invoice){context.navigate('sales-management',{saleInvoiceId:Number(invoice.getAttribute('data-sale-invoice'))});return;}var b=e.target.closest('[data-page]');if(b&&!b.disabled){activityPage=Number(b.getAttribute('data-page'));loadActivity();}};
    }); load(); loadEvents(); loadActivity();
  }
  var currentShift = null, pendingRefresh = null;
  var shiftTicker = 0, lastClock = '', visibilityBound = false;
  function pad2(n) { return n < 10 ? '0' + n : String(n); }
  /* openedUtc is UTC; a timestamp without a zone designator must not be parsed as local time. */
  function utcMs(v) {
    if (typeof v === 'string' && /T\d\d:\d\d/.test(v) && !/(Z|[+-]\d\d(:?\d\d)?)$/i.test(v)) v += 'Z';
    return new Date(v).getTime();
  }
  /* Western-digit HH:MM from the persisted openedUtc; 00:00:00 while no shift is open. */
  function clockText() {
    if (!currentShift) return '00:00:00';
    var minutes = Math.floor((Date.now() - utcMs(currentShift.openedUtc)) / 60000);
    if (!(minutes > 0)) minutes = 0;
    return pad2(Math.floor(minutes / 60)) + ':' + pad2(minutes % 60);
  }
  function statusText() { return currentShift ? 'وردية مفتوحة' : 'لا توجد وردية مفتوحة'; }
  function setText(node, value) { if (node && node.textContent !== value) node.textContent = value; }
  function paintClock(value) {
    lastClock = value;
    global.document.querySelectorAll('[data-shift-clock]').forEach(function (n) { setText(n, value); });
  }
  /* Runs every second but only touches the DOM when the displayed HH:MM actually changes. */
  function tickClock() {
    if (!currentShift || global.document.hidden) return;
    var value = clockText();
    if (value !== lastClock) paintClock(value);
  }
  /* Exactly one interval, and only while a shift is open. */
  function syncTicker() {
    if (currentShift) { if (!shiftTicker) shiftTicker = global.setInterval(tickClock, 1000); }
    else if (shiftTicker) { global.clearInterval(shiftTicker); shiftTicker = 0; }
  }
  function paintShiftButton(n) {
    var state = currentShift ? 'open' : 'closed', title = currentShift ? currentShift.cashierName + ' — #' + currentShift.id : 'افتح وردية أولاً';
    if (n.getAttribute('data-shift-ui') !== '1') {
      n.classList.add('shift-btn');
      n.innerHTML = '<span class="shift-info"><span class="shift-dot" aria-hidden="true"></span><span class="shift-state"></span><span class="shift-time" data-shift-clock dir="ltr"></span></span><span class="shift-action"></span>';
      n.setAttribute('data-shift-ui', '1');
    }
    if (n.getAttribute('data-shift-state') !== state) n.setAttribute('data-shift-state', state);
    setText(n.querySelector('.shift-state'), statusText());
    setText(n.querySelector('.shift-action'), currentShift ? 'إغلاق وردية #' + currentShift.id : 'فتح وردية');
    if (n.title !== title) n.title = title;
  }
  function paintStatus() {
    var button = global.document.getElementById('headerShift');
    if (button) paintShiftButton(button);
    global.document.querySelectorAll('[data-workforce-current]').forEach(function (n) { paintShiftButton(n); });
    paintClock(clockText());
    syncTicker();
    global.document.querySelectorAll('[data-post]').forEach(function (n) {
      if (!currentShift) { n.disabled = true; n.textContent = 'افتح وردية أولاً'; n.setAttribute('data-shift-blocked', ''); }
      else if (n.hasAttribute('data-shift-blocked')) { n.removeAttribute('data-shift-blocked'); n.textContent = 'إتمام البيع'; n.disabled = n.getAttribute('data-sale-ready') !== 'true'; }
    });
  }
  function refresh() {
    if (pendingRefresh) return pendingRefresh;
    pendingRefresh = Api.request(base + 'shifts/current').then(function (s) { currentShift = s; paintStatus(); return s; }, function (e) { currentShift = null; paintStatus(); throw e; });
    pendingRefresh = pendingRefresh.then(function (s) { pendingRefresh = null; return s; }, function (e) { pendingRefresh = null; throw e; }); return pendingRefresh;
  }
  function manage(context) {
    refresh().then(function (s) {
      if (!s) { shiftOpen(context, function () { refresh().catch(function(e){UI.toast(e.message,'danger');}); }); return; }
      UI.modal({title:'الوردية الحالية #' + s.id,body:summary(s),actions:'<button class="button" data-adjust-current>حركة نقد للدرج</button><button class="button primary" data-close-current>إغلاق وردية</button><button class="button" data-modal-close>رجوع</button>',onOpen:function(modal){
        modal.querySelector('[data-adjust-current]').onclick=function(){shiftAction(context,s,'adjust',function(){refresh().catch(function(e){UI.toast(e.message,'danger');});});};
        modal.querySelector('[data-close-current]').onclick=function(){shiftAction(context,s,'close',function(){refresh().catch(function(e){UI.toast(e.message,'danger');});});};
      }});
    }).catch(function(e){UI.toast(e.message,'danger');});
  }
  function header(context) {
    var button = global.document.getElementById('headerShift'); if (button) button.onclick = function(){manage(context);};
    refresh().catch(function(e){UI.toast(e.message,'danger');});
    global.addEventListener('focus',function(){refresh().catch(function(e){UI.toast(e.message,'danger');});});
    if(!visibilityBound){visibilityBound=true;global.document.addEventListener('visibilitychange',tickClock);}
  }
  function mount(container, context) {
    var anchor = container.querySelector('.touch-head') || container.querySelector('.page-header'); if (!anchor) return;
    var button = global.document.createElement('button'); button.type='button'; button.className='button'; button.setAttribute('data-workforce-current','');
    button.onclick=function(event){event.stopPropagation();manage(context);}; anchor.appendChild(button);
    var post = container.querySelector('[data-post]'); if(post)post.setAttribute('data-sale-ready',String(!post.disabled));
    paintStatus(); refresh().catch(function(e){if(active(context))UI.toast(e.message,'danger');});
  }
  R['workforce-employees'] = employees; R['workforce-attendance'] = attendance; R['workforce-shifts'] = shifts; R['workforce-advances'] = advances; R['workforce-payroll'] = payroll;
  global.AlKhayalWorkforce = { mount: mount, header: header, refresh: refresh, hasOpenShift: function(){return !!currentShift;} };
}(window));
