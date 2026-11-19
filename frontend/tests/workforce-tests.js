"use strict";
var assert = require('assert'), fs = require('fs'), path = require('path'), vm = require('vm');
var Element = require('./inventory-tests.js').Element;
var workforce = fs.readFileSync(path.join(__dirname, '../js/modules/workforce.js'), 'utf8');
var registry = JSON.parse(fs.readFileSync(path.join(__dirname, '../data/screens.json'), 'utf8'));
function settle() { return new Promise(function (resolve) { setImmediate(resolve); }); }
function harness(deferred) {
  var calls = [], pending = [], reads = [], options, modal, active = true, seq = 0, currentShift = null;
  var rows = [{ id: 1, code: 'EMP-000001', name: 'موظف محفوظ', version: 1, salary: '1000.00', isActive: true, attendanceEnabled: false }];
  var win = { document: { getElementById: function(){return null;}, querySelectorAll: function(){return [];}, addEventListener: function(){} }, crypto: { getRandomValues: function (bytes) { bytes.fill(++seq); } }, AlKhayalRenderers: {}, AlKhayalUtils: { escape: function (v) { return String(v == null ? '' : v); }, debounce: function (f) { return f; }, dateTime: String }, AlKhayalFeatures: { isEnabled: function () { return false; } },
    AlKhayalUI: { pageHeader: function (s, a) { return a || ''; }, toast: function () {}, closeModal: function () { modal.isConnected = false; },
      field: function (d, v) { if (d.type === 'select') return '<label>' + d.label + '<select name="' + d.id + '">' + d.options.map(function (o) { return '<option value="' + o.value + '"' + (String(v || '') === String(o.value) ? ' selected' : '') + '>' + o.label + '</option>'; }).join('') + '</select></label>'; return '<label>' + d.label + '<input name="' + d.id + '" value="' + (v == null ? '' : v) + '"></label>'; },
      modal: function (o) { options = o; modal = new Element('section'); modal.innerHTML = o.body + o.actions; var f = modal.querySelector('[data-workforce-form]'); if (f) { f.elements = f.querySelectorAll('input,select,textarea'); f.elements.forEach(function (n) { n.name = n.attrs.name; }); f.reportValidity = function () { return true; }; } o.onOpen(modal); }
    }, AlKhayalApi: { request: function (url, config) {
      calls.push({ url: url, config: config });
      if (config) return new Promise(function (resolve, reject) { pending.push({ resolve: resolve, reject: reject }); });
      if (url === '/api/workforce/shifts/current') return Promise.resolve(currentShift);
      if (url === '/api/users') return Promise.resolve([{ id: 3, displayName: 'مدير', username: 'owner', isActive: true }]);
      if (url === '/api/treasury/accounts') return Promise.resolve([{ id: 7, name: 'الخزنة', balance: '10000.00' }]);
      if (url.indexOf('/api/workforce/payroll?') === 0 || url.indexOf('/api/workforce/advances') === 0) return Promise.resolve([]);
      if (url.indexOf('/api/workforce/attendance?') === 0) return Promise.resolve({ items: [{ id: 11, employeeId: 1, employeeName: 'موظف محفوظ', checkInUtc: '2026-10-31T08:00:00Z', checkOutUtc: null, breakMinutes: 0, workedSeconds: 60, version: 1 }], total: 1, page: 1, pageSize: 25 });
      if (deferred) return new Promise(function (resolve, reject) { reads.push({ resolve: resolve, reject: reject }); });
      return Promise.resolve({ items: rows, total: rows.length, page: 1, pageSize: 25 });
    } }
  };
  vm.runInNewContext(workforce, { window: win, Uint8Array: Uint8Array });
  var context = { state: {}, user: { id: 3, role: 'administrator' }, isActive: function () { return active; }, navigate: function () {} };
  return { win:win, setShift:function(s){currentShift=s;}, calls: calls, pending: pending, reads: reads, rows: rows, context: context, options: function () { return options; }, modal: function () { return modal; }, leave: function () { active = false; },
    render: function (route) { var node = new Element('section'), s = registry.screens.find(function (r) { return r.route === route; }); win.AlKhayalRenderers[s.renderer](s, node, context); var form=node.querySelector("[data-filters]"); if(form){form.elements=form.querySelectorAll("input,select,textarea");form.elements.forEach(function(n){n.name=n.attrs.name;});} return node; },
    writes: function () { return calls.filter(function (c) { return c.config; }); }
  };
}
async function employeeForms() {
  var h = harness(), node = h.render('employees-list'); await settle(); await settle();
  assert.ok(node.querySelector('[data-results]').innerHTML.includes('موظف محفوظ'));
  var input = node.querySelector('[name="search"]'); input.value = 'بحث'; input.oninput(); await settle();
  assert.strictEqual(node.querySelector('[name="search"]'), input, 'search stays mounted');
  assert.ok(h.calls.some(function (c) { return c.url.includes('search=%D8%A8%D8%AD%D8%AB'); }));
  node.querySelector('[data-new]').onclick(); var modal = h.modal(), name = modal.querySelector('[name="name"]'); name.value = 'الاسم فقط';
  modal.querySelector('[name="nationalId"]').value = '12345678901234';
  var form = modal.querySelector('[data-workforce-form]'), button = modal.querySelector('[data-save]'); button.onclick(); button.onclick();
  assert.equal(h.writes().length, 1); assert.equal(h.options().beforeClose(), false);
  var body = JSON.parse(h.writes()[0].config.body); assert.equal(body.name, 'الاسم فقط'); assert.equal(body.nationalId, '12345678901234'); assert.equal(body.salary, null); assert.equal(body.payday, null); assert.equal(body.linkedUserId, null); assert.equal(body.attendanceEnabled, false);
  h.pending[0].reject({ status: 0, message: 'فقد الرد' }); await settle(); assert.equal(button.disabled, false); assert.equal(name.disabled, true);
  button.onclick(); assert.deepStrictEqual(JSON.parse(h.writes()[1].config.body), body, 'lost response retry preserves key and payload');
  h.pending[1].reject({ status: 400, message: 'حقل غير صالح' }); await settle(); assert.equal(name.disabled, false); assert.equal(name.value, 'الاسم فقط'); assert.equal(h.options().beforeClose(), true);
  form.onsubmit({ preventDefault: function () {} }); h.pending[2].resolve({ id: 1 }); await settle();
  assert.strictEqual(node.querySelector('[name="search"]'), input);
}
async function staleReads() {
  var h = harness(true), node = h.render('employees-list'); await settle();
  var search = node.querySelector('[name="search"]'); search.value = 'الأحدث'; search.oninput();
  h.reads[1].resolve({ items: [{ id: 2, name: 'الأحدث', code: 'EMP-000002' }], total: 1, page: 1, pageSize: 25 }); await settle();
  h.reads[0].resolve({ items: [{ id: 1, name: 'قديم', code: 'EMP-000001' }], total: 1, page: 1, pageSize: 25 }); await settle();
  assert.ok(node.querySelector('[data-results]').innerHTML.includes('الأحدث')); assert.ok(!node.querySelector('[data-results]').innerHTML.includes('قديم'));
  search.value = 'غير مرئي'; search.oninput(); h.leave(); var before = node.querySelector('[data-results]').innerHTML;
  h.reads[2].resolve({ items: [], total: 0, page: 1, pageSize: 25 }); await settle(); assert.equal(node.querySelector('[data-results]').innerHTML, before);
}
async function shiftForm() {
  var h = harness(), node = h.render('shifts-list'); await settle();
  node.querySelector('[data-open-shift]').onclick(); await settle();
  var modal = h.modal(); modal.querySelector('[name="amount"]').value = '10.10'; modal.querySelector('[data-save]').onclick(); modal.querySelector('[data-save]').onclick();
  assert.equal(h.writes().length, 1); var body = JSON.parse(h.writes()[0].config.body); assert.equal(body.amount, '10.10'); assert.equal(body.cashierId, 3); assert.equal(h.writes()[0].url, '/api/workforce/shifts/open');
  h.pending[0].reject({ status: 409, message: 'وردية مفتوحة' }); await settle(); assert.equal(modal.querySelector('[data-save]').disabled, false);
}
async function payrollAndAdvanceForms() {
  var h = harness(), node = h.render('employee-payroll'); await settle(); await settle();
  var employee = node.querySelector('[name="employeeId"]'); employee.value = '1'; employee.onchange(); await settle(); await settle();
  assert.ok(node.querySelector('[data-payroll-quote]').innerHTML.includes('1000.00'), 'payroll quote reads salary from the employee API result');
  var pay = node.querySelector('[data-pay]'); assert.strictEqual(pay.disabled, false);
  pay.onclick(); var modal = h.modal(); modal.querySelector('[data-save]').onclick(); await settle();
  var payload = JSON.parse(h.writes()[0].config.body); assert.strictEqual(payload.employeeId, 1); assert.strictEqual(payload.treasuryAccountId, 7); assert.strictEqual(payload.period.length, 7); assert.strictEqual(h.writes()[0].url, '/api/workforce/payroll/pay');
  var a = harness(), advancesNode = a.render('employee-advances'); await settle(); await settle();
  advancesNode.querySelector('[data-add-advance]').onclick(); var advanceModal = a.modal(); advanceModal.querySelector('[name="employeeId"]').value = '1'; advanceModal.querySelector('[name="amount"]').value = '50.00'; advanceModal.querySelector('[data-save]').onclick(); await settle();
  var advancePayload = JSON.parse(a.writes()[0].config.body); assert.strictEqual(advancePayload.employeeId, 1); assert.strictEqual(advancePayload.amount, '50.00'); assert.strictEqual(advancePayload.treasuryAccountId, 7); assert.strictEqual(a.writes()[0].url, '/api/workforce/advances');
}
async function attendanceBreakForm() {
  var h = harness(), node = h.render('attendance'); await settle();
  var breakButton = node.querySelector('[data-break="11"]'); breakButton.closest('[data-results]').onclick({ target: breakButton }); var modal = h.modal(); modal.querySelector('[name="breakMinutes"]').value = '30'; modal.querySelector('[data-save]').onclick();
  var payload = JSON.parse(h.writes()[0].config.body); assert.strictEqual(payload.id, 11); assert.strictEqual(payload.breakMinutes, 30); assert.strictEqual(h.writes()[0].url, '/api/workforce/attendance/break');
}
async function shiftStatusAndCheckout() {
  var h=harness(), header=new Element('button'), checkout=new Element('button');checkout.disabled=false;
  header.classList={add:function(){}};
  checkout.hasAttribute=function(k){return Object.prototype.hasOwnProperty.call(this.attrs,k);};checkout.removeAttribute=function(k){delete this.attrs[k];};
  checkout.setAttribute('data-sale-ready','true');
  h.win.document={getElementById:function(){return header;},querySelectorAll:function(selector){return selector==='[data-post]'?[checkout]:[];},addEventListener:function(){}};
  h.win.addEventListener=function(){};h.win.setInterval=function(){};h.win.clearInterval=function(){};
  h.win.AlKhayalWorkforce.header(h.context);await settle();
  assert.equal(header.title,'افتح وردية أولاً');assert.equal(checkout.disabled,true);assert.equal(checkout.textContent,'افتح وردية أولاً');
  h.setShift({id:7,cashierName:'كاشير',openedUtc:new Date(Date.now()-65*60000).toISOString()});await h.win.AlKhayalWorkforce.refresh();
  assert.ok(header.title.includes('كاشير'));assert.ok(header.title.includes('#7'));assert.equal(checkout.disabled,false);
  h.setShift(null);await h.win.AlKhayalWorkforce.refresh();assert.equal(checkout.disabled,true);
  checkout.setAttribute('data-sale-ready','false');h.setShift({id:8,openedUtc:new Date().toISOString()});await h.win.AlKhayalWorkforce.refresh();assert.equal(checkout.disabled,true,'empty/busy checkout remains disabled after opening');
}
async function closeConfirmation() {
  var h=harness(), header=new Element('button');
  header.classList={add:function(){}};
  h.win.document.getElementById=function(){return header;};h.win.addEventListener=function(){};h.win.setInterval=function(){};h.win.clearInterval=function(){};
  h.setShift({id:9,cashierName:'كاشير',openedUtc:new Date().toISOString(),expected:'10.00',expectedScaled:100000});
  h.win.AlKhayalWorkforce.header(h.context);await settle();await settle();header.onclick();for(var wait=0;wait<10&&!h.modal();wait++)await settle();
  h.modal().querySelector('[data-close-current]').onclick();var modal=h.modal(), form=modal.querySelector('[data-workforce-form]');
  modal.querySelector('[name="amount"]').value='9.25';var confirm=modal.querySelector('[name="confirmClose"]');confirm.type='checkbox';confirm.checked=false;
  form.oninput();assert.ok(modal.querySelector('[data-close-difference]').textContent.includes('-0.75'));
  modal.querySelector('[data-save]').onclick();assert.equal(h.writes().length,0,'closing needs explicit confirmation');
  confirm.checked=true;modal.querySelector('[data-save]').onclick();modal.querySelector('[data-save]').onclick();assert.equal(h.writes().length,1);
  var body=JSON.parse(h.writes()[0].config.body);assert.equal(body.id,9);assert.equal(body.expectedScaled,100000);assert.equal(body.amount,'9.25');
  h.pending[0].reject({status:409,message:'تغير النقد المتوقع'});await settle();assert.equal(modal.querySelector('[data-save]').disabled,false);
}
module.exports = async function () {
  ['employees-list', 'employee-payroll', 'employee-advances', 'attendance', 'attendance-history', 'shifts-list', 'shift-reports', 'shift-details'].forEach(function (route) { assert.ok(registry.screens.find(function (s) { return s.route === route; }).renderer.startsWith('workforce-')); });
  assert.ok(workforce.includes('discrepanciesOnly') && workforce.includes('shifts/current'));
  assert.ok(!workforce.includes('window.print') && !workforce.includes('mock-services'));
  await closeConfirmation(); await shiftStatusAndCheckout(); await employeeForms(); await staleReads(); await shiftForm(); await payrollAndAdvanceForms(); await attendanceBreakForm();
};
