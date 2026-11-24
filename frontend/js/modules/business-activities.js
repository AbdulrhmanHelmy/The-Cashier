(function (global) {
  "use strict";
  var U = global.AlKhayalUtils, UI = global.AlKhayalUI, Api = global.AlKhayalApi;

  function render(screen, container, context) {
    var canEdit = context.user && context.user.role === "administrator";
    var scaleProfiles = [], loading = true, error = "", loadToken = 0;

    function profileFields(profile, key) {
      return '<div class="card-body form-grid" data-scale-profile="' + key + '">' +
        '<label class="field"><span>اسم الميزان</span><input class="input" data-field="name" value="' + U.escape(profile.name) + '"></label>' +
        '<label class="field"><span>البادئة</span><input class="input ltr" data-field="prefix" value="' + U.escape(profile.prefix) + '" maxlength="6"></label>' +
        '<label class="field"><span>خانات PLU</span><input class="input" data-field="productLength" type="number" min="1" max="8" value="' + profile.productLength + '"></label>' +
        '<label class="field"><span>نوع القيمة</span><select class="select" data-field="valueType"><option value="weight"' + (profile.valueType === "weight" ? " selected" : "") + '>وزن</option><option value="price"' + (profile.valueType === "price" ? " selected" : "") + '>سعر إجمالي</option></select></label>' +
        '<label class="field"><span>خانات القيمة</span><input class="input" data-field="valueLength" type="number" min="1" max="8" value="' + profile.valueLength + '"></label>' +
        '<label class="field"><span>الخانات العشرية</span><input class="input" data-field="valueDecimals" type="number" min="0" max="8" value="' + profile.valueDecimals + '"></label>' +
        '<label class="switch"><input data-field="valueHasCheckDigit" type="checkbox"' + (profile.valueHasCheckDigit ? " checked" : "") + '><span>رقم تحقق داخلي للقيمة</span></label>' +
        '<label class="switch"><input data-field="isActive" type="checkbox"' + (profile.isActive ? " checked" : "") + '><span>مفعل</span></label>' +
        '<div><button class="button ' + (key === "new" ? "" : "primary") + '" data-save-scale="' + key + '">' + (key === "new" ? "إضافة الميزان" : "حفظ الملف") + '</button></div></div>';
    }

    function draw() {
      var state = loading ? '<div class="empty-state"><strong>جارٍ تحميل إعدادات الموازين…</strong></div>' :
        error ? '<div class="empty-state"><strong>تعذر تحميل إعدادات الموازين</strong><span>' + U.escape(error) + '</span><br><button class="button" data-retry>إعادة المحاولة</button></div>' : "";
      var profileRows = scaleProfiles.map(function (profile) { return profileFields(profile, String(profile.id)); }).join("");
      var newProfile = { name: "الميزان الرئيسي", prefix: "99", productLength: 5, valueType: "weight", valueLength: 5, valueDecimals: 3, valueHasCheckDigit: false, isActive: true };
      var content = state || '<section class="card settings-form-card"><div class="card-head"><h2>إعداد الموازين</h2><span class="badge info">ميزان واحد لكل نسخة</span></div>' +
        '<p class="card-body">اضبط البادئة وطول PLU ونوع القيمة. مجموع البادئة وPLU والقيمة يجب أن يساوي 12؛ رقم EAN-13 الأخير يُتحقق منه تلقائيًا. اربط PLU برمز SKU للمنتج.</p>' +
        profileRows + (scaleProfiles.length ? '<p class="card-body muted">يمكنك تعديل إعداد الميزان من الحقول أعلاه.</p>' : profileFields(newProfile, "new")) + '</section>';
      container.innerHTML = UI.pageHeader(screen) + (!canEdit ? '<div class="mock-banner"><strong>للعرض فقط</strong><span>يمكن لمسؤول النظام فقط تغيير إعدادات الموازين.</span></div>' : "") + content;
      bind();
    }

    function bind() {
      var retry = container.querySelector("[data-retry]");
      if (retry) retry.onclick = load;
      container.querySelectorAll("[data-save-scale]").forEach(function (button) {
        button.onclick = function () {
          var key = button.getAttribute("data-save-scale"), root = container.querySelector('[data-scale-profile="' + key + '"]');
          if (!root) return;
          function field(name) { var element = root.querySelector('[data-field="' + name + '"]'); return element ? (element.type === "checkbox" ? element.checked : element.value) : null; }
          var old = scaleProfiles.filter(function (profile) { return String(profile.id) === key; })[0] || {};
          var payload = { id: old.id || 0, version: old.version || 0, name: field("name"), prefix: field("prefix"), productLength: Number(field("productLength")), valueType: field("valueType"), valueLength: Number(field("valueLength")), valueDecimals: Number(field("valueDecimals")), valueHasCheckDigit: !!field("valueHasCheckDigit"), isActive: !!field("isActive") };
          button.disabled = true;
          Api.request("/api/catalog/scale-barcode-profiles", { method: "POST", body: JSON.stringify(payload) }).then(function () {
            UI.toast("تم حفظ ملف الميزان.", "success"); load();
          }).catch(function (reason) {
            button.disabled = false; UI.toast(reason.message || "تعذر حفظ ملف الميزان.", "danger");
          });
        };
      });
    }

    function load() {
      loadToken += 1; var token = loadToken;
      loading = true; error = ""; draw();
      Api.request("/api/catalog/scale-barcode-profiles").then(function (profiles) {
        if (token !== loadToken || !container.isConnected) return;
        scaleProfiles = profiles || []; loading = false; draw();
      }).catch(function (reason) {
        if (token !== loadToken || !container.isConnected) return;
        loading = false; error = reason.message; draw();
      });
    }

    load();
  }

  global.AlKhayalRenderers["business-activities"] = render;
}(window));
