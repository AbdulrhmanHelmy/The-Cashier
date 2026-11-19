(function (global) {
  "use strict";
  var number = new Intl.NumberFormat("ar-EG-u-nu-latn");
  var money = new Intl.NumberFormat("ar-EG-u-nu-latn", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  var eventTypes = {
    cash_sale: "بيع نقدي",
    credit_sale: "بيع آجل",
    collection: "تحصيل نقدي",
    in: "إيداع نقدي",
    out: "سحب نقدي",
    deposit: "إيداع نقدي",
    withdrawal: "سحب نقدي"
  };
  var actions = {
    "shift-open": "فتح الوردية",
    "shift-close": "إغلاق الوردية",
    "shift-adjust": "تعديل نقدية الوردية",
    created: "تم الإنشاء",
    posted: "تم ترحيل الفاتورة",
    cancelled: "تم إلغاء الفاتورة",
    returned: "تم إرجاع الفاتورة",
    corrected: "تم تصحيح الفاتورة",
    draft: "مسودة",
    open: "مفتوحة",
    closed: "مغلقة"
  };
  var fields = {
    opening: "نقد الافتتاح",
    expected: "النقدية المتوقعة",
    counted: "النقدية المعدودة",
    amount: "المبلغ",
    cashier: "الكاشير رقم",
    direction: "نوع الحركة",
    inventoryDocumentId: "رقم حركة المخزون",
    salesInvoiceId: "رقم الفاتورة",
    reversalDocumentId: "رقم حركة العكس",
    correctionOf: "تصحيح الفاتورة رقم"
  };
  function text(value) { return String(value == null ? "" : value); }
  function scaled(value) {
    var parsed = Number(value);
    return isFinite(parsed) ? money.format(parsed / 10000) + " ج.م" : text(value);
  }
  function action(value) { return actions[text(value)] || "تحديث الوردية"; }
  function eventType(value) { return eventTypes[text(value)] || "حركة نقدية"; }
  function status(value) { return actions[text(value)] || "حالة غير محددة"; }
  function fieldLabel(value) { return fields[text(value)] || "تفاصيل الحركة"; }
  function fieldValue(key, value) {
    if (key === "opening" || key === "expected" || key === "counted" || key === "amount") return fieldLabel(key) + ": " + scaled(value);
    if (key === "direction") return fieldLabel(key) + ": " + eventType(value);
    return fieldLabel(key) + ": " + number.format(Number(value)) ;
  }
  function technicalDetails(value) {
    var source = text(value), parts = source.split(";"), result = [];
    parts.forEach(function (part) {
      var pair = /^([^=]+)=(.*)$/.exec(part);
      if (pair) result.push(fieldValue(pair[1], pair[2]));
      else if (part) result.push(part);
    });
    return result.join(" — ") || "لا توجد تفاصيل إضافية";
  }
  function activitySource(kind) {
    return { invoice: "الفاتورة", audit: "تدقيق الوردية", "invoice-audit": "تدقيق الفاتورة", treasury: "الخزنة", operation: "نشاط النظام" }[text(kind)] || "نشاط الوردية";
  }
  function activityDescription(kind, detail) {
    var value = text(detail), parts = value.split(" — ");
    if (kind === "invoice") return "الفاتورة " + parts[0] + (parts[1] ? " — " + status(parts[1]) : "") + (parts[2] ? " — الإجمالي: " + scaled(parts[2]) : "");
    if (kind === "audit") return action(parts[0]) + (parts.slice(1).join(" — ") ? " — " + technicalDetails(parts.slice(1).join(" — ")) : "");
    if (kind === "invoice-audit") return action(parts[0]) + (parts.slice(1).join(" — ") ? " — " + technicalDetails(parts.slice(1).join(" — ")) : "");
    if (kind === "treasury") return "حركة الخزنة رقم " + (parts[0].match(/#(\d+)/) || ["", "—"])[1] + (parts[1] ? " — " + scaled(parts[1]) : "");
    if (kind === "operation") {
      var code = /(\d{3})$/.exec(value);
      return code && /^2/.test(code[1]) ? "تم تنفيذ العملية بنجاح" : code ? "تعذر تنفيذ العملية" : "نشاط نظام مسجل";
    }
    return "نشاط مسجل في الوردية";
  }
  function operationDescription(method) {
    return { GET: "عرض بيانات", POST: "إضافة أو تنفيذ", PUT: "تعديل بيانات", PATCH: "تعديل بيانات", DELETE: "حذف بيانات" }[text(method)] || "عملية نظام";
  }
  function actor(displayName, id) {
    if (text(displayName).trim()) return text(displayName);
    return "مستخدم غير معروف (#" + text(id) + ")";
  }
  global.AlKhayalShiftLocalization = {
    eventType: eventType,
    action: action,
    activitySource: activitySource,
    activityDescription: activityDescription,
    formatScaled: scaled,
    operationDescription: operationDescription,
    actor: actor
  };
}(window));
