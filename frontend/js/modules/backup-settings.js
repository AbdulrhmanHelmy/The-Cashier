(function (global) {
  "use strict";
  var Api = global.AlKhayalApi, UI = global.AlKhayalUI, U = global.AlKhayalUtils;
  var endpoint = "/api/settings/backups";

  function date(value) {
    return value ? U.dateTime(value) : "—";
  }

  function request(suffix, method, body, headers) {
    // Backups and uploads can exceed the ordinary API request timeout.
    return Api.request(endpoint + suffix, {
      method: method, body: body, headers: headers, signal: new AbortController().signal
    });
  }

  function download(name) {
    return endpoint + "/download?name=" + encodeURIComponent(name);
  }

  function database(screen, container, context) {
    container.innerHTML = UI.pageHeader(screen) +
      '<div data-backup-message class="backup-status" role="status" aria-live="polite">جارٍ تحميل حالة النسخ الاحتياطي…</div>' +
      '<div data-backup-content></div><div data-backup-database></div>';
    var message = container.querySelector("[data-backup-message]");
    var content = container.querySelector("[data-backup-content]");
    var databaseInfo = container.querySelector("[data-backup-database]");
    var busy = false;
    var dialogs = global.AlKhayalHasBackupDialogs && global.chrome.webview.hostObjects.alkhayalBackupDialogs;

    function report(text, failure) {
      message.textContent = text;
      message.classList.toggle("field-error", !!failure);
    }

    async function run(label, action, loading) {
      if (busy) return;
      busy = true;
      var controls = Array.from(content.querySelectorAll("button, input"));
      controls.forEach(function (control) { control.disabled = true; });
      if (loading) UI.loadingModal(loading);
      else report(label);
      try { await action(); }
      catch (error) { report(error.message, true); }
      finally {
        if (loading) UI.closeModal();
        busy = false;
        controls.forEach(function (control) { control.disabled = false; });
      }
    }

    function draw(status) {
      if (!context.isActive()) return;
      var config = status.configuration, last = status.lastSuccess;
      content.innerHTML =
        '<section class="card"><div class="card-head"><h2>النسخ الاحتياطي والاستعادة</h2></div><div class="card-body">' +
        '<p class="muted">تحتوي النسخة على بيانات العمل وحسابات المستخدمين. احفظ الملف في مكان آمن؛ محتواه غير مشفّر.</p>' +
        '<div class="page-actions"><button class="button primary" type="button" data-backup-create>إنشاء نسخة احتياطية</button>' +
        '<button class="button" type="button" data-backup-choose>استعادة نسخة احتياطية</button>' +
        '<input type="file" accept=".alkbackup" data-backup-file hidden></div>' +
        '<form class="form-grid" data-backup-config>' +
        '<label class="field span-2" for="backupFolder">مجلد النسخ على جهاز الخادم' +
        '<input class="input ltr" id="backupFolder" name="folder" required value="' + U.escape(config.directory) + '"></label>' +
        '<label class="switch span-2"><input type="checkbox" name="enabled"' + (config.enabled ? " checked" : "") +
        '><span>نسخة تلقائية يوميًا أثناء تشغيل البرنامج</span></label>' +
        '<div class="page-actions span-2"><button class="button" type="submit">حفظ إعدادات النسخ</button>' +
        (dialogs ? '<button class="button" type="button" data-backup-pick-folder>اختيار مجلد</button>' +
          '<button class="button" type="button" data-backup-open-folder>فتح مجلد النسخ</button>' : "") + '</div></form>' +
        '<div class="database-info"><dl><dt>آخر نسخة ناجحة</dt><dd>' + U.escape(last ? date(last.createdUtc) + " · " + last.name : "—") + '</dd>' +
        '<dt>آخر فشل</dt><dd>' + U.escape(status.lastFailureUtc ? date(status.lastFailureUtc) + " · " + status.lastError : "—") +
        '</dd></dl></div></div></section>' +
        '<section class="card"><div class="card-head"><h2>النسخ المتاحة في مجلد النسخ المُعد</h2></div><div class="card-body">' +
        '<p class="muted">يعرض هذا السجل ملفات المجلد التالي فقط: <bdi dir="ltr">' + U.escape(config.directory) + '</bdi></p>' +
        (status.files.length ? '<div class="table-wrap"><table class="data-table"><thead><tr><th>الملف</th><th>تاريخ الملف</th>' +
          '<th>الحجم (بايت)</th><th>الحالة</th></tr></thead><tbody>' + status.files.map(function (file) {
            return '<tr><td><a class="ltr" href="' + download(file.name) + '">' + U.escape(file.name) + '</a></td><td>' +
              U.escape(date(file.createdUtc)) + '</td><td>' + U.number(file.size) + '</td><td>' +
              (last && file.path === last.path ? "تم التحقق عند الإنشاء" : "يُفحص قبل الاستعادة") + '</td></tr>';
          }).join("") + '</tbody></table></div>' : '<p class="muted">لا توجد نسخ متاحة.</p>') + '</div></section>';

      var form = content.querySelector("[data-backup-config]");
      form.onsubmit = function (event) {
        event.preventDefault();
        var config = { directory: form.elements.folder.value.trim(), enabled: form.elements.enabled.checked };
        run("جارٍ حفظ إعدادات النسخ…", async function () {
          draw(await request("", "PUT", JSON.stringify(config)));
          report("تم حفظ إعدادات النسخ الاحتياطي.");
        });
      };

      if (dialogs) {
        form.querySelector("[data-backup-pick-folder]").onclick = function () { pickFolder(form.elements.folder); };
        form.querySelector("[data-backup-open-folder]").onclick = async function () {
          try {
            if (!await dialogs.openConfiguredFolder()) report("مجلد النسخ غير متاح حاليًا.", true);
          } catch (error) { report("تعذر فتح مجلد النسخ.", true); }
        };
      }
      content.querySelector("[data-backup-create]").onclick = function () { chooseDestination(config.directory); };

      var picker = content.querySelector("[data-backup-file]");
      content.querySelector("[data-backup-choose]").onclick = function () { picker.click(); };
      picker.onchange = function () {
        var file = picker.files[0];
        picker.value = "";
        if (!file) return;
        if (!/\.alkbackup$/i.test(file.name)) {
          report("اختر ملفًا بامتداد .alkbackup.", true);
          return;
        }
        run("جارٍ رفع النسخة والتحقق منها…", async function () {
          var manifest = await request("/inspect", "POST", file, { "Content-Type": "application/octet-stream" });
          if (!context.isActive()) return;
          report("تم التحقق من الملف. يلزم تأكيد الاستعادة.");
          confirmRestore(manifest);
        });
      };
    }

    async function pickFolder(input) {
      try {
        var selected = await dialogs.pickFolder();
        if (selected) input.value = selected;
      } catch (error) { UI.toast("تعذر فتح نافذة اختيار المجلد.", "danger"); }
    }

    function chooseDestination(configuredFolder) {
      UI.modal({
        title: "إنشاء نسخة احتياطية",
        body: '<form data-backup-manual><label class="field">مجلد الحفظ على جهاز الخادم' +
          '<input class="input ltr" name="folder" required value="' + U.escape(configuredFolder) + '"></label>' +
          (dialogs ? '<button class="button" type="button" data-backup-browse>اختيار مجلد</button>' : "") +
          '<p class="muted">يُنشأ ملف جديد باسم فريد في هذا المجلد.</p></form>',
        actions: '<button class="button" type="button" data-modal-close>إلغاء</button>' +
          '<button class="button primary" type="button" data-backup-save>إنشاء النسخة</button>',
        focusSelector: '[name="folder"]',
        onOpen: function (modal) {
          var form = modal.querySelector("[data-backup-manual]");
          if (dialogs) form.querySelector("[data-backup-browse]").onclick = function () { pickFolder(form.elements.folder); };
          form.onsubmit = function (event) {
            event.preventDefault();
            if (busy) return;
            var folder = form.elements.folder.value.trim();
            if (!folder || !form.reportValidity()) return;
            UI.closeModal();
            run("", async function () {
              var file = await request("/create", "POST", JSON.stringify({ directory: folder }));
              if (!context.isActive()) return;
              var status;
              try {
                status = await request("", "GET");
                draw(status);
              } catch (error) { UI.toast(error.message, "warning"); }
              report("تم إنشاء النسخة والتحقق منها · " + file.path + " · " + U.number(file.size) + " بايت");
              var listed = status && status.files.some(function (entry) { return entry.path === file.path; });
              if (status && !listed) {
                message.appendChild(document.createTextNode(" · لم يظهر هذا الملف في سجل مجلد النسخ المُعد. استخدم المسار المعروض أعلاه للوصول إليه على جهاز الخادم."));
              }
              if (listed) {
                var link = document.createElement("a");
                link.className = "button";
                link.href = download(file.name);
                link.textContent = "حفظ نسخة في مكان آخر";
                message.appendChild(document.createTextNode(" "));
                message.appendChild(link);
              }
            }, {
              title: "جاري إنشاء النسخة الاحتياطية",
              message: "يرجى الانتظار حتى الانتهاء من حفظ بياناتك..."
            });
          };
          modal.querySelector("[data-backup-save]").onclick = function () { form.requestSubmit(); };
        }
      });
    }

    function confirmRestore(manifest) {
      UI.modal({
        title: "تأكيد استعادة النسخة الاحتياطية",
        body: '<p class="field-error">ستُستبدل بيانات العمل الحالية، بما فيها حسابات المستخدمين. ' +
          'احفظ عملك المفتوح وأبلغ مستخدمي الأجهزة الأخرى قبل المتابعة.</p><dl><dt>تاريخ النسخة</dt><dd>' +
          U.escape(date(manifest.createdUtc)) + '</dd><dt>إصدار التطبيق</dt><dd>' + U.escape(manifest.applicationVersion) +
          '</dd><dt>آخر ترحيل</dt><dd class="ltr">' + U.escape(manifest.schemaVersion) +
          '</dd><dt>المحتوى</dt><dd>قاعدة بيانات العمل · ' + U.number(manifest.size) + ' بايت</dd></dl>' +
          '<p>تُحفظ نسخة طوارئ قبل الاستعادة. سجّل الدخول بعدها بحساب موجود في النسخة المستعادة.</p>',
        actions: '<button class="button" type="button" data-modal-close>إلغاء</button>' +
          '<button class="button primary" type="button" data-backup-confirm>تأكيد الاستعادة واستبدال البيانات</button>',
        onOpen: function (modal) {
          modal.querySelector("[data-backup-confirm]").onclick = function () {
            if (busy) return;
            UI.closeModal();
            run("", async function () {
              var emergency = await request("/restore", "POST", JSON.stringify({ confirmation: manifest.confirmation }));
              Api.clearSession();
              for (var index = global.sessionStorage.length - 1; index >= 0; index--) {
                var key = global.sessionStorage.key(index);
                if (key && key.indexOf("alkhayal_") === 0) global.sessionStorage.removeItem(key);
              }
              global.alert("تمت الاستعادة. نسخة الطوارئ محفوظة في " + emergency.path);
              global.location.replace("/login.html");
            }, {
              title: "جاري استعادة النسخة الاحتياطية",
              message: "يرجى الانتظار، لا تغلق البرنامج أثناء استعادة البيانات..."
            });
          };
        }
      });
    }

    request("", "GET").then(function (status) {
      draw(status);
      report("");
    }).catch(function (error) { report(error.message, true); });

    Api.request("/api/settings/database-info").then(function (data) {
      if (!context.isActive()) return;
      databaseInfo.innerHTML = '<section class="card"><div class="card-head"><h2>حالة قاعدة البيانات</h2></div>' +
        '<div class="card-body database-info"><dl><dt>المسار المحلي</dt><dd class="ltr">' +
        U.escape(data.databasePath) + '</dd><dt>الحجم</dt><dd>' + U.number(data.sizeBytes) +
        ' بايت</dd><dt>الترحيلات المطبقة</dt><dd>' + U.number(data.appliedMigrationCount) +
        '</dd><dt>آخر ترحيل</dt><dd class="ltr">' + U.escape(data.latestMigration) +
        '</dd><dt>وضع السجل</dt><dd class="ltr">' + U.escape(data.journalMode) +
        '</dd><dt>المفاتيح الخارجية</dt><dd>' + (data.foreignKeysEnabled ? "مفعّلة" : "غير مفعّلة") + '</dd></dl></div></section>';
    }).catch(function (error) { databaseInfo.textContent = error.message; });
  }

  global.AlKhayalRenderers["database-settings"] = database;
}(window));
