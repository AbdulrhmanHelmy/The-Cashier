"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");
var Element = require("./inventory-tests.js").Element;
var source = fs.readFileSync(path.join(__dirname, "../js/modules/backup-settings.js"), "utf8");

function settle() { return new Promise(function (resolve) { setImmediate(resolve); }); }

module.exports = async function () {
  var container = new Element("section"), modal, modalOptions, loadingOptions, calls = [], pending = [], modalCloses = 0;
  var status = { configuration: { directory: "D:\\Backups", enabled: false }, files: [] };
  var cleared = false, redirected, notified, nativePicker = 0;
  var sessionKeys = ["other", "alkhayal_sale_post_1"];
  var win = {
    AlKhayalRenderers: {},
    AlKhayalHasBackupDialogs: true,
    chrome: { webview: { hostObjects: { alkhayalBackupDialogs: {
      pickFolder: async function () { nativePicker++; return "E:\\USB"; },
      openConfiguredFolder: async function () { return true; }
    } } } },
    AlKhayalUtils: {
      number: String,
      escape: function (value) { return String(value).replace(/</g, "&lt;").replace(/"/g, "&quot;"); }
    },
    AlKhayalApi: {
      clearSession: function () { cleared = true; },
      request: function (url, options) {
        calls.push({ url: url, options: options });
        if (url.endsWith("database-info")) return Promise.resolve({});
        if (options.method === "GET") return Promise.resolve(status);
        return new Promise(function (resolve, reject) { pending.push({ resolve: resolve, reject: reject }); });
      }
    },
    AlKhayalUI: {
      pageHeader: function () { return ""; },
      closeModal: function () { modal = null; modalCloses++; },
      loadingModal: function (options) {
        loadingOptions = options;
        modalOptions = { loading: true, beforeClose: function () { return false; } };
        modal = new Element("section");
        modal.innerHTML = '<div data-loading-title>' + options.title + '</div><div data-loading-message>' + options.message + '</div>';
      },
      toast: function () {},
      modal: function (options) {
        modalOptions = options;
        modal = new Element("section");
        modal.innerHTML = options.body + options.actions;
        var form = modal.querySelector("[data-backup-manual]");
        if (form) {
          form.elements = { folder: form.querySelector('[name="folder"]') };
          form.reportValidity = function () { return true; };
          form.requestSubmit = function () { form.onsubmit({ preventDefault: function () {} }); };
        }
        options.onOpen(modal);
      }
    },
    sessionStorage: {
      get length() { return sessionKeys.length; },
      key: function (index) { return sessionKeys[index]; },
      removeItem: function (key) { sessionKeys.splice(sessionKeys.indexOf(key), 1); }
    },
    alert: function (message) { notified = message; },
    location: { replace: function (url) { redirected = url; } }
  };
  var documentStub = {
    createElement: function (tag) { return new Element(tag); },
    createTextNode: function (text) { var node = new Element("span"); node.textContent = text; return node; }
  };
  vm.runInNewContext(source, { window: win, document: documentStub, AbortController: AbortController }, { filename: "backup-settings.js" });
  win.AlKhayalRenderers["database-settings"]({}, container, { isActive: function () { return true; } });
  var message = container.querySelector("[data-backup-message]"), failed;
  message.classList = { toggle: function (_, value) { failed = value; } };
  await settle();

  var create = container.querySelector("[data-backup-create]");
  create.onclick();
  assert.ok(modal.querySelector("[data-backup-manual]"), "manual creation asks for a destination first");
  modal.querySelector("[data-backup-browse]").onclick();
  await settle();
  assert.strictEqual(nativePicker, 1);
  var save = modal.querySelector("[data-backup-save]");
  save.onclick();
  save.onclick();
  assert.ok(create.disabled, "manual controls are disabled while the backup is pending");
  assert.strictEqual(pending.length, 1);
  assert.strictEqual(loadingOptions.title, "جاري إنشاء النسخة الاحتياطية");
  assert.strictEqual(loadingOptions.message, "يرجى الانتظار حتى الانتهاء من حفظ بياناتك...");
  assert.ok(modal && modalOptions.beforeClose() === false, "backup loading remains visible and cannot close while pending");
  assert.strictEqual(JSON.parse(calls[calls.length - 1].options.body).directory, "E:\\USB");
  pending.shift().reject(new Error("مساحة غير كافية"));
  await settle();
  assert.strictEqual(modal, null, "backup failure closes the loading modal");
  assert.strictEqual(create.disabled, false);
  assert.ok(failed && message.textContent.includes("مساحة"));

  var form = container.querySelector("[data-backup-config]");
  form.elements = { folder: { value: "E:\\USB" }, enabled: { checked: true } };
  form.onsubmit({ preventDefault: function () {} });
  assert.deepStrictEqual(JSON.parse(calls[calls.length - 1].options.body), { directory: "E:\\USB", enabled: true });
  status.configuration = { directory: "E:\\USB", enabled: true };
  pending.shift().resolve(status);
  await settle();

  container.querySelector("[data-backup-create]").onclick();
  modal.querySelector("[data-backup-save]").onclick();
  assert.ok(modal && loadingOptions.title === "جاري إنشاء النسخة الاحتياطية");
  var successfulBackup = { name: "saved.alkbackup", path: "E:\\USB\\saved.alkbackup", size: 42 };
  status.files = [successfulBackup];
  pending.shift().resolve(successfulBackup);
  await settle();
  await settle();
  await settle();
  assert.strictEqual(modal, null, "backup success closes the loading modal");
  assert.ok(message.textContent.includes("تم إنشاء النسخة") && message.textContent.includes("42"), message.textContent);

  var picker = container.querySelector("[data-backup-file]"), picked = false;
  picker.click = function () { picked = true; };
  container.querySelector("[data-backup-choose]").onclick();
  assert.ok(picked);
  var file = { name: "portable.alkbackup" };
  picker.files = [file];
  picker.onchange();
  var upload = calls[calls.length - 1];
  assert.ok(upload.url.endsWith("/inspect"));
  assert.strictEqual(upload.options.body, file);
  assert.strictEqual(upload.options.headers["Content-Type"], "application/octet-stream");
  assert.strictEqual(modal, null, "confirmation appears only after inspection");
  pending.shift().resolve({ applicationVersion: "<source>", schemaVersion: "090", size: 10, confirmation: "approved-token" });
  await settle();
  assert.ok(modal.innerHTML.includes("&lt;source>") && modal.innerHTML.includes("ستُستبدل بيانات العمل"));
  assert.strictEqual(pending.length, 0, "inspection alone must not restore");
  var confirm = modal.querySelector("[data-backup-confirm]");
  confirm.onclick();
  confirm.onclick();
  assert.strictEqual(pending.length, 1);
  assert.strictEqual(loadingOptions.title, "جاري استعادة النسخة الاحتياطية");
  assert.strictEqual(loadingOptions.message, "يرجى الانتظار، لا تغلق البرنامج أثناء استعادة البيانات...");
  assert.ok(modal && modalOptions.beforeClose() === false, "restore loading remains visible and cannot close while pending");
  var restore = calls[calls.length - 1];
  assert.ok(restore.url.endsWith("/restore"));
  assert.deepStrictEqual(JSON.parse(restore.options.body), { confirmation: "approved-token" });
  pending.shift().reject(new Error("تعذر الاتصال أثناء الاستعادة"));
  await settle();
  assert.strictEqual(modal, null, "restore failure closes the loading modal");
  assert.ok(failed && message.textContent.includes("تعذر الاتصال"));
  assert.strictEqual(redirected, undefined, "an interrupted request must not be treated as restore success");

  picker.files = [file];
  picker.onchange();
  pending.shift().resolve({ applicationVersion: "source", schemaVersion: "090", size: 10, confirmation: "second-token" });
  await settle();
  modal.querySelector("[data-backup-confirm]").onclick();
  pending.shift().resolve({ path: "D:\\Recovery\\emergency.alkbackup" });
  await settle();
  assert.ok(modalCloses >= 5, "completed and failed operations close their modal state");
  assert.ok(cleared && notified.includes("emergency.alkbackup"));
  assert.strictEqual(redirected, "/login.html");
  assert.deepStrictEqual(sessionKeys, ["other"], "retry state must not carry across a restore");
  console.log("Backup Settings destination, loading and failure states, daily configuration, confirmation and session reset passed.");
};
