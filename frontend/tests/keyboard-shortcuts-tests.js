"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");

module.exports = function () {
  var appJs = fs.readFileSync(path.join(__dirname, "..", "js", "app.js"), "utf8");
  var listener, focused = 0, clicked = 0, navigated = [], modalKeydowns = 0, touchActive = true, touchActions = [], help = [];
  var input = { tagName: "INPUT", focus: function () { focused++; } };
  var modalLayer = { hidden: false, onkeydown: function (event) { modalKeydowns++; event.preventDefault(); } };
  function touchElement(name) {
    return { tagName: name === "search" || name === "customer" ? "INPUT" : "SELECT", disabled: false, focus: function () { touchActions.push("focus-" + name); }, click: function () { touchActions.push("click-" + name); } };
  }
  var touch = { search: touchElement("search"), customer: touchElement("customer"), newInvoice: touchElement("newInvoice"), post: touchElement("post"), payment: touchElement("payment") };
  var elements = { globalSearch: input, modalLayer: modalLayer, headerShift: { disabled: false, click: function () { clicked++; } } };
  var documentStub = {
    addEventListener: function (name, callback) { if (name === "keydown") listener = callback; },
    getElementById: function (id) { return elements[id] || null; }
  };
  var context = { window: {}, document: documentStub };
  context.window.document = documentStub;
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, "..", "js", "core", "keyboard-shortcuts.js"), "utf8"), context);
  context.window.AlKhayalKeyboardShortcuts.install({
    navigate: function (route) { navigated.push(route); },
    openShift: function () { elements.headerShift.click(); },
    focusSearch: function () { input.focus(); },
    showHelp: function (markup) { help.push(markup); },
    getModalLayer: function () { return modalLayer; },
    isTouchPos: function () { return touchActive; },
    focusTouchSearch: function () { touch.search.focus(); },
    focusTouchCustomer: function () { touch.customer.focus(); touch.customer.click(); },
    newTouchInvoice: function () { if (!touch.newInvoice.disabled) touch.newInvoice.click(); },
    postTouchSale: function () { if (!touch.post.disabled) touch.post.click(); },
    focusTouchPayment: function () { touch.payment.focus(); touch.payment.click(); }
  });
  function event(key, target, options) {
    options = options || {};
    return Object.assign({ key: key, target: target || { tagName: "BODY" }, repeat: false, ctrlKey: false, altKey: false, metaKey: false, shiftKey: false, preventDefault: function () { this.prevented = true; } }, options);
  }
  listener(event("F3"));
  listener(event("F10"));
  listener(event("k", null, { ctrlKey: true }));
  assert.deepStrictEqual(navigated, ["pos"], "F3 must navigate to Touch POS");
  assert.strictEqual(clicked, 1, "F10 must invoke the existing shift action");
  assert.strictEqual(focused, 1, "Ctrl+K must focus global search");
  listener(event("F1"));
  assert.strictEqual(help.length, 0, "F1 must not open over an active modal");
  listener(event("F3", input));
  listener(event("F10", input));
  listener(event("k", input, { ctrlKey: true }));
  assert.deepStrictEqual(navigated, ["pos"], "shortcuts must not run while typing");
  assert.strictEqual(clicked, 1, "shift shortcut must not run while typing");
  assert.strictEqual(focused, 1, "search shortcut must not run while typing");
  listener(event("F3", null, { repeat: true }));
  listener(event("F3", null, { ctrlKey: true }));
  assert.deepStrictEqual(navigated, ["pos"], "repeated or modified function keys must be ignored");
  listener(event("Escape", input));
  assert.strictEqual(modalKeydowns, 1, "Escape must use the active modal close handler");
  modalLayer.hidden = true;
  listener(event("F1"));
  assert.strictEqual(help.length, 1, "F1 must open the keyboard shortcuts help");
  assert.ok(appJs.indexOf('title: "اختصارات لوحة المفاتيح"') >= 0 && appJs.indexOf("UI.modal") >= 0 && appJs.indexOf("compact: true") >= 0, "F1 must use the compact existing modal with the required title");
  ["عام", "الكاشير اللمسي", "F3", "F10", "Ctrl + K", "Esc", "F2", "F4", "F6", "F8", "F9", "تعمل هذه الاختصارات فقط"].forEach(function (text) {
    assert.ok(help[0].indexOf(text) >= 0, "help popup must contain " + text);
  });
  listener(event("F1", input));
  listener(event("F1", null, { repeat: true }));
  listener(event("F1", null, { ctrlKey: true }));
  assert.strictEqual(help.length, 1, "F1 must ignore typing, repeats, and modifiers");
  modalLayer.hidden = true;
  listener(event("Escape"));
  assert.strictEqual(modalKeydowns, 1, "Escape must do nothing without an active modal");
  listener(event("F2"));
  listener(event("F4"));
  listener(event("F6"));
  listener(event("F8"));
  listener(event("F9"));
  assert.deepStrictEqual(touchActions, ["focus-search", "focus-customer", "click-customer", "click-newInvoice", "click-post", "focus-payment", "click-payment"], "Touch POS shortcuts must delegate to existing controls");
  touch.post.disabled = true;
  listener(event("F8"));
  assert.strictEqual(touchActions.length, 7, "checkout shortcut must not click a disabled button");
  touchActive = false;
  listener(event("F2"));
  assert.strictEqual(touchActions.length, 7, "Touch POS shortcuts must be inactive on other screens");
  touchActive = true;
  modalLayer.hidden = false;
  listener(event("F6"));
  assert.strictEqual(touchActions.length, 7, "Touch POS shortcuts must not run inside modals");
  modalLayer.hidden = true;
  listener(event("F2", null, { repeat: true }));
  listener(event("F2", null, { ctrlKey: true }));
  assert.strictEqual(touchActions.length, 7, "Touch POS shortcuts must ignore repeats and modifiers");
  console.log("Global keyboard shortcut tests passed.");
};
