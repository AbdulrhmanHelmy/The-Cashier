(function (global) {
  "use strict";
  var U = {};
  // Only call this for numeric fields; identifiers and free text must remain untouched.
  U.normalizeNumericInput = function (value) { var text = String(value == null ? "" : value).replace(/[٠-٩۰-۹]/g, function (digit) { var code = digit.charCodeAt(0); return String(code >= 1776 ? code - 1776 : code - 1632); }).replace(/٫/g, "."); if (/^[+-]?\d{1,3}(?:٬\d{3})+(?:\.\d*)?$/.test(text)) text = text.replace(/٬/g, ""); return text; };
  if (global.document && global.document.addEventListener) {
    global.document.addEventListener("paste", function (event) {
      var field = event.target, clipboard = event.clipboardData;
      if (!field || field.tagName !== "INPUT" || (field.type !== "number" && field.getAttribute("inputmode") !== "decimal") || field.readOnly || field.disabled || !clipboard) return;
      var original = clipboard.getData("text"), normalized = U.normalizeNumericInput(original);
      if (original === normalized) return;
      // Native number controls do not expose selectionStart/setRangeText. insertText
      // uses Chromium's current editing selection and keeps its native validation.
      if (field.type === "number") {
        if (!/^[0-9.+-]+$/.test(normalized) || typeof global.document.execCommand !== "function") return;
        if (global.document.execCommand("insertText", false, normalized)) event.preventDefault();
        return;
      }
      event.preventDefault();
      var start = field.selectionStart, end = field.selectionEnd; field.setRangeText(normalized, start, end, "end");
      field.dispatchEvent(new Event("input", { bubbles: true }));
    }, true);
    global.document.addEventListener("input", function (event) {
      var field = event.target;
      if (!field || field.tagName !== "INPUT" || field.getAttribute("inputmode") !== "decimal") return;
      var normalized = U.normalizeNumericInput(field.value);
      if (normalized !== field.value) field.value = normalized;
    }, true);
  }
  U.escape = function (value) { return String(value == null ? "" : value).replace(/[&<>"']/g, function (character) { return { "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[character]; }); };
  U.numericHtml = function (value) { var text = String(value == null ? "" : value), numeric = /^[+-]?(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d+)?$/.test(text), dateTime = /^(?:\d{4}[-/]\d{2}[-/]\d{2}|\d{2}[-/]\d{2}[-/]\d{4})(?:[ T]\d{1,2}:\d{2}(?::\d{2})?(?: [^\d\s]+)?)?$/.test(text); return numeric || dateTime ? '<bdi dir="ltr">' + U.escape(text) + '</bdi>' : U.escape(text); };
  U.money = function (value) { return new Intl.NumberFormat("ar-EG-u-nu-latn", { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(Number(value || 0)) + " ج.م"; };
  U.number = function (value) { return new Intl.NumberFormat("ar-EG-u-nu-latn").format(Number(value || 0)); };
  U.date = function (value) { var date = value instanceof Date ? value : new Date(value); return isNaN(date.getTime()) ? "—" : new Intl.DateTimeFormat("ar-EG-u-nu-latn", { year: "numeric", month: "2-digit", day: "2-digit" }).format(date); };
  U.dateTime = function (value) { var date = value instanceof Date ? value : new Date(value); return isNaN(date.getTime()) ? "—" : new Intl.DateTimeFormat("ar-EG-u-nu-latn", { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" }).format(date); };
  U.normalize = function (value) { return String(value == null ? "" : value).toLowerCase().replace(/[أإآ]/g, "ا").replace(/ة/g, "ه").replace(/ى/g, "ي").replace(/[\u064B-\u065F\u0670]/g, "").trim(); };
  U.uid = function (prefix) { return (prefix || "id") + "-" + Date.now().toString(36) + "-" + Math.floor(Math.random() * 100000).toString(36); };
  U.debounce = function (callback, wait) { var timer; return function () { var args = arguments, context = this; clearTimeout(timer); timer = setTimeout(function () { callback.apply(context, args); }, wait); }; };
  U.clone = function (value) { return JSON.parse(JSON.stringify(value)); };
  U.downloadCsv = function (filename, columns, rows) { var quote = function (value) { return '"' + String(value == null ? "" : value).replace(/"/g, '""') + '"'; }; var csv = "\ufeff" + columns.map(function (column) { return quote(column.label); }).join(",") + "\r\n" + rows.map(function (row) { return columns.map(function (column) { return quote(row[column.key]); }).join(","); }).join("\r\n"); var url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" })); var anchor = document.createElement("a"); anchor.href = url; anchor.download = filename; document.body.appendChild(anchor); anchor.click(); anchor.remove(); setTimeout(function () { URL.revokeObjectURL(url); }, 500); };
  global.AlKhayalUtils = U;
}(window));
