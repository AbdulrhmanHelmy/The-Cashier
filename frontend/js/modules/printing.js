(function (global) {
  "use strict";
  var Api = global.AlKhayalApi, UI = global.AlKhayalUI, U = global.AlKhayalUtils;
  function numericHtml(value){return U.numericHtml?U.numericHtml(value):U.escape(value);}
  function dateTime(value){return U.dateTime?U.dateTime(value):String(value==null?"":value);}
  var inFlight = Object.create(null), pending = Object.create(null);
  function key() { var bytes = new Uint8Array(16); global.crypto.getRandomValues(bytes); return Array.from(bytes).map(function (n) { return n.toString(16).padStart(2, "0"); }).join(""); }
  function active(context) { return !context.isActive || context.isActive(); }
  function esc(x) { return U.escape(x == null ? "" : String(x)); }
  // One shared transport for all document screens. No financial mutation or browser print engine.
  function submit(type, id, automatic, button) {
    var identity = type + ":" + id + ":" + (automatic ? "auto" : "manual");
    if (inFlight[identity]) return inFlight[identity];
    if (button) { button.disabled = true; button.setAttribute("aria-busy", "true"); }
    var operationKey = pending[identity] || (pending[identity] = key());
    inFlight[identity] = Api.request("/api/printing/jobs", { method: "POST", body: JSON.stringify({ documentType: type, documentId: id, operationKey: operationKey, automatic: !!automatic, reprint: !automatic }) }).then(function (job) {
      delete pending[identity];
      if (job.status !== "disabled") UI.toast((automatic ? "تم حفظ البيع؛ " : "") + (job.status === "submitted_unknown" ? "أُرسل " + (type === "delivery" ? "بون التوصيل" : "الفاتورة") + " إلى Windows؛ خروج الورق غير مؤكد." : "تعذر طباعة " + (type === "delivery" ? "بون التوصيل" : "الفاتورة") + ": " + (job.message || "راجع سجل الطباعة.")), job.status === "submitted_unknown" ? "success" : "warning");
      return job;
    }).catch(function (e) {
      // Retain key on uncertain network outcome: retry asks for the original job instead of printing twice.
      if (e.status && e.status < 500) delete pending[identity];
      UI.toast((automatic ? "تم حفظ البيع؛ " : "") + "تعذر تأكيد طباعة " + (type === "delivery" ? "بون التوصيل" : "الفاتورة") + ": " + (e.message || "راجع سجل مهام الطباعة."), "warning");
      return null;
    }).finally(function () { delete inFlight[identity]; if (button) { button.disabled = false; button.removeAttribute("aria-busy"); } });
    return inFlight[identity];
  }
  function mount(host, type, id) {
    var button = global.document.createElement("button"); button.type = "button"; button.className = "button"; button.textContent = "🖨 طباعة الفاتورة";
    button.onclick = function () { submit(type, id, false, button); }; host.appendChild(button); return button;
  }
  function supplierReceipts(host, supplierId) {
    var button = global.document.createElement('button'); button.type = 'button'; button.className = 'button'; button.textContent = 'طباعة سند دفع محفوظ'; host.appendChild(button);
    button.onclick = function () {
      button.disabled = true;
      Api.request('/api/suppliers/' + supplierId + '/ledger').then(function (rows) {
        if (!button.isConnected) return;
        var payments = rows.filter(function (p) { return p.entryType === 'payment' || p.entryType === 'legacy_payment'; });
        UI.modal({ title: 'سندات دفع المورد المحفوظة', body: payments.length ? '<label class="field"><span>اختر سند الدفع</span><select class="select" data-payment-print-choice>' + payments.map(function (p) { return '<option value="' + p.id + '">' + esc(p.reference + ' — ' + p.occurredOn) + '</option>'; }).join('') + '</select></label>' : '<p>لا توجد سندات دفع محفوظة لهذا المورد.</p>', actions: (payments.length ? '<button class="button primary" data-print-payment>طباعة / إعادة طباعة</button>' : '') + '<button class="button" data-modal-close>إغلاق</button>', onOpen: function (modal) { var print = modal.querySelector('[data-print-payment]'); if (print) print.onclick = function () { submit('supplier-payment', Number(modal.querySelector('[data-payment-print-choice]').value), false, print); }; } });
      }).catch(function (e) { UI.toast(e.message, 'warning'); }).finally(function () { button.disabled = false; });
    };
  }
  var types = ["sale", "delivery", "sales-return", "purchase", "purchase-return", "customer-payment", "supplier-payment", "shift-summary", "shift-closing"];
  var titles = ["فاتورة بيع", "إيصال توصيل", "مرتجع بيع", "فاتورة شراء", "مرتجع شراء", "سند قبض عميل", "سند دفع مورد", "ملخص وردية", "إغلاق وردية"];
  var sectionNames = { company: "اسم الشركة", logo: "الشعار", address: "العنوان", phone: "هاتف الشركة", taxInfo: "الرقم الضريبي", header: "رأس مخصص", title: "عنوان المستند", reference: "رقم المستند", date: "التاريخ والوقت", cashier: "أمين الصندوق", party: "العميل / المورد", partyPhone: "هاتف العميل / المورد الحالي", warehouse: "المخزن / نقطة البيع", paymentMethod: "طريقة الدفع", paymentNotes: "ملاحظات الدفع", signature: "حقل التوقيع", items: "بنود المستند", variant: "خصائص الصنف والوحدة", unitPrice: "سعر الوحدة", discount: "خصم البند", tax: "ضريبة البند", quantity: "الكمية", lineTotal: "قيمة البند", subtotal: "قبل الخصم", discountTotal: "إجمالي الخصم", taxTotal: "إجمالي الضريبة", grandTotal: "الإجمالي", paid: "المدفوع / المعدود", remaining: "المتبقي / الفرق", footer: "رسالة الشكر", policy: "سياسة الإرجاع" };
  var statuses = { accepted: "مقبول — الإرسال غير مؤكد", submitted_unknown: "أُرسل إلى Windows — خروج الورق غير مؤكد", failed: "فشل", uncertain: "النتيجة غير مؤكدة" };
  function option(value, label, selected) { return '<option value="' + esc(value) + '"' + (value === selected ? ' selected' : '') + '>' + esc(label) + '</option>'; }
  function field(label, keyName, value, min, max, step) { return '<label class="field"><span>' + label + '</span><input class="input" type="number" data-design="' + keyName + '" value="' + esc(value) + '" min="' + min + '" max="' + max + '" step="' + (step || '0.1') + '"></label>'; }
  // Share only the outstanding scan, including across a closed and reopened workspace.
  var discoveryRequest;
  function discoverPrinters() {
    if (!discoveryRequest) {
      discoveryRequest = Api.request('/api/printing/printers', { skipPageTrack: true })
        .finally(function () { discoveryRequest = null; });
    }
    return discoveryRequest;
  }
  function currentView(context) {
    return !context.isCurrent || context.isCurrent();
  }
  function deviceStatus(phase, found) {
    if (phase !== 'loaded' && phase !== 'empty') return 'تعذّر التحقق من الطابعة حاليًا';
    return found ? 'متاحة ضمن قائمة Windows المكتشفة' : 'غير موجودة في قائمة Windows الحالية';
  }
  function windowsOptions(devices, selected, phase, showDefault) {
    return option('', 'اختر طابعة Windows', selected) + devices.map(function (p) {
      return option(p.name, p.name + (showDefault && p.isDefault ? ' (افتراضية في Windows)' : ''), selected);
    }).join('') + (selected && !devices.some(function (p) { return p.name === selected; })
      ? option(selected, selected + ' — ' + deviceStatus(phase, false), selected) : '');
  }
  function settings(screen, container, context, discovery) {
    var state = context.state.printing || (context.state.printing = {}), printers = [], defaults, saving = false, sequence = 0;
    container.innerHTML = UI.pageHeader(screen) + '<section class="card"><div class="card-body" data-printing-host><p>جارٍ تحميل إعدادات الطباعة…</p></div></section>';
    var host = container.querySelector('[data-printing-host]');
    function selected() { return state.settings.templates.find(function (t) { return t.id === state.selected; }) || state.settings.templates[0]; }
    function report(e) { if (active(context)) UI.toast(e.message || "تعذر تحميل إعدادات الطباعة.", "danger"); }
    function draw() {
      if (!currentView(context)) return;
      var s = state.settings;
      host.innerHTML = '<p>طباعة مباشرة بواسطة C# إلى طابعة Windows المختارة. تغيير الإعدادات يتطلب الحفظ. اختبار الطباعة يرسل ورقًا فعليًا.</p>' +
        '<p role="status" data-legacy-discovery></p><button class="button" data-refresh-printers>تحديث قائمة الطابعات</button>' +
        '<label class="field"><span><input type="checkbox" data-auto' + (s.autoPrintSale ? ' checked' : '') + '> طباعة الفاتورة تلقائيًا بعد البيع</span></label>' +
        '<label class="field"><span><input type="checkbox" data-auto-delivery' + (s.autoPrintDelivery !== false ? ' checked' : '') + '> طباعة بون التوصيل منفصلًا بعد بيع التوصيل</span></label>' +
        '<div class="table-wrap"><table class="data-table"><thead><tr><th>المستند</th><th>الطابعة</th><th>القالب الافتراضي</th><th>النسخ</th><th>اختبار</th></tr></thead><tbody>' + s.profiles.map(function (p, i) {
          var names = printers.map(function (x) { return x.name; }); if (p.printerName && names.indexOf(p.printerName) < 0) names.push(p.printerName);
          return '<tr><td>' + titles[types.indexOf(p.documentType)] + '</td><td><select class="select" data-profile="' + i + '" data-prop="printerName" aria-label="طابعة ' + esc(p.documentType) + '">' + option('', 'لم يتم الاختيار', p.printerName || '') + names.map(function (n) { return option(n, n + (printers.some(function (x) { return x.name === n; }) ? '' : ' — غير موجودة'), p.printerName); }).join('') + '</select></td><td><select class="select" data-profile="' + i + '" data-prop="templateId" aria-label="قالب المستند">' + s.templates.map(function (t) { return option(t.id, t.name, p.templateId); }).join('') + '</select></td><td><input class="input" type="number" min="1" max="10" value="' + p.copies + '" data-profile="' + i + '" data-prop="copies" aria-label="عدد النسخ"></td><td><button class="button" data-test="' + p.documentType + '">اختبار الإعداد المحفوظ</button></td></tr>';
        }).join('') + '</tbody></table></div>' +
        '<details><summary>مقاسات التعريفات المتاحة</summary><div data-legacy-papers></div></details>' +
        '<div class="printing-actions"><button class="button primary" data-save-printing>حفظ إعدادات الطباعة والقوالب</button><button class="button" data-reload-printing>إعادة تحميل المحفوظ</button></div>' +
        '<h2>مصمم القوالب</h2><div class="printing-actions"><label>القالب <select class="select" data-template>' + s.templates.map(function (t) { return option(t.id, t.name, selected().id); }).join('') + '</select></label><button class="button" data-new-template>إنشاء قالب</button><button class="button" data-copy-template>نسخ القالب</button><button class="button" data-delete-template>حذف قالب غير مستخدم</button><button class="button" data-restore-template>استعادة التصميم الافتراضي</button></div>' +
        '<div class="printing-designer"><div data-editor></div><aside class="printing-preview"><button class="button" data-preview-template>تحديث المعاينة</button><p>الصفحة الأولى ببيانات اختبار من محرّك C#؛ لا ترسل طباعة.</p><img alt="معاينة قالب الطباعة" data-preview-image hidden></aside></div>' +
        '<details><summary>آخر 100 مهمة طباعة</summary><button class="button" data-load-jobs>تحديث السجل</button><div data-print-jobs></div></details>';
      editor(); wire(); updateDiscovery();
    }
    function updateDiscovery() {
      printers = discovery.devices;
      var button = host.querySelector('[data-refresh-printers]');
      if (!button) return;
      button.disabled = discovery.pending;
      host.querySelector('[data-legacy-discovery]').textContent = discovery.message();
      host.querySelectorAll('[data-prop="printerName"]').forEach(function (select) {
        select.innerHTML = windowsOptions(printers, select.value, discovery.phase);
      });
      host.querySelector('[data-legacy-papers]').innerHTML = printers.map(function (p) { return '<p><strong>' + esc(p.name) + '</strong>: ' + esc(p.capabilityNote) + '</p><p>' + p.papers.map(function (paper) { return esc(paper.name + ' (' + Number(paper.widthMm).toFixed(1) + ' × ' + Number(paper.heightMm).toFixed(1) + ' مم)'); }).join('، ') + '</p>'; }).join('');
    }
    function editor() {
      ++sequence;
      var previousPreview = host.querySelector('[data-preview-image]'); if (previousPreview) previousPreview.hidden = true;
      var t = selected(), p = t.paper, target = host.querySelector('[data-editor]');
      target.innerHTML = '<div class="form-grid"><label class="field"><span>اسم القالب</span><input class="input" data-design="name" value="' + esc(t.name) + '" maxlength="100"></label>' +
        '<label class="field"><span>مقاس الورق</span><select class="select" data-design="paper.preset">' + ['58mm','80mm','A4','A5','custom'].map(function (v) { return option(v, v === 'custom' ? 'مخصص' : v, p.preset); }).join('') + '</select></label>' +
        field('العرض مم', 'paper.widthMm', p.widthMm, 30, 420) + field('الارتفاع / أقصى طول صفحة مم', 'paper.heightMm', p.heightMm, 30, 3000) +
        field('هامش يمين مم', 'paper.marginRightMm', p.marginRightMm, 0, 100) + field('هامش يسار مم', 'paper.marginLeftMm', p.marginLeftMm, 0, 100) + field('هامش أعلى مم', 'paper.marginTopMm', p.marginTopMm, 0, 100) + field('هامش أسفل مم', 'paper.marginBottomMm', p.marginBottomMm, 0, 100) + field('مقياس الخط', 'paper.scale', p.scale, .5, 2, .05) +
        '<label class="field"><span><input type="checkbox" data-design="paper.continuous"' + (p.continuous ? ' checked' : '') + '> طول إيصال متغير ضمن حدود التعريف</span></label><label class="field"><span><input type="checkbox" data-design="paper.landscape"' + (p.landscape ? ' checked' : '') + '> اتجاه أفقي</span></label><label class="field"><span><input type="checkbox" data-design="showReferenceBarcode"' + (t.showReferenceBarcode ? ' checked' : '') + '> باركود رقم المستند على الإيصال الحراري</span></label>' +
        field('عرض اسم الصنف %', 'nameColumnPercent', t.nameColumnPercent, 35, 70) + field('عرض الكمية %', 'quantityColumnPercent', t.quantityColumnPercent, 10, 25) +
        field('عرض الشعار مم', 'logoWidthMm', t.logoWidthMm, 1, 100) + field('ارتفاع الشعار مم', 'logoHeightMm', t.logoHeightMm, 1, 100) +
        '<label class="field"><span>شعار PNG حتى 130 كيلوبايت / 1200×1200</span><input type="file" accept="image/png" data-logo></label><button class="button" data-clear-logo>إزالة الشعار</button>' +
        ['headerText','footerText','returnPolicy'].map(function (k, i) { return '<label class="field"><span>' + ['نص رأس مخصص','رسالة الشكر','سياسة الإرجاع'][i] + '</span><textarea class="input" maxlength="1000" data-design="' + k + '">' + esc(t[k]) + '</textarea></label>'; }).join('') +
        '<label class="field"><span><input type="checkbox" data-design="dividers"' + (t.dividers ? ' checked' : '') + '> فواصل البنود</span></label></div>' +
        '<p>غيّر ترتيب الأقسام بالأسهم. سعر الوحدة والخصم والضريبة والخصائص تظهر أسفل اسم الصنف عند تفعيلها.</p><div class="table-wrap"><table class="data-table"><thead><tr><th>القسم</th><th>ظاهر</th><th>عريض</th><th>حجم الخط نقطة</th><th>مسافة مم</th><th>محاذاة</th><th>ترتيب</th></tr></thead><tbody>' + t.sections.map(function (s, i) {
          return '<tr><td>' + sectionNames[s.key] + '</td><td><input aria-label="إظهار ' + sectionNames[s.key] + '" type="checkbox" data-section="' + i + '" data-prop="visible"' + (s.visible ? ' checked' : '') + '></td><td><input aria-label="خط عريض" type="checkbox" data-section="' + i + '" data-prop="bold"' + (s.bold ? ' checked' : '') + '></td><td><input class="input" aria-label="حجم الخط" type="number" min="6" max="24" value="' + s.fontSize + '" data-section="' + i + '" data-prop="fontSize"></td><td><input class="input" aria-label="المسافة" type="number" min="0" max="15" step="0.1" value="' + s.spacingMm + '" data-section="' + i + '" data-prop="spacingMm"></td><td><select class="select" aria-label="المحاذاة" data-section="' + i + '" data-prop="alignment">' + ['right','center','left'].map(function (v, j) { return option(v, ['يمين','وسط','يسار'][j], s.alignment); }).join('') + '</select></td><td><button class="button" data-move="' + i + '" data-direction="-1" aria-label="أعلى">↑</button><button class="button" data-move="' + i + '" data-direction="1" aria-label="أسفل">↓</button></td></tr>';
        }).join('') + '</tbody></table></div>';
    }
    async function save(button) {
      if (saving) return; saving = true; button.disabled = true;
      var submitted = JSON.stringify(state.settings);
      try { var result = await Api.request('/api/printing/settings', { method: 'PUT', body: submitted }); var edited = JSON.stringify(state.settings) !== submitted; state.settings.version = result.version; if (active(context)) UI.toast(edited ? 'تم حفظ الإعدادات السابقة؛ توجد تعديلات جديدة غير محفوظة.' : 'تم حفظ إعدادات الطباعة والقوالب.', edited ? 'warning' : 'success'); }
      catch (e) { report(e); } finally { saving = false; if (currentView(context)) button.disabled = false; }
    }
    function wire() {
      host.oninput = function (event) {
        var n = event.target, value = n.type === 'checkbox' ? n.checked : n.type === 'number' ? Number(n.value) : n.value;
        if (n.hasAttribute('data-design') || n.hasAttribute('data-section')) { ++sequence; host.querySelector('[data-preview-image]').hidden = true; }
        if (n.hasAttribute('data-auto')) state.settings.autoPrintSale = n.checked;
        if (n.hasAttribute('data-auto-delivery')) state.settings.autoPrintDelivery = n.checked;
        if (n.hasAttribute('data-profile')) state.settings.profiles[Number(n.getAttribute('data-profile'))][n.getAttribute('data-prop')] = value;
        if (n.hasAttribute('data-section')) { var section = selected().sections[Number(n.getAttribute('data-section'))], property = n.getAttribute('data-prop'); section[property] = value; if (['fontSize','spacingMm','bold','alignment'].indexOf(property) >= 0) section.useTypography = true; }
        if (n.hasAttribute('data-design')) {
          var path = n.getAttribute('data-design').split('.'), obj = path.length === 2 ? selected()[path[0]] : selected(); obj[path[path.length - 1]] = value;
        }
      };
      host.onchange = function (event) {
        var n = event.target;
        if (n.hasAttribute('data-template')) { state.selected = n.value; ++sequence; editor(); var img = host.querySelector('[data-preview-image]'); img.hidden = true; }
        if (n.getAttribute('data-design') === 'paper.preset') {
          var p = selected().paper; p.preset = n.value;
          if (p.preset === '58mm' || p.preset === '80mm') p.widthMm = p.preset === '58mm' ? 58 : 80;
          if (p.preset === 'A4' || p.preset === 'A5') { p.widthMm = p.preset === 'A4' ? 210 : 148; p.heightMm = p.preset === 'A4' ? 297 : 210; p.continuous = false; }
          editor();
        }
        if (n.hasAttribute('data-logo') && n.files && n.files[0]) {
          var file = n.files[0], target = selected(); if (file.type !== 'image/png' || file.size > 130000) { report(new Error('اختر PNG حتى 130 كيلوبايت.')); return; }
          var reader = new FileReader(); reader.onload = function () { target.logoPngBase64 = String(reader.result).split(',')[1]; if (active(context) && target === selected()) { ++sequence; host.querySelector('[data-preview-image]').hidden = true; } }; reader.onerror = function () { report(new Error('تعذر قراءة الشعار.')); }; reader.readAsDataURL(file);
        }
      };
      host.onclick = async function (event) {
        var b = event.target.closest('button'); if (!b || saving) return;
        if (b.hasAttribute('data-save-printing')) { await save(b); return; }
        if (b.hasAttribute('data-reload-printing')) { delete state.settings; await load(); return; }
        if (b.hasAttribute('data-refresh-printers')) { await discovery.refresh(); return; }
        if (b.hasAttribute('data-test')) {
          b.disabled = true; try { var job = await Api.request('/api/printing/test/' + b.getAttribute('data-test'), { method: 'POST', body: JSON.stringify({operationKey:key()}) }); if (active(context)) UI.toast(job.message, 'success'); } catch (e) { report(e); } finally { b.disabled = false; } return;
        }
        if (b.hasAttribute('data-preview-template')) {
          var current = ++sequence; b.disabled = true;
          try { var preview = await Api.request('/api/printing/preview', { method: 'POST', body: JSON.stringify(selected()) }); if (active(context) && current === sequence) { var image = host.querySelector('[data-preview-image]'); image.src = 'data:image/png;base64,' + preview.pngBase64; image.hidden = false; } } catch (e) { report(e); } finally { b.disabled = false; } return;
        }
        if (b.hasAttribute('data-load-jobs')) {
          b.disabled = true; try { var jobs = await Api.request('/api/printing/jobs'); if (active(context)) host.querySelector('[data-print-jobs]').innerHTML = '<div class="table-wrap"><table class="data-table"><thead><tr><th>المستند</th><th>الطابعة / القالب</th><th>الوقت</th><th>الحالة</th><th>التفاصيل</th></tr></thead><tbody>' + jobs.map(function (j) { return '<tr><td>' + esc(j.reference) + (j.reprint ? ' (إعادة طباعة)' : '') + '</td><td>' + esc(j.printerName) + ' / ' + esc(j.templateId) + '</td><td>' + numericHtml(dateTime(j.createdUtc)) + '</td><td>' + esc(statuses[j.status]) + '</td><td>' + esc(j.message) + (j.batchSummaryJson ? '<details><summary>بنود الدفعة</summary><code>' + esc(j.batchSummaryJson) + '</code></details>' : '') + (j.status === 'accepted' ? '<button class="button" data-review-job="' + j.id + '">تأكيد مراجعة قائمة Windows (بعد 10 دقائق)</button>' : '') + '</td></tr>'; }).join('') + '</tbody></table></div>'; } catch (e) { report(e); } finally { b.disabled = false; } return;
        }
        if(b.hasAttribute('data-review-job')){b.disabled=true;try{var review=await Api.request('/api/printing/jobs/'+b.getAttribute('data-review-job')+'/review',{method:'POST',body:'{}'});if(active(context))UI.toast(review.message,'warning');}catch(e){report(e);}finally{b.disabled=false;}return;}
        if (b.hasAttribute('data-clear-logo')) { selected().logoPngBase64 = null; editor(); return; }
        if (b.hasAttribute('data-move')) { var from = Number(b.getAttribute('data-move')), to = from + Number(b.getAttribute('data-direction')), sections = selected().sections; if (to >= 0 && to < sections.length) { var temp = sections[from]; sections[from] = sections[to]; sections[to] = temp; editor(); } return; }
        if (b.hasAttribute('data-new-template') || b.hasAttribute('data-copy-template')) {
          if (state.settings.templates.length >= 40) { report(new Error('الحد الأقصى 40 قالبًا.')); return; }
          var template = JSON.parse(JSON.stringify(b.hasAttribute('data-copy-template') ? selected() : defaults.templates[0])); template.id = 'custom-' + key(); template.name = b.hasAttribute('data-copy-template') ? 'نسخة من ' + template.name : 'قالب جديد'; state.settings.templates.push(template); state.selected = template.id; draw(); return;
        }
        if (b.hasAttribute('data-delete-template')) {
          var id = selected().id; if (state.settings.profiles.some(function (p) { return p.templateId === id; }) || state.settings.templates.length === 1) { report(new Error('القالب مستخدم. اختر بديلًا للمستندات قبل حذفه.')); return; }
          state.settings.templates = state.settings.templates.filter(function (t) { return t.id !== id; }); state.selected = state.settings.templates[0].id; draw(); return;
        }
        if (b.hasAttribute('data-restore-template')) { var old = selected(), restored = JSON.parse(JSON.stringify(defaults.templates.find(function (t) { return t.id === old.id; }) || defaults.templates[0])); restored.id = old.id; restored.name = old.name; state.settings.templates[state.settings.templates.indexOf(old)] = restored; draw(); }
      };
    }
    async function load() {
      try {
        var result = await Promise.all([state.settings ? Promise.resolve(state.settings) : Api.request('/api/printing/settings'), Api.request('/api/printing/defaults')]);
        if (!currentView(context)) return;
        state.settings = result[0]; defaults = result[1]; draw();
      } catch (error) { report(error); }
    }
    load();
    return updateDiscovery;
  }
  global.AlKhayalPrinting = { submit: submit, mount: mount, supplierReceipts: supplierReceipts };
  function managedSettings(screen, container, context) {
    var windows = [], config, company, busy = false, loading = false, previewSerial = 0, savedSnapshot = '';
    var brandingError = '', brandingRequest, updateLabels, updateAdvanced, updateProfile, updateDocument;
    var discovery = { devices: windows, phase: 'idle', pending: false, refresh: refresh, message: discoveryMessage };
    function discoveryMessage() {
      if (discovery.pending) return 'اكتشاف طابعات Windows جارٍ…';
      if (discovery.phase === 'failed') return 'فشل تحديث قائمة طابعات Windows. أعد المحاولة.';
      return windows.length ? 'الطابعات المكتشفة في Windows: ' + windows.length : 'لا توجد طابعات Windows مكتشفة.';
    }
    function updateDiscovery() {
      var button = host.querySelector('[data-refresh-printers]');
      if (button) {
        button.disabled = discovery.pending;
        button.textContent = discovery.pending ? 'جارٍ تحديث طابعات Windows…' : 'تحديث طابعات Windows';
        host.querySelector('[data-windows-status]').textContent = discoveryMessage();
        host.querySelectorAll('[data-device-status]').forEach(function (node) {
          var found = windows.some(function (p) { return p.name === node.getAttribute('data-device-status'); });
          node.textContent = deviceStatus(discovery.phase, found);
        });
      }
      if (updateProfile) updateProfile();
      if (updateLabels) updateLabels(windows, discovery.phase);
      if (updateAdvanced) updateAdvanced();
    }
    function brandingMessage() {
      return brandingError || 'جارٍ تحميل بيانات الشركة للمعاينة…';
    }
    function loadBranding() {
      if (brandingRequest) return brandingRequest;
      brandingError = '';
      brandingRequest = Api.request('/api/settings/company').then(function (result) {
        company = result;
      }, function () {
        brandingError = 'تعذر تحميل بيانات الشركة للمعاينة. أعد المحاولة.';
      }).finally(function () {
        brandingRequest = null;
        if (currentView(context)) {
          updateBranding();
          if (updateProfile) updateProfile(true);
          if (updateDocument) updateDocument();
        }
      });
      updateBranding();
      return brandingRequest;
    }
    function updateBranding() {
      var status = host.querySelector('[data-branding-status]'), retry = host.querySelector('[data-retry-branding]');
      if (!status) return;
      status.textContent = company ? '' : brandingMessage();
      retry.hidden = !brandingError;
    }
    container.innerHTML = UI.pageHeader(screen) + '<section class="card"><div class="card-body" data-printer-manager><p role="status">جارٍ تحميل الطابعات…</p></div><div data-barcode-assignment></div></section>';
    var host = container.querySelector('[data-printer-manager]');
    var labelPanel = container.querySelector('[data-barcode-assignment]');
    if (global.AlKhayalLabels) updateLabels = global.AlKhayalLabels.mountSettings(labelPanel, context, windows);
    function message(error) { if (active(context)) UI.toast(error.message || 'تعذر إكمال العملية.', 'danger'); }
    function configured() { return config.printers || (config.printers = []); }
    function template(item) { return config.templates.find(function (t) { return t.id === item.templateId; }); }
    function assigned(item) { return config.profiles.filter(function (p) { return p.printerId === item.id; }); }
    function title(type) { var index = types.indexOf(type); return index < 0 ? type : titles[index]; }
    function cards() {
      var rows = configured();
      if (!rows.length) return '<div class="empty-state"><strong>لا توجد طابعات مضافة إلى The Cashier</strong><span>أضف طابعة مثبتة في Windows، ثم عيّنها للمستندات.</span><button class="button primary" data-add-printer>إضافة طابعة</button></div>';
      return '<div class="printer-grid">' + rows.map(function (item) { var t = template(item); return '<article class="printer-card"><div><h3>' + esc(item.name) + '</h3><p>' + esc(item.windowsPrinterName) + '</p><small>' + esc(t ? t.paper.preset + ' · ' + t.paper.widthMm + ' مم' : 'قالب مفقود') + '</small></div><small data-device-status="' + esc(item.windowsPrinterName) + '"></small><p>' + esc(assigned(item).map(function (p) { return title(p.documentType); }).join('، ') || 'غير معيّنة') + '</p><div class="printing-actions"><button class="button" data-edit-printer="' + esc(item.id) + '">تعديل</button><button class="button" data-preview-printer="' + esc(item.id) + '">معاينة</button><button class="button" data-test-printer="' + esc(item.id) + '">طباعة صفحة اختبار</button><button class="button" data-remove-printer="' + esc(item.id) + '">إزالة</button></div></article>'; }).join('') + '</div>';
    }
    function draw() {
      if (!currentView(context)) return;
      var advanced = host.querySelector('[data-printer-advanced]');
      host.innerHTML = '<div class="printing-actions printing-actions-field-row"><label class="field">تصميم مستند<select class="select" data-designer-type>' + types.map(function (value) { return option(value, title(value), 'sale'); }).join('') + '</select></label><button class="button primary" data-open-designer>فتح المصمم البصري</button><button class="button" data-add-printer>إضافة طابعة</button><button class="button" data-refresh-printers>تحديث طابعات Windows</button><button class="button" data-reload-printers>إعادة تحميل المحفوظ</button></div>' +
        '<p role="status" data-windows-status></p><p role="status" data-branding-status></p><button class="button" data-retry-branding hidden>إعادة تحميل بيانات الشركة</button><h2>الطابعات المسجلة في البرنامج</h2>' + cards() +
        '<section class="printer-assignments"><h2>تعيين الطابعات</h2><p>الطابعة الافتراضية هنا لكل نوع مستند داخل The Cashier؛ لا تتغير طابعة Windows الافتراضية.</p><div class="printer-assignment-grid">' + config.profiles.map(function (p, index) { var legacy = !p.printerId && p.printerName; return '<div class="field"><label><span>' + esc(title(p.documentType)) + '</span><select class="select" data-assignment="' + index + '">' + option('', legacy ? 'القديم: ' + p.printerName : 'غير معيّنة', p.printerId || '') + configured().map(function (item) { return option(item.id, item.name + ' — ' + item.windowsPrinterName, p.printerId); }).join('') + '</select></label>' + (legacy ? '<small>الإعداد القديم يعمل إلى أن تختار طابعة مضافة.</small>' : '') + '<button class="button" type="button" data-design-document="' + esc(p.documentType) + '">تصميم ' + esc(title(p.documentType)) + '</button></div>'; }).join('') + '</div><div data-barcode-assignment></div><label class="field"><span><input type="checkbox" data-auto-sale' + (config.autoPrintSale ? ' checked' : '') + '> طباعة فاتورة البيع تلقائيًا بعد الترحيل</span></label><label class="field"><span><input type="checkbox" data-auto-delivery' + (config.autoPrintDelivery !== false ? ' checked' : '') + '> طباعة بون التوصيل منفصلًا تلقائيًا</span></label><div class="printing-actions"><button class="button primary" data-save-assignments>حفظ التعيينات</button></div></section>' +
        '<details class="printer-advanced" data-printer-advanced><summary>إعدادات متقدمة</summary><p>القوالب القديمة والتعديلات الدقيقة ما زالت محفوظة. يمكنك فتح المحرر التفصيلي عند الحاجة.</p><div data-legacy-editor></div></details>' +
        '<details><summary>آخر 100 مهمة طباعة</summary><button class="button" data-load-jobs>تحديث السجل</button><div data-print-jobs></div></details>';
      var labelSlot = host.querySelector('[data-barcode-assignment]');
      labelSlot.replaceWith(labelPanel);
      var advancedSlot = host.querySelector('[data-printer-advanced]');
      if (advanced) advancedSlot.replaceWith(advanced);
      else advancedSlot.ontoggle = function () {
        if (!this.open || this.dataset.loaded) return;
        this.dataset.loaded = 'true';
        updateAdvanced = settings(screen, this.querySelector('[data-legacy-editor]'), { state: {}, isActive: context.isActive, isCurrent: context.isCurrent }, discovery);
      };
      updateDiscovery();
      updateBranding();
      host.onchange = function (event) { var n = event.target; if (n.hasAttribute('data-assignment')) { var p = config.profiles[Number(n.getAttribute('data-assignment'))]; p.printerId = n.value || null; } if (n.hasAttribute('data-auto-sale')) config.autoPrintSale = n.checked; if (n.hasAttribute('data-auto-delivery')) config.autoPrintDelivery = n.checked; };
      host.onclick = click;
    }
    function lockDialog(element) {
      var controls = Array.from(element.querySelectorAll('button,input,select,textarea'));
      var previous = controls.map(function (control) { return control.disabled; });
      controls.forEach(function (control) { control.disabled = true; });
      return function () { controls.forEach(function (control, i) { control.disabled = previous[i]; }); };
    }
    async function save(candidate, errorField) {
      if (busy) return false;
      busy = true;
      var controls = Array.from(host.querySelectorAll('[data-assignment], [data-auto-sale], [data-auto-delivery], [data-save-assignments]'));
      controls.forEach(function (control) { control.disabled = true; });
      try { config = await Api.request('/api/printing/settings', { method: 'PUT', body: JSON.stringify(candidate || config) }); savedSnapshot = JSON.stringify(config); if (active(context)) UI.toast('تم حفظ إعدادات الطباعة.', 'success'); draw(); return true; }
      catch (error) {
        if (errorField) errorField.textContent = error.message || 'تعذر حفظ الإعدادات. أعد المحاولة.';
        else message(error);
        return false;
      }
      finally { busy = false; controls.forEach(function (control) { control.disabled = false; }); }
    }
    async function refresh() {
      if (discovery.pending) return;
      discovery.pending = true;
      discovery.phase = discovery.phase === 'idle' ? 'loading' : 'refreshing';
      updateDiscovery();
      try {
        windows = await discoverPrinters();
        discovery.phase = windows.length ? 'loaded' : 'empty';
      } catch (error) {
        windows = [];
        discovery.phase = 'failed';
      } finally {
        discovery.devices = windows;
        discovery.pending = false;
        if (currentView(context)) updateDiscovery();
      }
    }
    function paperOptions(value) { return ['58mm', '80mm', 'A4', 'A5', 'custom'].map(function (p) { return option(p, p === 'custom' ? 'مخصص' : p, value); }).join(''); }
    function documentDesigner(type, seed) {
      var profile = config.profiles.find(function (p) { return p.documentType === type; }); if (!profile) return;
      var assignedPrinter = configured().find(function (p) { return p.id === profile.printerId; });
      var baseId = profile.useDocumentTemplate || !assignedPrinter ? profile.templateId : assignedPrinter.templateId;
      var base = config.templates.find(function (t) { return t.id === baseId; }) || config.templates[0];
      var templateId = profile.useDocumentTemplate ? profile.templateId : 'document-template-' + type;
      var initialDraft = JSON.parse(JSON.stringify(base)); initialDraft.id = templateId; initialDraft.name = title(type);
      if (!profile.useDocumentTemplate) initialDraft.enableExtendedItemDetails = true;
      var draft = seed || JSON.parse(JSON.stringify(initialDraft)), initial = JSON.stringify(initialDraft), sequence = 0, timer = null, closed = false, logoPending = false, selectedId = null, searchSequence = 0, searchTimer = null;
      var hasItems = ['sale','delivery','sales-return','purchase','purchase-return','shift-summary','shift-closing'].indexOf(type) >= 0;
      var itemKeys = type === 'sale' ? ['items','variant','quantity','unitPrice','discount','tax','lineTotal'] : type === 'purchase' ? ['items','variant','quantity','unitPrice','lineTotal'] : type === 'delivery' ? ['items','variant','quantity'] : ['items','variant','quantity','lineTotal'];
      var partyKeys = type === 'customer-payment' ? ['party','partyPhone'] : type === 'supplier-payment' ? ['party','partyPhone','paymentMethod'] : type.indexOf('shift-') === 0 ? ['cashier','party','warehouse'] : type.indexOf('return') >= 0 ? ['cashier','party','warehouse','paymentMethod'] : ['cashier','party','partyPhone','warehouse','paymentMethod'];
      var totalKeys = type === 'sale' ? ['subtotal','discountTotal','taxTotal','grandTotal','paid','remaining'] : type === 'purchase' || type.indexOf('-payment') >= 0 ? ['grandTotal'] : ['grandTotal','paid','remaining'];
      var footerKeys = type === 'purchase' || type.indexOf('-payment') >= 0 ? ['paymentNotes','footer','policy','signature'] : ['footer','policy','signature'];
      var groups = [
        ['هوية المنشأة والرأس', ['company','logo','address','phone','taxInfo','header','title','reference','date']],
        ['الأطراف والعناصر', hasItems ? partyKeys.concat(itemKeys) : partyKeys],
        ['الأسعار والإجماليات', totalKeys],
        ['التذييل', footerKeys]
      ];
      function sectionRows(keys) { return keys.slice().sort(function (a, b) { return draft.sections.findIndex(function (s) { return s.key === a; }) - draft.sections.findIndex(function (s) { return s.key === b; }); }).map(function (key) { var part = draft.sections.find(function (s) { return s.key === key; }); if (!part) return ''; return '<div class="receipt-section-row"><label><input type="checkbox" data-doc-key="' + key + '" data-doc-prop="visible"' + (part.visible ? ' checked' : '') + '> ' + esc(sectionNames[key]) + '</label><label>الخط <input class="input" type="number" min="6" max="24" step="0.5" value="' + part.fontSize + '" data-doc-key="' + key + '" data-doc-prop="fontSize"></label><label>مسافة مم <input class="input" type="number" min="0" max="15" step="0.5" value="' + part.spacingMm + '" data-doc-key="' + key + '" data-doc-prop="spacingMm"></label><label>التسمية <input class="input" maxlength="80" value="' + esc(part.label || '') + '" data-doc-key="' + key + '" data-doc-prop="label" placeholder="الافتراضية"></label><label><input type="checkbox" data-doc-key="' + key + '" data-doc-prop="bold"' + (part.bold ? ' checked' : '') + '> عريض</label><label>محاذاة <select class="select" data-doc-key="' + key + '" data-doc-prop="alignment">' + ['right','center','left'].map(function (value, index) { return option(value, ['يمين','وسط','يسار'][index], part.alignment); }).join('') + '</select></label><div class="receipt-move"><button class="button" type="button" data-doc-move="' + key + '" data-direction="-1" aria-label="نقل ' + esc(sectionNames[key]) + ' للأعلى">↑</button><button class="button" type="button" data-doc-move="' + key + '" data-direction="1" aria-label="نقل ' + esc(sectionNames[key]) + ' للأسفل">↓</button></div></div>'; }).join(''); }
      var body = '<div class="receipt-designer-grid"><div class="receipt-designer-controls"><label class="field">نوع المستند<select class="select" data-doc-type>' + types.map(function (value) { return option(value, title(value), type); }).join('') + '</select></label><details open><summary>الطابعة والورق</summary><div class="printer-modal-fields"><label class="field">مقاس الورق<select class="select" data-doc-paper>' + paperOptions(draft.paper.preset) + '</select></label><label class="field">العرض مم<input class="input" type="number" min="30" max="420" data-doc-width value="' + draft.paper.widthMm + '"></label><label class="field">ارتفاع / أقصى طول مم<input class="input" type="number" min="30" max="3000" data-doc-paper-number="heightMm" value="' + draft.paper.heightMm + '"></label><label class="field">هامش اليمين مم<input class="input" type="number" min="0" max="100" data-doc-margin="marginRightMm" value="' + draft.paper.marginRightMm + '"></label><label class="field">هامش اليسار مم<input class="input" type="number" min="0" max="100" data-doc-margin="marginLeftMm" value="' + draft.paper.marginLeftMm + '"></label><label class="field">هامش الأعلى مم<input class="input" type="number" min="0" max="100" data-doc-margin="marginTopMm" value="' + draft.paper.marginTopMm + '"></label><label class="field">هامش الأسفل مم<input class="input" type="number" min="0" max="100" data-doc-margin="marginBottomMm" value="' + draft.paper.marginBottomMm + '"></label><label class="field">مقياس الخط<input class="input" type="number" min="0.5" max="2" step="0.05" data-doc-paper-number="scale" value="' + draft.paper.scale + '"></label><label><input type="checkbox" data-doc-paper-flag="continuous"' + (draft.paper.continuous ? ' checked' : '') + '> ورق حراري متصل إن دعمه التعريف</label></div></details>' +
        '<details><summary>الشعار والنصوص</summary><label class="field">شعار PNG حتى 130 كيلوبايت<input type="file" accept="image/png" data-doc-logo></label><label class="field">عرض الشعار مم<input class="input" type="number" min="1" max="100" data-doc-number="logoWidthMm" value="' + draft.logoWidthMm + '"></label><label class="field">ارتفاع الشعار مم<input class="input" type="number" min="1" max="100" data-doc-number="logoHeightMm" value="' + draft.logoHeightMm + '"></label><label class="field">نص الرأس<textarea class="input" maxlength="1000" data-doc-text="headerText">' + esc(draft.headerText || '') + '</textarea></label><label class="field">نص التذييل<textarea class="input" maxlength="1000" data-doc-text="footerText">' + esc(draft.footerText || '') + '</textarea></label><label class="field">سياسة الإرجاع<textarea class="input" maxlength="1000" data-doc-text="returnPolicy">' + esc(draft.returnPolicy || '') + '</textarea></label><label class="field">تسمية التوقيع<input class="input" maxlength="80" data-doc-text="signatureText" value="' + esc(draft.signatureText || 'التوقيع') + '"></label><label><input type="checkbox" data-doc-flag="dividers"' + (draft.dividers ? ' checked' : '') + '> فواصل بين البنود</label><label><input type="checkbox" data-doc-flag="showReferenceBarcode"' + (draft.showReferenceBarcode ? ' checked' : '') + '> باركود رقم المستند إذا اتسع الورق</label>' + (hasItems ? '<label><input type="checkbox" data-doc-flag="showSku"' + (draft.showSku ? ' checked' : '') + '> إظهار SKU إذا كان متاحًا</label>' : '') + '</details>' +
        groups.map(function (g, i) { return '<details' + (i === 0 ? ' open' : '') + '><summary>' + g[0] + '</summary>' + sectionRows(g[1]) + '</details>'; }).join('') + '</div>' +
        '<aside class="printing-preview" aria-label="معاينة تصميم المستند" tabindex="0"><strong>' + esc(title(type)) + '</strong><p>المعاينة من محرّك الطباعة الفعلي. احفظ التصميم ليطبق على الورق.</p><label class="field">مصدر المعاينة<select class="select" data-doc-mode><option value="sample">بيانات نموذجية</option><option value="actual">مستند محفوظ</option></select></label><div data-doc-picker hidden><label class="field">ابحث برقم المستند أو اسم العميل / المورد<input class="input" type="search" maxlength="100" autocomplete="off" data-doc-search></label><div class="receipt-record-results" data-doc-results role="listbox"></div><p data-doc-selected role="status">اختر مستندًا محفوظًا من النتائج.</p></div><label class="field">تكبير العرض فقط<input class="input" type="range" min="50" max="150" value="100" data-doc-zoom></label><p role="status" data-doc-status></p><img alt="معاينة المستند" data-doc-image hidden></aside></div><p class="field-error" role="alert" data-doc-error></p>';
      var modalElement;
      function searchDocuments(immediate) {
        var current = ++searchSequence; if (searchTimer !== null) global.clearTimeout(searchTimer);
        if (!modalElement || closed) return;
        var results = modalElement.querySelector('[data-doc-results]'); results.innerHTML = '<p role="status">جارٍ البحث…</p>';
        searchTimer = global.setTimeout(async function () {
          searchTimer = null;
          try { var term = modalElement.querySelector('[data-doc-search]').value.trim(); var rows = await Api.request('/api/printing/documents?type=' + encodeURIComponent(type) + '&q=' + encodeURIComponent(term));
            if (closed || current !== searchSequence) return;
            results.innerHTML = rows.length ? rows.map(function (row) { return '<button type="button" class="receipt-record-choice" role="option" data-doc-choice="' + Number(row.id) + '"><strong>' + esc(row.reference) + '</strong><span>' + esc(row.party || '') + '</span><small>' + esc(row.date || '') + (row.amount ? ' · ' + esc(row.amount) : '') + '</small></button>'; }).join('') : '<p role="status">لا توجد مستندات محفوظة مطابقة.</p>';
          } catch (error) { if (!closed && current === searchSequence) results.innerHTML = '<p role="alert">' + esc(error.message || 'تعذر البحث عن المستندات.') + '</p>'; }
        }, immediate ? 0 : 250);
      }
      function preview(immediate) { var current = ++sequence; if (timer !== null) global.clearTimeout(timer); if (!modalElement || closed) return; modalElement.querySelector('[data-doc-status]').textContent = 'جارٍ تحديث المعاينة…'; var existing = modalElement.querySelector('[data-doc-image]'); if (!existing.hidden) existing.setAttribute('data-stale', 'true'); timer = global.setTimeout(async function () {
        timer = null; if (closed || current !== sequence) return;
        var actual = modalElement.querySelector('[data-doc-mode]').value === 'actual';
        if (actual && !selectedId) { modalElement.querySelector('[data-doc-status]').textContent = 'اختر مستندًا محفوظًا من نتائج البحث.'; modalElement.querySelector('[data-doc-image]').hidden = true; return; }
        if (!company) { modalElement.querySelector('[data-doc-status]').textContent = brandingMessage(); return; }
        try { var response = await Api.request(actual ? '/api/printing/preview/document' : '/api/printing/preview/branding', { method: 'POST', body: JSON.stringify({ template: draft, documentType: type, documentId: actual ? selectedId : null, businessName: company.businessName, address: company.address, phone: company.phone }) });
          if (closed || current !== sequence) return; var image = modalElement.querySelector('[data-doc-image]'); image.src = 'data:image/png;base64,' + response.pngBase64; image.hidden = false; image.removeAttribute('data-stale'); modalElement.querySelector('[data-doc-status]').textContent = actual ? 'مستند محفوظ — بيانات فعلية' : 'بيانات نموذجية — غير محفوظة'; modalElement.querySelector('[data-doc-error]').textContent = ''; }
        catch (error) { if (!closed && current === sequence) { modalElement.querySelector('[data-doc-error]').textContent = error.message || 'تعذر إنشاء المعاينة.'; modalElement.querySelector('[data-doc-status]').textContent = 'المعاينة السابقة لا تمثل التعديل الحالي.'; } }
      }, immediate ? 0 : 320); }
      UI.modal({ title: 'تصميم ' + title(type), wide: true, body: body, actions: '<button class="button primary" data-doc-save>حفظ التصميم</button><button class="button" data-doc-test>طباعة اختبار</button><button class="button" data-doc-reset>استعادة الافتراضي</button><button class="button" data-modal-close>إلغاء</button>', beforeClose: function () { if (busy) return false; if (closed) return true; if (JSON.stringify(draft) !== initial && global.confirm && !global.confirm('توجد تعديلات غير محفوظة. هل تريد إلغاءها؟')) return false; closed = true; updateDocument = null; ++sequence; ++searchSequence; if (timer !== null) global.clearTimeout(timer); if (searchTimer !== null) global.clearTimeout(searchTimer); return true; }, onOpen: function (element) {
        modalElement = element; updateDocument = function () { if (!closed && element.isConnected) preview(true); }; element.className += ' receipt-designer-modal'; var grid = element.querySelector('[class="receipt-designer-grid"]');
        element.querySelector('[data-doc-type]').onchange = function () { if (JSON.stringify(draft) !== initial && global.confirm && !global.confirm('توجد تعديلات غير محفوظة. هل تريد الانتقال إلى مستند آخر؟')) { this.value = type; return; } var next = this.value; closed = true; ++sequence; if (timer !== null) global.clearTimeout(timer); if (searchTimer !== null) global.clearTimeout(searchTimer); UI.closeModal(); documentDesigner(next); };
        grid.addEventListener('input', function (event) { var n = event.target, part = n.hasAttribute('data-doc-key') ? draft.sections.find(function (s) { return s.key === n.getAttribute('data-doc-key'); }) : null;
          if (part) { var property = n.getAttribute('data-doc-prop'); part[property] = n.type === 'checkbox' ? n.checked : n.type === 'number' ? Number(n.value) : n.value; if (['fontSize','spacingMm','bold','alignment'].indexOf(property) >= 0) part.useTypography = true; }
          if (n.hasAttribute('data-doc-text')) draft[n.getAttribute('data-doc-text')] = n.value;
          if (n.hasAttribute('data-doc-flag')) draft[n.getAttribute('data-doc-flag')] = n.checked;
          if (n.hasAttribute('data-doc-number')) draft[n.getAttribute('data-doc-number')] = Number(n.value);
          if (n.hasAttribute('data-doc-width')) draft.paper.widthMm = Number(n.value);
          if (n.hasAttribute('data-doc-margin')) draft.paper[n.getAttribute('data-doc-margin')] = Number(n.value);
          if (n.hasAttribute('data-doc-paper-number')) draft.paper[n.getAttribute('data-doc-paper-number')] = Number(n.value);
          if (n.hasAttribute('data-doc-paper-flag')) draft.paper[n.getAttribute('data-doc-paper-flag')] = n.checked;
          if (n.hasAttribute('data-doc-zoom')) { element.querySelector('[data-doc-image]').style.width = n.value + '%'; return; }
          if (part || n.hasAttribute('data-doc-text') || n.hasAttribute('data-doc-flag') || n.hasAttribute('data-doc-number') || n.hasAttribute('data-doc-width') || n.hasAttribute('data-doc-margin') || n.hasAttribute('data-doc-paper-number') || n.hasAttribute('data-doc-paper-flag')) preview(false);
        });
        grid.addEventListener('click', function (event) { var button = event.target.closest('[data-doc-move]'); if (!button) return; var key = button.getAttribute('data-doc-move'), group = groups.find(function (g) { return g[1].indexOf(key) >= 0; }); if (!group) return; var ordered = group[1].slice().sort(function (a, b) { return draft.sections.findIndex(function (s) { return s.key === a; }) - draft.sections.findIndex(function (s) { return s.key === b; }); }); var current = ordered.indexOf(key), next = current + Number(button.getAttribute('data-direction')); if (next < 0 || next >= ordered.length) return; var from = draft.sections.findIndex(function (s) { return s.key === key; }), to = draft.sections.findIndex(function (s) { return s.key === ordered[next]; }); var temp = draft.sections[from]; draft.sections[from] = draft.sections[to]; draft.sections[to] = temp; draft.useSectionOrder = true; closed = true; ++sequence; if (timer !== null) global.clearTimeout(timer); if (searchTimer !== null) global.clearTimeout(searchTimer); UI.closeModal(); documentDesigner(type, draft); });
        element.querySelector('[data-doc-mode]').onchange = function () { var actual = this.value === 'actual'; element.querySelector('[data-doc-picker]').hidden = !actual; if (actual) searchDocuments(true); preview(false); };
        element.querySelector('[data-doc-search]').oninput = function () { selectedId = null; element.querySelector('[data-doc-selected]').textContent = 'اختر مستندًا محفوظًا من النتائج.'; searchDocuments(false); preview(false); };
        element.querySelector('[data-doc-results]').onclick = function (event) { var choice = event.target.closest('[data-doc-choice]'); if (!choice) return; selectedId = Number(choice.getAttribute('data-doc-choice')); element.querySelector('[data-doc-selected]').textContent = 'المستند المحدد: ' + Array.prototype.map.call(choice.children, function (part) { return part.textContent.trim(); }).filter(Boolean).join(' — '); preview(true); };
        element.querySelector('[data-doc-paper]').onchange = function () { draft.paper.preset = this.value; var widths = { '58mm':58, '80mm':80, A4:210, A5:148 }; if (widths[this.value]) { draft.paper.widthMm = widths[this.value]; element.querySelector('[data-doc-width]').value = widths[this.value]; } if (this.value === 'A4' || this.value === 'A5') { draft.paper.heightMm = this.value === 'A4' ? 297 : 210; draft.paper.continuous = false; element.querySelector('[data-doc-paper-number="heightMm"]').value = draft.paper.heightMm; element.querySelector('[data-doc-paper-flag="continuous"]').checked = false; } preview(false); };
        element.querySelector('[data-doc-logo]').onchange = function () { var file = this.files && this.files[0]; if (!file) return; if (file.type !== 'image/png' || file.size > 130000) { element.querySelector('[data-doc-error]').textContent = 'اختر PNG حتى 130 كيلوبايت.'; return; } logoPending = true; var reader = new FileReader(); reader.onload = function () { logoPending = false; if (closed) return; draft.logoPngBase64 = String(reader.result).split(',')[1]; preview(false); }; reader.onerror = function () { logoPending = false; element.querySelector('[data-doc-error]').textContent = 'تعذر قراءة الشعار.'; }; reader.readAsDataURL(file); };
        element.querySelector('[data-doc-save]').onclick = async function () {
          if (busy || !currentView(context)) return;
          var error = element.querySelector('[data-doc-error]');
          if (logoPending) { error.textContent = 'انتظر قراءة الشعار.'; return; }
          var candidate = JSON.parse(JSON.stringify(config));
          var index = candidate.templates.findIndex(function (t) { return t.id === templateId; });
          if (index < 0) candidate.templates.push(draft); else candidate.templates[index] = draft;
          var assignment = candidate.profiles.find(function (p) { return p.documentType === type; });
          assignment.templateId = templateId; assignment.useDocumentTemplate = true;
          var unlock = lockDialog(element);
          error.textContent = '';
          if (await save(candidate, error)) {
            closed = true; updateDocument = null;
            if (element.isConnected) UI.closeModal();
          } else unlock();
        };
        element.querySelector('[data-doc-test]').onclick = async function () { if (JSON.stringify(draft) !== initial || !profile.useDocumentTemplate) { element.querySelector('[data-doc-error]').textContent = 'احفظ التصميم أولًا ثم أعد فتحه لطباعة الاختبار.'; return; } if (!await UI.confirm({ title: 'طباعة اختبار', message: 'سيُرسل نموذج اختبار إلى الطابعة المعينة لهذا المستند.', confirmText: 'طباعة اختبار' })) return; var button = this; button.disabled = true; try { var job = await Api.request('/api/printing/test/' + type, { method: 'POST', body: JSON.stringify({ operationKey: key() }) }); UI.toast(job.message, job.status === 'submitted_unknown' ? 'success' : 'warning'); } catch (error) { message(error); } finally { button.disabled = false; } };
        element.querySelector('[data-doc-reset]').onclick = function () { var fallback = config.templates.find(function (t) { return t.id === 'builtin-' + type; }); if (!fallback) { element.querySelector('[data-doc-error]').textContent = 'القالب الافتراضي غير متاح.'; return; } var reset = JSON.parse(JSON.stringify(fallback)); reset.id = templateId; reset.name = title(type); closed = true; ++sequence; if (timer !== null) global.clearTimeout(timer); UI.closeModal(); documentDesigner(type, reset); };
        preview(true);
      } });
    }
    function modal(item, showPreview) {
      var editing = !!item, draft = editing ? JSON.parse(JSON.stringify(item)) : { id: 'printer-' + key(), name: '', windowsPrinterName: '', templateId: 'printer-template-' + key() };
      var original = editing ? template(item) : config.templates.find(function (t) { return t.id === config.profiles[0].templateId; }) || config.templates[0];
      var receipt = JSON.parse(JSON.stringify(original)); receipt.id = draft.templateId;
      var chosenPaper = editing ? receipt.paper.preset : '';
      var previewTimer = null, closed = false, logoReadToken = 0, logoPending = false, modalElement = null;
      function cleanup() {
        closed = true; updateProfile = null; ++previewSerial; ++logoReadToken;
        if (previewTimer !== null) global.clearTimeout(previewTimer);
        // Preview and logo use data URLs, so no object URLs are allocated or need revocation.
        if (modalElement) {
          var image = modalElement.querySelector('[data-preview-image]');
          var logo = modalElement.querySelector('[data-logo-preview]');
          if (image) image.removeAttribute('src');
          if (logo) logo.removeAttribute('src');
        }
        return true;
      }
      var layer = UI.modal({ title: editing ? 'تعديل طابعة' : 'إضافة طابعة', wide: true, focusSelector: '[data-friendly]', fallbackFocus: function () { return host.querySelector('[data-add-printer]'); }, beforeClose: function () { return !busy && cleanup(); }, body: '<section><h3>تعريف الطابعة</h3><div class="printer-modal-fields"><label class="field"><span>اسم الطابعة داخل The Cashier</span><input class="input" data-friendly maxlength="100" value="' + esc(draft.name) + '"></label><label class="field"><span>طابعة Windows المثبتة</span><select class="select" data-windows-printer>' + windowsOptions(windows, draft.windowsPrinterName, discovery.phase, true) + '</select></label></div><p role="status" data-profile-discovery></p><button class="button" data-profile-refresh>تحديث طابعات Windows</button></section><section><h3>إعداد الورق</h3><div class="printer-modal-fields"><label class="field"><span>مقاس الورق</span><select class="select" data-paper>' + option('', 'اختر المقاس', chosenPaper) + paperOptions(chosenPaper) + '</select></label><label class="field"><span>عرض الورق بالملليمتر</span><input class="input" type="number" min="30" max="420" data-width value="' + esc(receipt.paper.widthMm) + '"></label><label class="field"><span>ارتفاع الصفحة / أقصى طول بالملليمتر</span><input class="input" type="number" min="30" max="3000" data-height value="' + esc(receipt.paper.heightMm) + '"></label></div></section><section><h3>هوية الإيصال</h3><p>اسم المنشأة والعنوان والهاتف من ملف الشركة؛ تعديلها من إعدادات الشركة.</p><p role="status" data-profile-branding></p><label class="field"><span>شعار المنشأة PNG</span><input type="file" accept="image/png" data-logo></label><label class="field"><span><input type="checkbox" data-show-logo' + (receipt.sections.some(function (s) { return s.key === 'logo' && s.visible; }) ? ' checked' : '') + '> إظهار الشعار</span></label><label class="field"><span><input type="checkbox" data-reference-barcode' + (receipt.showReferenceBarcode ? ' checked' : '') + '> طباعة باركود رقم المستند</span></label><img class="printer-logo-preview" data-logo-preview alt="معاينة الشعار" ' + (receipt.logoPngBase64 ? 'src="data:image/png;base64,' + esc(receipt.logoPngBase64) + '"' : 'hidden') + '><label class="field"><span>رسالة أسفل الإيصال</span><textarea class="input" data-footer maxlength="1000">' + esc(receipt.footerText || '') + '</textarea></label></section><details><summary>إعدادات متقدمة</summary><div class="printer-modal-fields"><label class="field"><span>هامش اليمين مم</span><input class="input" data-margin="marginRightMm" type="number" min="0" max="100" value="' + receipt.paper.marginRightMm + '"></label><label class="field"><span>هامش اليسار مم</span><input class="input" data-margin="marginLeftMm" type="number" min="0" max="100" value="' + receipt.paper.marginLeftMm + '"></label><label class="field"><span>هامش الأعلى مم</span><input class="input" data-margin="marginTopMm" type="number" min="0" max="100" value="' + receipt.paper.marginTopMm + '"></label><label class="field"><span>هامش الأسفل مم</span><input class="input" data-margin="marginBottomMm" type="number" min="0" max="100" value="' + receipt.paper.marginBottomMm + '"></label><label class="field"><span>مقياس الخط</span><input class="input" data-scale type="number" min="0.5" max="2" step="0.05" value="' + receipt.paper.scale + '"></label><label class="field"><span><input type="checkbox" data-continuous' + (receipt.paper.continuous ? ' checked' : '') + '> إيصال بطول متغير إذا دعمه التعريف</span></label></div></details><div class="printing-preview" role="region" aria-label="معاينة الإيصال"><h3>معاينة الإيصال</h3><label class="field"><span>نوع المستند في المعاينة</span><select class="select" data-preview-type>' + types.map(function (type) { return option(type, title(type), 'sale'); }).join('') + '</select></label><button class="button" data-modal-preview>معاينة نموذج الإيصال</button><p>بيانات اختبار فقط؛ المعاينة لا تطبع ولا تمثل فاتورة حقيقية. تعرض الإيصال الحراري كاملاً؛ الأوراق الأخرى تعرض الصفحة الأولى.</p><img data-preview-image alt="معاينة الإيصال" hidden></div><p role="alert" data-modal-error class="field-error"></p>', actions: '<button class="button primary" data-modal-save>حفظ الطابعة</button><button class="button" data-modal-close>إلغاء</button>', onOpen: function (element) {
        var serial = ++previewSerial;
        function read() { draft.name = element.querySelector('[data-friendly]').value.trim(); draft.windowsPrinterName = element.querySelector('[data-windows-printer]').value; receipt.paper.preset = element.querySelector('[data-paper]').value; receipt.paper.widthMm = Number(element.querySelector('[data-width]').value); receipt.paper.heightMm = Number(element.querySelector('[data-height]').value); receipt.footerText = element.querySelector('[data-footer]').value; receipt.headerText = element.querySelector('[data-header]').value; receipt.paper.scale = Number(element.querySelector('[data-scale]').value); receipt.paper.continuous = element.querySelector('[data-continuous]').checked; receipt.sections.find(function (s) { return s.key === 'logo'; }).visible = element.querySelector('[data-show-logo]').checked; receipt.showReferenceBarcode = element.querySelector('[data-reference-barcode]').checked; element.querySelectorAll('[data-margin]').forEach(function (n) { receipt.paper[n.getAttribute('data-margin')] = Number(n.value); }); }
        element.querySelector('[data-paper]').onchange = function () { var preset = this.value, sizes = { '58mm': [58, 297], '80mm': [80, 297], A4: [210, 297], A5: [148, 210] }; if (sizes[preset]) { element.querySelector('[data-width]').value = sizes[preset][0]; element.querySelector('[data-height]').value = sizes[preset][1]; } if (preset === 'A4' || preset === 'A5') element.querySelector('[data-continuous]').checked = false; element.querySelector('[data-preview-image]').hidden = true; };
        element.querySelector('[data-modal-save]').onclick = async function () {
          if (busy || !currentView(context)) return;
          var error = element.querySelector('[data-modal-error]');
          function invalid(text, selector) {
            error.textContent = text;
            if (selector) element.querySelector(selector).focus();
          }
          if (logoPending) return invalid('انتظر حتى تكتمل قراءة الشعار.');
          if (!element.querySelector('[data-paper]').value) return invalid('اختر مقاس الورق.', '[data-paper]');
          read();
          if (!draft.name || !draft.windowsPrinterName) return invalid('أدخل اسمًا واختر طابعة Windows.', draft.name ? '[data-windows-printer]' : '[data-friendly]');
          if (discovery.pending) return invalid('انتظر اكتمال اكتشاف طابعات Windows.', '[data-windows-printer]');
          if (!windows.some(function (p) { return p.name === draft.windowsPrinterName; })) return invalid('طابعة Windows غير متاحة حاليًا. اختر طابعة مثبتة أو حدّث القائمة.', '[data-windows-printer]');
          if (configured().some(function (p) { return p.id !== draft.id && p.windowsPrinterName.toLowerCase() === draft.windowsPrinterName.toLowerCase(); }))
            return invalid('هذه الطابعة مضافة مسبقًا؛ عدّل سجلها الحالي.', '[data-windows-printer]');
          var candidate = JSON.parse(JSON.stringify(config)), index = candidate.printers.findIndex(function (p) { return p.id === draft.id; });
          if (index < 0) candidate.printers.push(draft); else candidate.printers[index] = draft;
          var templateIndex = candidate.templates.findIndex(function (t) { return t.id === receipt.id; });
          if (templateIndex < 0) candidate.templates.push(receipt); else candidate.templates[templateIndex] = receipt;
          var unlock = lockDialog(element);
          error.textContent = '';
          if (await save(candidate, error)) {
            cleanup();
            if (element.isConnected) UI.closeModal();
          } else {
            unlock();
            updateProfile();
          }
        };
        modalElement = element;
        var deviceSelect = element.querySelector('[data-windows-printer]');
        var refreshButton = element.querySelector('[data-profile-refresh]');
        if (refreshButton) refreshButton.onclick = refresh;
        updateProfile = function (brandingChanged) {
          if (closed || !element.isConnected) return;
          var status = element.querySelector('[data-profile-discovery]');
          if (status) status.textContent = discoveryMessage();
          if (refreshButton) refreshButton.disabled = busy || discovery.pending;
          deviceSelect.innerHTML = windowsOptions(windows, deviceSelect.value, discovery.phase, true);
          var branding = element.querySelector('[data-profile-branding]');
          if (branding) branding.textContent = company ? [company.businessName, company.address, company.phone].filter(Boolean).join(' · ') : brandingMessage();
          if (brandingChanged && element.querySelector('[data-paper]').value) queuePreview(true);
        };
        updateProfile();
        function parentOf(node) { return node.parentNode || node.parent; }
        var image = element.querySelector('[data-preview-image]');
        var status = global.document.createElement('p'); status.setAttribute('data-preview-status', ''); status.setAttribute('role', 'status');
        var previewSource = global.document.createElement('div'); previewSource.className = 'printer-modal-fields';
        previewSource.innerHTML = '<label class="field"><span>مصدر المعاينة</span><select class="select" data-preview-mode><option value="sample">بيانات نموذجية — غير محفوظة</option><option value="actual">مستند محفوظ — بيانات فعلية</option></select></label><label class="field"><span>معرّف المستند المحفوظ</span><input class="input" type="number" min="1" step="1" data-actual-document disabled placeholder="رقم المستند في النظام"></label>';
        var previewButton = element.querySelector('[data-modal-preview]');
        parentOf(image).insertBefore(previewSource, previewButton);
        parentOf(image).insertBefore(status, image);
        var sectionEditor = global.document.createElement('details'); sectionEditor.className = 'receipt-section-editor';
        sectionEditor.innerHTML = '<summary>إظهار العناصر والخطوط والمسافات</summary><p>تؤثر الخيارات المحفوظة في الطباعة والمعاينة معًا.</p>' + receipt.sections.map(function (part) {
          return '<div class="receipt-section-row"><label><input type="checkbox" data-section-edit="visible" data-key="' + esc(part.key) + '"' + (part.visible ? ' checked' : '') + '> ' + esc(sectionNames[part.key] || part.key) + '</label><label>الخط <input class="input" type="number" min="6" max="24" step="0.5" value="' + part.fontSize + '" data-section-edit="fontSize" data-key="' + esc(part.key) + '"></label><label>مسافة مم <input class="input" type="number" min="0" max="15" step="0.5" value="' + part.spacingMm + '" data-section-edit="spacingMm" data-key="' + esc(part.key) + '"></label><label>التسمية <input class="input" type="text" maxlength="80" value="' + esc(part.label || '') + '" data-section-edit="label" data-key="' + esc(part.key) + '" placeholder="الافتراضية"></label></div>';
        }).join('');
        element.className += ' printer-profile-modal';
        var modalBody = element.querySelector('.modal-body');
        if (modalBody) {
          var editorGrid = global.document.createElement('div'), controls = global.document.createElement('div');
          editorGrid.className = 'receipt-designer-grid'; controls.className = 'receipt-designer-controls';
          var previewPanel = element.querySelector('.printing-preview'), errorPanel = element.querySelector('[data-modal-error]');
          previewPanel.tabIndex = 0;
          var jump = global.document.createElement('button');
          jump.type = 'button'; jump.className = 'button'; jump.textContent = 'انتقل إلى المعاينة';
          jump.onclick = function () { previewPanel.focus(); };
          controls.appendChild(jump);
          Array.prototype.slice.call(modalBody.children).forEach(function (child) { if (child !== previewPanel && child !== errorPanel) controls.appendChild(child); });
          controls.appendChild(sectionEditor); editorGrid.appendChild(controls); editorGrid.appendChild(previewPanel); modalBody.appendChild(editorGrid);
          var actions = element.querySelector('.modal-actions');
          actions.insertBefore(errorPanel, actions.firstChild);
          editorGrid.addEventListener('input', function (event) { var n = event.target; if (!n.hasAttribute('data-section-edit')) return; var part = receipt.sections.find(function (s) { return s.key === n.getAttribute('data-key'); }); if (!part) return; part[n.getAttribute('data-section-edit')] = n.type === 'checkbox' ? n.checked : n.type === 'number' ? Number(n.value) : n.value; queuePreview(false); });
        }
        previewSource.querySelector('[data-preview-mode]').onchange = function () { previewSource.querySelector('[data-actual-document]').disabled = this.value !== 'actual'; queuePreview(false); };
        previewSource.querySelector('[data-actual-document]').oninput = function () { queuePreview(false); };
        var removeLogo = global.document.createElement('button'); removeLogo.type = 'button'; removeLogo.className = 'button'; removeLogo.setAttribute('data-remove-logo', ''); removeLogo.textContent = 'إزالة الشعار المخصص';
        parentOf(parentOf(element.querySelector('[data-logo]'))).appendChild(removeLogo);
        var headerLabel = global.document.createElement('label'); headerLabel.className = 'field';
        headerLabel.innerHTML = '<span>نص رأس الإيصال</span><textarea class="input" data-header maxlength="1000">' + esc(receipt.headerText || '') + '</textarea>';
        parentOf(parentOf(element.querySelector('[data-footer]'))).appendChild(headerLabel);
        function stale(message) {
          ++previewSerial;
          if (previewTimer !== null) global.clearTimeout(previewTimer);
          if (!image.hidden) image.setAttribute('data-stale', 'true');
          status.textContent = message || 'جارٍ تحديث المعاينة…';
          element.querySelector('[data-modal-error]').textContent = '';
        }
        async function render(current) {
          if (closed || current !== previewSerial) return;
          if (!element.isConnected) { cleanup(); return; }
          if (!element.querySelector('[data-paper]').value) { status.textContent = 'اختر مقاس الورق لعرض المعاينة.'; return; }
          read();
          var actualMode = element.querySelector('[data-preview-mode]').value === 'actual', actualId = Number(element.querySelector('[data-actual-document]').value);
          if (actualMode && (!Number.isSafeInteger(actualId) || actualId <= 0)) { status.textContent = 'أدخل معرّف مستند محفوظ صحيحًا.'; return; }
          if (!company) { status.textContent = brandingMessage(); return; }
          var body = JSON.stringify({ template: receipt, documentType: element.querySelector('[data-preview-type]').value, documentId: actualMode ? actualId : null, businessName: company.businessName, address: company.address, phone: company.phone });
          status.textContent = image.hidden ? 'جارٍ إعداد المعاينة…' : 'جارٍ التحديث — الصورة السابقة لا تمثل التعديلات الحالية.';
          try {
            var result = await Api.request(actualMode ? '/api/printing/preview/document' : '/api/printing/preview/branding', { method: 'POST', body: body });
            if (closed || current !== previewSerial || !element.isConnected) return;
            image.src = 'data:image/png;base64,' + result.pngBase64;
            image.hidden = false; image.removeAttribute('data-stale');
            status.textContent = actualMode ? 'معاينة من بيانات المستند المحفوظ؛ التعديلات لا تطبع حتى الحفظ.' : 'معاينة محدثة ببيانات اختبار، غير محفوظة ولا تطبع.';
          } catch (error) {
            if (closed || current !== previewSerial || !element.isConnected) return;
            status.textContent = image.hidden ? 'تعذر إنشاء المعاينة.' : 'تعذر التحديث؛ الصورة السابقة لا تمثل الإعدادات الحالية.';
            element.querySelector('[data-modal-error]').textContent = error.message || 'تعذر معاينة الإيصال. راجع المقاس والهوامش.';
          }
        }
        function queuePreview(immediate) {
          if (closed) return;
          stale();
          var current = previewSerial;
          previewTimer = global.setTimeout(function () { previewTimer = null; render(current); }, immediate ? 0 : 320);
        }
        element.querySelector('[data-modal-preview]').onclick = function () { queuePreview(true); };
        element.querySelector('[data-paper]').onchange = function () {
          var sizes = { '58mm': [58, 297], '80mm': [80, 297], A4: [210, 297], A5: [148, 210] }, size = sizes[this.value];
          if (size) { element.querySelector('[data-width]').value = size[0]; element.querySelector('[data-height]').value = size[1]; }
          if (this.value === 'A4' || this.value === 'A5') element.querySelector('[data-continuous]').checked = false;
          queuePreview(false);
        };
        element.querySelector('[data-preview-type]').onchange = function () { queuePreview(false); };
        element.querySelector('[data-show-logo]').onchange = function () { queuePreview(false); };
        element.querySelector('[data-reference-barcode]').onchange = function () { queuePreview(false); };
        element.querySelector('[data-continuous]').onchange = function () { queuePreview(false); };
        ['data-width', 'data-height', 'data-footer', 'data-header', 'data-scale'].forEach(function (name) { element.querySelector('[' + name + ']').oninput = function () { queuePreview(false); }; });
        element.querySelectorAll('[data-margin]').forEach(function (node) { node.oninput = function () { queuePreview(false); }; });
        removeLogo.onclick = function () {
          ++logoReadToken; logoPending = false; receipt.logoPngBase64 = null;
          element.querySelector('[data-logo]').value = '';
          var logo = element.querySelector('[data-logo-preview]'); logo.removeAttribute('src'); logo.hidden = true;
          queuePreview(false);
        };
        element.querySelector('[data-logo]').onchange = function () {
          var file = this.files && this.files[0]; if (!file) return;
          if (file.type !== 'image/png' || file.size > 130000) { element.querySelector('[data-modal-error]').textContent = 'اختر صورة PNG حتى 130 كيلوبايت.'; return; }
          stale('جارٍ قراءة الشعار…'); logoPending = true; var token = ++logoReadToken, reader = new FileReader();
          reader.onload = function () {
            if (closed || token !== logoReadToken) return;
            logoPending = false; receipt.logoPngBase64 = String(reader.result).split(',')[1];
            var logo = element.querySelector('[data-logo-preview]'); logo.src = reader.result; logo.hidden = false;
            queuePreview(false);
          };
          reader.onerror = function () { if (!closed && token === logoReadToken) { logoPending = false; status.textContent = 'تعذر تحديث المعاينة.'; element.querySelector('[data-modal-error]').textContent = 'تعذر قراءة الشعار.'; } };
          reader.readAsDataURL(file);
        };
        if (showPreview || editing) queuePreview(true);
        else status.textContent = 'اختر مقاس الورق لعرض معاينة حية ببيانات اختبار.';
      } });
      return layer;
    }
    async function click(event) {
      var b = event.target.closest('button'); if (!b || busy || loading) return;
      if (b.hasAttribute('data-retry-branding')) { loadBranding(); return; }
      if (b.hasAttribute('data-open-designer')) { documentDesigner(host.querySelector('[data-designer-type]').value); return; }
      if (b.hasAttribute('data-design-document')) { documentDesigner(b.getAttribute('data-design-document')); return; }
      if (b.hasAttribute('data-add-printer')) { modal(null, false); return; }
      if (b.hasAttribute('data-refresh-printers')) { await refresh(); return; }
      if (b.hasAttribute('data-reload-printers')) { await load(); return; }
      if (b.hasAttribute('data-save-assignments')) { await save(); return; }
      var id = b.getAttribute('data-edit-printer') || b.getAttribute('data-preview-printer') || b.getAttribute('data-test-printer') || b.getAttribute('data-remove-printer');
      if (id) { var item = configured().find(function (p) { return p.id === id; }); if (!item) return;
        if (b.hasAttribute('data-edit-printer') || b.hasAttribute('data-preview-printer')) { modal(item, b.hasAttribute('data-preview-printer')); return; }
        if (b.hasAttribute('data-remove-printer')) { var links = assigned(item); if (links.length && !await UI.confirm({ title: 'إزالة طابعة معيّنة', message: 'هذه الطابعة معيّنة لـ ' + links.map(function (p) { return title(p.documentType); }).join('، ') + '. ستُزال التعيينات ويتوقف إرسال تلك المستندات حتى تعيّن طابعة أخرى. هل تريد المتابعة؟', confirmText: 'إزالة وإلغاء التعيينات', danger: true })) return; if (!links.length && !await UI.confirm({ title: 'إزالة طابعة', message: 'إزالة ' + item.name + ' من إعدادات The Cashier؟', confirmText: 'إزالة', danger: true })) return; var before = JSON.parse(JSON.stringify(config)); links.forEach(function (p) { p.printerId = null; p.printerName = null; }); config.printers = configured().filter(function (p) { return p.id !== id; }); if (config.autoPrintSale && links.some(function (p) { return p.documentType === 'sale'; })) config.autoPrintSale = false; if (!await save()) config = before; return; }
        if (b.hasAttribute('data-test-printer')) { var assignedType = assigned(item)[0]; if (!assignedType || savedSnapshot !== JSON.stringify(config)) { UI.toast('عيّن الطابعة لنوع مستند واحفظ الإعدادات قبل اختبارها.', 'warning'); return; } if (!await UI.confirm({ title: 'طباعة صفحة اختبار', message: 'سيُرسل نموذج حقيقي إلى طابعة Windows المختارة. هل تريد المتابعة؟', confirmText: 'طباعة صفحة اختبار' })) return; b.disabled = true; try { var job = await Api.request('/api/printing/test/' + assignedType.documentType, { method: 'POST', body: JSON.stringify({ operationKey: key() }) }); UI.toast(job.message, job.status === 'submitted_unknown' ? 'success' : 'warning'); } catch (error) { message(error); } finally { b.disabled = false; } return; }
      }
      if (b.hasAttribute('data-load-jobs')) { b.disabled = true; try { var jobs = await Api.request('/api/printing/jobs'); host.querySelector('[data-print-jobs]').innerHTML = jobs.map(function (j) { return '<p>' + esc(j.reference) + ' — ' + esc(j.printerName) + ' — ' + esc(statuses[j.status] || j.status) + ' — ' + esc(j.message) + (j.status === 'accepted' ? ' <button class="button" data-review-job="' + j.id + '">مراجعة بعد 10 دقائق</button>' : '') + '</p>'; }).join('') || '<p>لا توجد مهام.</p>'; } catch (error) { message(error); } finally { b.disabled = false; } return; }
      if (b.hasAttribute('data-review-job')) { b.disabled = true; try { var result = await Api.request('/api/printing/jobs/' + b.getAttribute('data-review-job') + '/review', { method: 'POST', body: '{}' }); UI.toast(result.message, 'warning'); } catch (error) { message(error); } finally { b.disabled = false; } }
    }
    async function load() {
      if (loading || busy) return;
      loading = true;
      if (labelPanel.parentNode !== host.parentNode && host.parentNode) host.parentNode.appendChild(labelPanel);
      host.innerHTML = '<p role="status">جارٍ تحميل إعدادات الطباعة المحفوظة…</p>';
      updateAdvanced = null;
      try {
        var result = await Api.request('/api/printing/settings');
        if (!currentView(context)) return;
        config = result;
        savedSnapshot = JSON.stringify(config);
        draw();
      } catch (error) {
        if (!currentView(context)) return;
        host.innerHTML = '<div class="empty-state" role="alert"><strong>تعذر تحميل إعدادات الطباعة</strong><span>' + esc(error.message) + '</span><button class="button" data-retry>إعادة المحاولة</button></div>';
        host.querySelector('[data-retry]').onclick = load;
      } finally {
        loading = false;
      }
    }
    load();
    loadBranding();
    refresh();
  }
  global.AlKhayalRenderers['native-printing'] = managedSettings;
}(window));
