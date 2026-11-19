(function (global) {
  "use strict";
  var busy = false;
  function encode(filters) { return Object.keys(filters || {}).filter(function (key) { return filters[key] !== null && filters[key] !== undefined && String(filters[key]) !== ""; }).map(function (key) { return encodeURIComponent(key) + "=" + encodeURIComponent(filters[key]); }).join("&"); }
  function bytesToBase64(buffer) { var bytes = new Uint8Array(buffer), parts = [], size = 0x8000; for (var i = 0; i < bytes.length; i += size) parts.push(String.fromCharCode.apply(null, bytes.subarray(i, Math.min(i + size, bytes.length)))); return btoa(parts.join("")); }
  async function errorFrom(response) { try { var body = await response.json(); return new Error(body && body.error && body.error.message || "تعذر تجهيز ملف التصدير."); } catch (ignore) { return new Error("تعذر تجهيز ملف التصدير."); } }
  async function run(options, format, trigger) {
    if (busy) return;
    var dialogs = global.AlKhayalHasExportDialogs && global.chrome && global.chrome.webview && global.chrome.webview.hostObjects.alkhayalExportDialogs;
    var suggested = options.fileName || options.dataset;
    var path = dialogs ? await dialogs.pickFile(format, suggested) : null;
    if (dialogs && !path) return;
    busy = true; if (trigger) trigger.disabled = true;
    global.AlKhayalUI.loadingModal({ title: "جاري تصدير " + options.arabicName, message: "يرجى الانتظار حتى الانتهاء من تجهيز وحفظ الملف..." });
    try {
      var query = encode(typeof options.filters === "function" ? options.filters() : options.filters), url = "/api/exports/" + encodeURIComponent(options.dataset) + "/" + format + (query ? "?" + query : "");
      var response = await fetch(url, { credentials: "same-origin", headers: { Accept: format === "xlsx" ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "text/csv" } });
      if (!response.ok) throw await errorFrom(response);
      var buffer = await response.arrayBuffer();
      if (dialogs) await dialogs.saveFile(path, bytesToBase64(buffer));
      else { var blob = new Blob([buffer], { type: response.headers.get("Content-Type") || "application/octet-stream" }), link = document.createElement("a"); link.href = URL.createObjectURL(blob); link.download = suggested + "." + format; document.body.appendChild(link); link.click(); link.remove(); setTimeout(function () { URL.revokeObjectURL(link.href); }, 0); }
      global.AlKhayalUI.closeModal(); global.AlKhayalUI.toast("تم تصدير البيانات وحفظ الملف بنجاح.", "success");
    } catch (error) { global.AlKhayalUI.closeModal(); global.AlKhayalUI.toast(error && error.message || "تعذر تصدير البيانات.", "danger"); }
    finally { busy = false; if (trigger && trigger.isConnected) trigger.disabled = false; }
  }
  function choose(options, trigger) {
    if (busy) return;
    global.AlKhayalUI.modal({ title: "تصدير " + options.arabicName, body: "<p>اختر صيغة الملف المطلوبة. سيشمل التصدير كل السجلات المطابقة للفلاتر الحالية، وليس الصفحة المعروضة فقط.</p>", actions: '<button class="button" type="button" data-modal-close>إلغاء</button><button class="button" type="button" data-export-format="csv">تصدير إلى CSV</button><button class="button primary" type="button" data-export-format="xlsx">تصدير إلى Excel</button>', onOpen: function (modal) { modal.querySelectorAll("[data-export-format]").forEach(function (button) { button.onclick = function () { var format = button.getAttribute("data-export-format"); global.AlKhayalUI.closeModal(); run(options, format, trigger); }; }); } });
  }
  global.AlKhayalExports = { choose: choose, run: run, isBusy: function () { return busy; } };
}(window));
