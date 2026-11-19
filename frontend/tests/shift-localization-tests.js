"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");
var source = fs.readFileSync(path.join(__dirname, "../js/core/shift-localization.js"), "utf8");
module.exports = async function () {
  var window = {};
  vm.runInNewContext(source, { window: window, Intl: Intl });
  var L = window.AlKhayalShiftLocalization;
  ["cash_sale", "credit_sale", "collection", "in", "out", "deposit"].forEach(function (kind, index) {
    assert.strictEqual(L.eventType(kind), ["بيع نقدي", "بيع آجل", "تحصيل نقدي", "إيداع نقدي", "سحب نقدي", "إيداع نقدي"][index]);
  });
  assert.strictEqual(L.action("shift-open"), "فتح الوردية");
  assert.strictEqual(L.action("shift-close"), "إغلاق الوردية");
  assert.strictEqual(L.action("created"), "تم الإنشاء");
  assert.strictEqual(L.action("posted"), "تم ترحيل الفاتورة");
  assert.strictEqual(L.activityDescription("audit", "shift-close — expected=123400;counted=120000"), "إغلاق الوردية — النقدية المتوقعة: 12.34 ج.م — النقدية المعدودة: 12.00 ج.م");
  assert.strictEqual(L.activityDescription("invoice-audit", "posted — inventoryDocumentId=42"), "تم ترحيل الفاتورة — رقم حركة المخزون: 42");
  assert.strictEqual(L.activityDescription("operation", "POST /api/workforce/shifts/1 — 201"), "تم تنفيذ العملية بنجاح");
  assert.ok(!L.activityDescription("operation", "POST /api/secret — 500").includes("/api/"));
  assert.strictEqual(L.actor("مسؤول النظام", 1), "مسؤول النظام");
  assert.strictEqual(L.actor("الكاشير الثاني", 2), "الكاشير الثاني");
  assert.strictEqual(L.actor("", 2), "مستخدم غير معروف (#2)");
  assert.strictEqual(L.actor(null, 99), "مستخدم غير معروف (#99)");
  assert.strictEqual(L.eventType("future-event"), "حركة نقدية");
  assert.strictEqual(L.activitySource("future-source"), "نشاط الوردية");
  console.log("Shift activity Arabic localization and scaled numeric formatting tests passed.");
};
