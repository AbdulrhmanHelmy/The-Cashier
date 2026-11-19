"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");

module.exports = async function () {
  var uiSource = fs.readFileSync(path.join(__dirname, "../js/components/ui.js"), "utf8");
  var apiSource = fs.readFileSync(path.join(__dirname, "../js/api-client.js"), "utf8");
  var window = { AlKhayalUtils: { escape: String } };
  vm.runInNewContext(uiSource, { window: window });
  var UI = window.AlKhayalUI;
  assert.strictEqual(UI.displayLabel("pending"), "قيد الانتظار");
  assert.strictEqual(UI.displayLabel("out_for_delivery"), "خرج للتوصيل");
  assert.strictEqual(UI.displayLabel("حالة مخصصة"), "حالة مخصصة");
  assert.strictEqual(UI.displayLabel("future_status"), "حالة غير معروفة");
  assert.strictEqual(UI.displayError("تعذر حفظ السجل."), "تعذر حفظ السجل.");
  assert.strictEqual(UI.displayError("System.InvalidOperationException at /api/users"), "تعذر تنفيذ الطلب. أعد المحاولة.");

  var apiWindow = {};
  vm.runInNewContext(apiSource, {
    window: apiWindow,
    sessionStorage: { getItem: function () { return null; }, setItem: function () {}, removeItem: function () {} },
  });
  var errorMessage = apiWindow.AlKhayalApi.errorMessage;
  assert.strictEqual(errorMessage("فشل الطلب في /api/users", "server_error", 400), "تعذر تنفيذ الطلب. أعد المحاولة.");
  assert.strictEqual(errorMessage("تعذر حفظ السجل.", "validation_error", 400), "تعذر حفظ السجل.");
  assert.strictEqual(errorMessage("Internal Server Error", "server_error", 500), "تعذر تنفيذ الطلب. أعد المحاولة.");
  assert.strictEqual(errorMessage("future status", "future_status", 400), "تعذر تنفيذ الطلب. أعد المحاولة.");
  console.log("User-facing Arabic status and error localization tests passed.");
};
