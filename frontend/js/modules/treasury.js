(function (global) {
  "use strict";
  var R = global.AlKhayalRenderers, U = global.AlKhayalUtils, UI = global.AlKhayalUI, Api = global.AlKhayalApi;
  function numericHtml(value){return U.numericHtml?U.numericHtml(value):U.escape(value);}
  function dateTime(value){return U.dateTime?U.dateTime(value):String(value==null?"":value);}
  function key() { var bytes = new Uint8Array(16); global.crypto.getRandomValues(bytes); return Array.prototype.map.call(bytes, function (x) { return ("0" + x.toString(16)).slice(-2); }).join(""); }
  function write(path, method, body) { return Api.request(path, { method: method, body: body ? JSON.stringify(body) : undefined }); }
  function accountType(type) { return type === "bank" ? "بنك" : "خزنة"; }
  function total(rows, type) {
    var sum = BigInt(0), valid = true;
    rows.filter(function (row) { return row.type === type; }).forEach(function (row) {
      var match = String(row.balance).match(/^(-?)(\d+)(?:\.(\d{1,4}))?$/);
      if (!match) { valid = false; return; }
      var amount = BigInt(match[2] + ((match[3] || "") + "0000").slice(0, 4));
      sum += match[1] ? -amount : amount;
    });
    if (!valid) return "—";
    var negative = sum < BigInt(0); if (negative) sum = -sum;
    var digits = ("0000" + String(sum)).slice(-4), whole = String(sum / BigInt(10000));
    return (negative ? "-" : "") + whole + "." + (digits.slice(2) === "00" ? digits.slice(0, 2) : digits);
  }
  function showError(host, reason) { var node = host.querySelector("[data-treasury-error]"); node.hidden = false; node.textContent = reason.message || reason; }
  function field(id, label, value, extra) { return '<div class="field"><label for="' + id + '">' + label + '</label><input class="input" id="' + id + '" value="' + U.escape(value || "") + '" ' + (extra || "") + '></div>'; }

  function directory(screen, container, context) {
    var rows = [], receivable = null, token = 0, state = context.state;
    if (state.globalSearchTreasuryId) { selectedAccount(); return; }
    function selectedAccount() {
      var id = state.globalSearchTreasuryId;
      var current = context.isCurrent || context.isActive;
      paint('<p role="status">جارٍ تحميل الحساب…</p>');
      Api.request("/api/treasury/accounts/directory-search?id=" + id).then(function (items) {
        if (!current()) return;
        var row = items.find(function (item) { return item.id === id; });
        if (!row) throw new Error("Account unavailable");
        paint('<h2>' + U.escape(row.name) + '</h2><p><bdi>' + U.escape(row.code) + '</bdi> · ' +
          U.escape(accountType(row.type)) + '</p>');
      }).catch(function () { if (current()) paint('<p role="alert">لا يمكن فتح هذا السجل</p>'); });
      function paint(body) {
        container.innerHTML = UI.pageHeader(screen) + '<section class="card"><div class="card-body">' + body +
          '<button class="button" type="button" data-treasury-back>العودة إلى الحسابات</button></div></section>';
        container.querySelector("[data-treasury-back]").onclick = function () { context.navigate(screen.route, {}); };
      }
    }
    container.innerHTML = UI.pageHeader(screen, '<button class="button primary" data-account-add>+ إضافة خزنة أو بنك</button>') +
      '<div class="treasury-center"><div class="treasury-summary" data-treasury-summary aria-live="polite"></div><section class="card treasury-directory"><div class="card-body treasury-toolbar">' + field("treasurySearch", "بحث بالاسم أو الكود", state.treasurySearch, 'type="search"') +
      '<p class="muted">الحسابات ذات الحركات المالية لا يمكن حذفها.</p><p class="field-error" data-treasury-error role="alert" hidden></p></div><div data-treasury-results aria-live="polite"></div></section></div>';
    var results = container.querySelector("[data-treasury-results]"), search = container.querySelector("#treasurySearch");
    search.oninput = function () { state.treasurySearch = search.value; draw(); };
    container.querySelector("[data-account-add]").onclick = function () { edit(null); };
    function draw() {
      var query = U.normalize(search.value), visible = rows.filter(function (row) { return U.normalize(row.code + " " + row.name).indexOf(query) >= 0; });
      var receivableMatches = receivable && U.normalize("INSTALLMENT-RECEIVABLE مديونية الأقساط حساب الأقساط").indexOf(query) >= 0;
      container.querySelector("[data-treasury-summary]").innerHTML = '<article class="treasury-stat cash"><span>أرصدة الخزن</span><strong dir="ltr">' + U.escape(total(rows, "cash")) + ' <small>ج.م</small></strong><small>الحسابات النقدية</small></article><article class="treasury-stat bank"><span>أرصدة البنوك</span><strong dir="ltr">' + U.escape(total(rows, "bank")) + ' <small>ج.م</small></strong><small>الحسابات البنكية</small></article><article class="treasury-stat count"><span>عدد الحسابات</span><strong>' + rows.length + '</strong><small>خزن وبنوك فقط</small></article>';
      results.innerHTML = '<div class="table-wrap"><table class="data-table"><thead><tr><th>الكود</th><th>الاسم</th><th>النوع</th><th>الرصيد (ج.م)</th><th>ملاحظات</th><th>الإجراءات</th></tr></thead><tbody>' +
        (visible.length ? visible.map(function (row) {
          return '<tr><td>' + U.escape(row.code) + '</td><td>' + U.escape(row.name) + '</td><td>' + '<span class="treasury-badge ' + U.escape(row.type) + '">' + accountType(row.type) + '</span></td><td class="ltr treasury-money">' + U.escape(row.balance) + '</td><td>' + U.escape(row.notes || "—") + '</td><td><button class="table-action" data-account-edit="' + row.id + '">تعديل</button><button class="table-action danger" data-account-delete="' + row.id + '">حذف</button></td></tr>';
        }).join("") : "") + (receivableMatches ? '<tr><td>INSTALLMENT-RECEIVABLE</td><td>مديونية الأقساط</td><td><span class="treasury-badge noncash">حساب غير نقدي</span></td><td class="ltr">' + U.escape(receivable.balance) + '</td><td>إجمالي المستحق بعد خصم المقدمات والتحصيلات والمرتجعات</td><td>—</td></tr>' : "") +
        (visible.length || receivableMatches ? "" : '<tr><td colspan="6"><div class="empty-state">' + (rows.length || receivable ? "لا توجد نتائج مطابقة." : "لا توجد خزن أو بنوك. أضف أول حساب للبدء.") + '</div></td></tr>') + '</tbody></table></div>';
      results.querySelectorAll("[data-account-edit]").forEach(function (button) { button.onclick = function () { edit(rows.find(function (row) { return String(row.id) === button.getAttribute("data-account-edit"); })); }; });
      results.querySelectorAll("[data-account-delete]").forEach(function (button) {
        button.onclick = function () {
          var row = rows.find(function (item) { return String(item.id) === button.getAttribute("data-account-delete"); });
          UI.confirm({ title: "حذف خزنة أو بنك", message: "هل تريد حذف «" + row.name + "»؟ لا يمكن حذف حساب عليه حركات مالية.", danger: true }).then(function (confirmed) {
            if (!confirmed || !context.isActive()) return;
            button.disabled = true;
            write("/api/treasury/accounts/" + row.id + "?version=" + row.version, "DELETE").then(function () { if (context.isActive()) { UI.toast("تم حذف الحساب.", "success"); load(); } }).catch(function (reason) { if (context.isActive()) { button.disabled = false; showError(container, reason); } });
          });
        };
      });
    }
    function load() {
      var current = ++token;
      results.innerHTML = '<div class="empty-state" role="status">جارٍ تحميل الخزن والبنوك...</div>';
      container.querySelector("[data-treasury-error]").hidden = true;
      Promise.all([Api.request("/api/treasury/accounts"), Api.request("/api/treasury/installment-receivable")]).then(function (data) { if (current !== token || !context.isActive()) return; rows = data[0]; receivable=data[1]; draw(); }).catch(function (reason) {
        if (current !== token || !context.isActive()) return;
        results.innerHTML = '<div class="empty-state"><span>' + U.escape(reason.message) + '</span><button class="button" data-retry>إعادة المحاولة</button></div>';
        results.querySelector("[data-retry]").onclick = load;
      });
    }
    function edit(row) {
      var busy = false;
      UI.modal({ title: row ? "تعديل الخزنة أو البنك" : "إضافة خزنة أو بنك", compact: true, focusSelector: "#treasuryName", beforeClose: function () { return !busy; },
        body: '<form id="treasuryAccountForm" class="form-grid treasury-account-form">' + field("treasuryCode", "الكود", row && row.code, 'required maxlength="40" dir="ltr"') + field("treasuryName", "الاسم", row && row.name, 'required maxlength="160"') +
          '<div class="field"><label for="treasuryType">النوع</label><select class="select" id="treasuryType"><option value="cash">خزنة</option><option value="bank"' + (row && row.type === "bank" ? ' selected' : '') + '>بنك</option></select></div><div class="field"><label for="treasuryNotes">ملاحظات</label><textarea class="textarea" id="treasuryNotes" maxlength="1000">' + U.escape(row && row.notes || "") + '</textarea></div><p class="field-error span-2" data-treasury-error role="alert" hidden></p></form>',
        actions: '<button class="button primary" type="submit" form="treasuryAccountForm" data-account-save>حفظ</button><button class="button" data-modal-close>إلغاء</button>',
        onOpen: function (modal) {
          modal.querySelector("#treasuryAccountForm").onsubmit = function (event) {
            event.preventDefault(); if (busy) return;
            var command = { code: modal.querySelector("#treasuryCode").value.trim(), name: modal.querySelector("#treasuryName").value.trim(), type: modal.querySelector("#treasuryType").value, notes: modal.querySelector("#treasuryNotes").value.trim(), version: row ? row.version : 0 };
            if (!command.code || !command.name) { showError(modal, "أدخل الكود والاسم."); return; }
            busy = true; modal.querySelector("[data-account-save]").disabled = true;
            write("/api/treasury/accounts" + (row ? "/" + row.id : ""), row ? "PUT" : "POST", command).then(function () {
              UI.closeModal(); if (context.isActive()) { UI.toast("تم حفظ الحساب.", "success"); load(); }
            }).catch(function (reason) { busy = false; modal.querySelector("[data-account-save]").disabled = false; showError(modal, reason); });
          };
        }
      });
    }
    load();
  }

  function movement(screen, container, context) {
    var type = screen.route === "account-deposit" ? "deposit" : "withdraw", label = type === "deposit" ? "إيداع" : "سحب", state = context.state;
    var draft = state.treasuryDraft || (state.treasuryDraft = { accountId: "", amount: "", notes: "", operationKey: key() }), rows = [], busy = false;
    container.innerHTML = UI.pageHeader(screen) + '<section class="card treasury-movement treasury-' + type + '"><div class="card-body" data-treasury-form>جارٍ تحميل الحسابات...</div></section>';
    var host = container.querySelector("[data-treasury-form]");
    function load() {
      Api.request("/api/treasury/accounts").then(function (data) { if (!context.isActive()) return; rows = data; draw(); }).catch(function (reason) {
        if (!context.isActive()) return;
        host.innerHTML = '<div class="empty-state"><span>' + U.escape(reason.message) + '</span><button class="button" data-retry>إعادة المحاولة</button></div>';
        host.querySelector("[data-retry]").onclick = load;
      });
    }
    function draw() {
      if (!rows.length) { host.innerHTML = '<div class="empty-state">لا توجد خزن أو بنوك. أضف حسابًا من قائمة الخزنة والبنك أولًا.</div>'; return; }
      host.innerHTML = '<form id="treasuryMovementForm" class="form-grid treasury-movement-form"><div class="treasury-form-heading span-2"><span>' + label + '</span><strong>تفاصيل الحركة</strong><small>اختر الحساب وأدخل المبلغ والبيان</small></div><div class="field"><label for="treasuryAccount">الخزنة أو البنك</label><select class="select" id="treasuryAccount" required><option value="">اختر الحساب</option>' + rows.map(function (row) { return '<option value="' + row.id + '"' + (String(row.id) === String(draft.accountId) ? ' selected' : '') + '>' + U.escape(row.name + " — " + accountType(row.type) + " — " + row.code) + '</option>'; }).join("") + '</select></div><div class="treasury-balance"><span>الرصيد الحالي</span><output data-account-balance class="ltr"></output><small>ج.م</small></div>' +
        field("treasuryAmount", "المبلغ (ج.م)", draft.amount, 'required inputmode="decimal" dir="ltr" maxlength="20" autocomplete="off"') + '<div class="field"><label for="treasuryMovementNotes">البيان / ملاحظات</label><textarea class="textarea" id="treasuryMovementNotes" maxlength="1000">' + U.escape(draft.notes) + '</textarea></div><p class="field-error span-2" data-treasury-error role="alert" hidden></p><div class="span-2"><button class="button primary" type="submit" data-movement-save>' + (draft.pending ? "إعادة المحاولة للتحقق من الحفظ" : "حفظ " + label + " الرصيد") + '</button></div></form><div data-treasury-receipt role="status"></div>';
      var account = host.querySelector("#treasuryAccount"), amount = host.querySelector("#treasuryAmount"), notes = host.querySelector("#treasuryMovementNotes"), form = host.querySelector("#treasuryMovementForm");
      function balance() { var row = rows.find(function (item) { return String(item.id) === account.value; }); host.querySelector("[data-account-balance]").textContent = row ? row.balance : "—"; }
      account.onchange = function () { draft.accountId = account.value; balance(); };
      amount.oninput = function () { draft.amount = amount.value; };
      notes.oninput = function () { draft.notes = notes.value; };
      balance();
      if (draft.pending) { account.disabled = amount.disabled = notes.disabled = true; showError(host, "لم يتم تأكيد نتيجة الطلب. أعد المحاولة بنفس البيانات للتحقق من الحفظ قبل بدء عملية جديدة."); }
      if (state.treasuryReceipt) receipt(state.treasuryReceipt);
      form.onsubmit = function (event) {
        event.preventDefault(); if (busy) return;
        var command = draft.pending || { accountId: Number(account.value), amount: amount.value.trim(), notes: notes.value.trim() };
        if (!command.accountId || !/^\d+(?:\.\d{1,2})?$/.test(command.amount) || !/[1-9]/.test(command.amount)) { showError(host, "اختر الحساب وأدخل مبلغًا أكبر من صفر بدقة خانتين عشريتين بحد أقصى."); return; }
        var signature = JSON.stringify([command.accountId, command.amount, command.notes]);
        if (!draft.pending && draft.signature !== signature) { draft.operationKey = key(); draft.signature = signature; }
        command.operationKey = draft.operationKey;
        draft.pending = command; busy = true;
        account.disabled = amount.disabled = notes.disabled = true;
        var button = host.querySelector("[data-movement-save]"); button.disabled = true;
        write("/api/treasury/" + type, "POST", command).then(function (result) {
          draft.pending = null; draft.amount = ""; draft.notes = ""; draft.operationKey = key(); draft.signature = null; state.treasuryReceipt = result; busy = false;
          if (context.isActive()) { UI.toast("تم حفظ " + label + " الرصيد.", "success"); draw(); load(); }
        }).catch(function (reason) {
          busy = false;
          // A transport/5xx error can follow a committed transaction; retain its identity across tab switches.
          if (reason.status && reason.status < 500) draft.pending = null;
          if (!context.isActive()) return;
          draw(); showError(host, reason);
        });
      };
    }
    function receipt(result) {
      host.querySelector("[data-treasury-receipt]").innerHTML = '<div class="notice treasury-receipt"><strong>تم حفظ ' + label + ' الرصيد — حركة رقم ' + numericHtml(result.id) + '</strong><p>المبلغ: ' + numericHtml(result.amount) + ' ج.م — رصيد الحساب بعد الحركة: ' + numericHtml(result.balanceAfter) + ' ج.م</p><p>' + numericHtml(dateTime(result.createdUtc)) + (result.replayed ? " — تم التحقق من العملية السابقة دون تكرارها." : "") + '</p></div>';
    }
    load();
  }
  R["treasury-accounts"] = directory;
  R["treasury-movement"] = movement;
}(window));
