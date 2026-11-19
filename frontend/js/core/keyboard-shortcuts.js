(function (global) {
  "use strict";
  function isTypingTarget(target) {
    if (!target) return false;
    var tagName = String(target.tagName || "").toLowerCase();
    return tagName === "input" || tagName === "textarea" || tagName === "select" || target.isContentEditable === true;
  }
  function isUnmodified(event) {
    return !event.ctrlKey && !event.altKey && !event.metaKey && !event.shiftKey;
  }
  var helpGroups = [
    { title: "عام", items: [
      ["F1", "عرض اختصارات لوحة المفاتيح"],
      ["F3", "فتح شاشة الكاشير اللمسي"],
      ["F10", "فتح إدارة الوردية"],
      ["Ctrl + K", "فتح البحث العام"],
      ["Esc", "إغلاق النافذة النشطة"]
    ] },
    { title: "الكاشير اللمسي", note: "تعمل هذه الاختصارات فقط عند فتح شاشة الكاشير اللمسي.", items: [
      ["F2", "التركيز على بحث الصنف أو الباركود"],
      ["F4", "فتح اختيار العميل"],
      ["F6", "بدء فاتورة جديدة"],
      ["F8", "إتمام البيع"],
      ["F9", "فتح اختيار طريقة الدفع"]
    ] }
  ];
  function helpMarkup() {
    return '<div class="keyboard-shortcuts-help" dir="rtl">' + helpGroups.map(function (group) {
      return '<section class="keyboard-shortcuts-group"><h3>' + group.title + '</h3>' + (group.note ? '<p class="keyboard-shortcuts-note">' + group.note + '</p>' : '') + '<div class="keyboard-shortcuts-list">' + group.items.map(function (item) {
        return '<div class="keyboard-shortcut-row"><kbd>' + item[0] + '</kbd><span>' + item[1] + '</span></div>';
      }).join("") + '</div></section>';
    }).join("") + '</div>';
  }
  function install(actions) {
    var document = global.document;
    document.addEventListener("keydown", function (event) {
      if (event.repeat) return;
      var modalLayer = actions.getModalLayer && actions.getModalLayer();
      if (event.key === "Escape") {
        if (modalLayer && !modalLayer.hidden && typeof modalLayer.onkeydown === "function") {
          modalLayer.onkeydown(event);
        }
        return;
      }
      if (actions.isTouchPos && actions.isTouchPos() && !(modalLayer && !modalLayer.hidden) && actions.captureTouchBarcode && actions.captureTouchBarcode(event)) return;
      if (event.key === "F1" && isUnmodified(event)) {
        if (isTypingTarget(event.target) || (modalLayer && !modalLayer.hidden)) return;
        event.preventDefault();
        if (actions.showHelp) actions.showHelp(helpMarkup());
        return;
      }
      if (event.key === "F3" && isUnmodified(event)) {
        if (isTypingTarget(event.target)) return;
        event.preventDefault();
        actions.navigate("pos");
        return;
      }
      if (event.key === "F10" && isUnmodified(event)) {
        if (isTypingTarget(event.target)) return;
        event.preventDefault();
        actions.openShift();
        return;
      }
      if ((event.key === "k" || event.key === "K") && event.ctrlKey && !event.altKey && !event.metaKey && !event.shiftKey) {
        if (isTypingTarget(event.target)) return;
        event.preventDefault();
        actions.focusSearch();
        return;
      }
      if (!actions.isTouchPos || !actions.isTouchPos() || (modalLayer && !modalLayer.hidden) || !isUnmodified(event) || isTypingTarget(event.target)) return;
      if (event.key === "F2") {
        event.preventDefault();
        actions.focusTouchSearch();
        return;
      }
      if (event.key === "F4") {
        event.preventDefault();
        actions.focusTouchCustomer();
        return;
      }
      if (event.key === "F6") {
        event.preventDefault();
        actions.newTouchInvoice();
        return;
      }
      if (event.key === "F8") {
        event.preventDefault();
        actions.postTouchSale();
        return;
      }
      if (event.key === "F9") {
        event.preventDefault();
        actions.focusTouchPayment();
      }
    });
  }
  global.AlKhayalKeyboardShortcuts = { install: install, helpMarkup: helpMarkup };
}(window));
