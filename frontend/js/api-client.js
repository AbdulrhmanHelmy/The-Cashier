(function (global) {
  "use strict";
  var csrfKey = "alkhayal_csrf";
  var activePageRoute = null, pageReads = Object.create(null), pageReadGenerations = Object.create(null), pageReadRequestIds = Object.create(null), pageReadSequence = 0;
  var errorMessages = { maintenance: "تجري استعادة قاعدة البيانات. حاول مجددًا بعد اكتمالها.", installment_read_failed: "تعذر تحميل عقود الأقساط من قاعدة البيانات. راجع رقم الخطأ الظاهر أو سجل التطبيق.", unauthorized: "انتهت جلسة الدخول.", forbidden: "لا تملك صلاحية تنفيذ هذا الإجراء.", invalid_csrf: "انتهت صلاحية الطلب. أعد المحاولة.", not_found: "تعذر العثور على البيانات المطلوبة.", conflict: "تعارضت العملية مع تغيير أحدث. حدّث الشاشة وأعد المحاولة.", invalid_response: "استجابة غير صالحة من الخادم.", database_busy: "قاعدة البيانات مشغولة حاليًا. أعد المحاولة بعد لحظات." };
  function safeErrorMessage(value, code, status) { var message = String(value == null ? "" : value).trim(); if (code === "installment_read_failed") return /[\u0600-\u06ff]/.test(message) ? message : errorMessages.installment_read_failed; if (code === "conflict" && /[\u0600-\u06ff]/.test(message)) return message; if (errorMessages[code]) return errorMessages[code]; if (status >= 500 || /\/api\/|(?:System|Microsoft)\.[A-Za-z.]|Exception|stack trace| at [A-Za-z0-9_$.-]+\(/i.test(message)) return "تعذر تنفيذ الطلب. أعد المحاولة."; if (message && /[\u0600-\u06ff]/.test(message)) return message; if (status === 401) return errorMessages.unauthorized; if (status === 403) return errorMessages.forbidden; if (status === 404) return errorMessages.not_found; if (status === 409) return errorMessages.conflict; return "تعذر تنفيذ الطلب. أعد المحاولة."; }
  function rememberCsrf(data) { if (data && data.csrfToken) sessionStorage.setItem(csrfKey, data.csrfToken); return data; }
  var volatileFields = { durationMinutes: true, elapsedMinutes: true, ageMinutes: true, generatedUtc: true, serverUtc: true, currentUtc: true, checkedUtc: true };
  function stableValue(value) { if (Array.isArray(value)) return value.map(stableValue); if (value && typeof value === "object") { var result = {}; Object.keys(value).sort().forEach(function (key) { if (!volatileFields[key]) result[key] = stableValue(value[key]); }); return result; } return value; }
  function trackedForScreen(route, path) { if (route !== "pos") return true; return ["/api/inventory/products", "/api/inventory/categories", "/api/inventory/warehouses", "/api/payment-methods/active"].indexOf(path) >= 0; }
  function displayedValue(route, path, data) {
    var fields = route === "pos" ? path === "/api/inventory/warehouses" ? ["id", "name", "isActive", "isDefault"] : path === "/api/inventory/categories" ? ["id", "name"] : path === "/api/payment-methods/active" ? ["id", "name", "number", "type", "isActive"] : null : path === "/api/treasury/accounts" && ["customer-credit-payment", "customer-statement", "employee-advances", "employee-payroll"].indexOf(route) >= 0 ? ["id", "name", "type", "isActive", "isDeleted"] : null;
    if (!fields) return data;
    function pick(row) { var result = {}; fields.forEach(function (field) { if (row && Object.prototype.hasOwnProperty.call(row, field)) result[field] = row[field]; }); return result; }
    return Array.isArray(data) ? data.map(pick) : pick(data);
  }
  function fingerprint(data, route, path) { var serialized = JSON.stringify(stableValue(displayedValue(route, path, data))), value = serialized === undefined ? "<undefined>" : serialized, hash = 2166136261; for (var i = 0; i < value.length; i++) { hash ^= value.charCodeAt(i); hash = Math.imul(hash, 16777619); } return (hash >>> 0).toString(16) + ":" + value.length; }
  async function request(path, options) {
    var config = Object.assign({ credentials: "same-origin" }, options || {});
    var trackingRoute = activePageRoute, trackingGeneration = trackingRoute ? (pageReadGenerations[trackingRoute] || 0) : 0;
    var trackingPath = path.split("?")[0], trackingRequestId = 0;
    if (methodIsGet(config) && trackingRoute && !config.skipPageTrack && path.indexOf("/api/") === 0 && trackedForScreen(trackingRoute, trackingPath)) { if (!pageReadRequestIds[trackingRoute]) pageReadRequestIds[trackingRoute] = Object.create(null); trackingRequestId = pageReadRequestIds[trackingRoute][trackingPath] = ++pageReadSequence; }
    config.headers = Object.assign({ "Content-Type": "application/json", "Accept": "application/json" }, config.headers || {});
    var method = (config.method || "GET").toUpperCase(), csrf = sessionStorage.getItem(csrfKey);
    if (method !== "GET" && method !== "HEAD" && csrf) config.headers["X-CSRF-Token"] = csrf;
    var response, timeoutId = null, timedOut = false;
    if (global.AbortController && !config.signal) {
      var controller = new global.AbortController();
      config.signal = controller.signal;
      timeoutId = global.setTimeout(function () { timedOut = true; controller.abort(); }, 20000);
    }
    try { response = await fetch(path, config); }
    catch (networkError) { var unavailable = new Error(timedOut ? "انتهت مهلة الاتصال بالخادم. تحقق من اتصال التطبيق وحاول مجددًا." : "الخادم غير متاح حاليًا."); unavailable.code = timedOut ? "request_timeout" : "network_error"; unavailable.status = 0; throw unavailable; }
    finally { if (timeoutId !== null) global.clearTimeout(timeoutId); }
    var body = await response.json().catch(function () { return { success: false, error: { code: "invalid_response", message: "استجابة غير صالحة من الخادم." } }; });
    if (!response.ok || !body.success) { var code = body.error && body.error.code, error = new Error(safeErrorMessage(body.error && body.error.message, code, response.status)); error.code = code; error.status = response.status; if (response.status === 401) sessionStorage.removeItem(csrfKey); throw error; }
    if (method === "GET" && trackingRoute && !config.skipPageTrack && path.indexOf("/api/") === 0 && trackedForScreen(trackingRoute, trackingPath) && trackingGeneration === (pageReadGenerations[trackingRoute] || 0) && trackingRequestId === pageReadRequestIds[trackingRoute][trackingPath]) { if (!pageReads[trackingRoute]) pageReads[trackingRoute] = Object.create(null); pageReads[trackingRoute][trackingPath] = { path: path, fingerprint: fingerprint(body.data, trackingRoute, trackingPath) }; }
    if (method !== "GET" && method !== "HEAD" && global.dispatchEvent) global.dispatchEvent(new CustomEvent("alkhayal:data-changed", {detail:{path:path}}));
    return rememberCsrf(body.data);
  }
  function methodIsGet(config) { return (config.method || "GET").toUpperCase() === "GET"; }
  global.AlKhayalApi = { request: request, setActivePage: function (route) { activePageRoute = route; }, resetPageSnapshot: function (route) { pageReadGenerations[route] = (pageReadGenerations[route] || 0) + 1; pageReads[route] = Object.create(null); }, untrackPageSnapshot: function (route, path) { var key = path.split("?")[0]; if (pageReads[route]) delete pageReads[route][key]; if (pageReadRequestIds[route]) pageReadRequestIds[route][key] = ++pageReadSequence; }, checkPageSnapshot: async function (route) { var reads = Object.keys(pageReads[route] || {}).map(function (key) { return pageReads[route][key]; }); if (!reads.length) return false; var results = await Promise.all(reads.map(async function (saved) { var current = await request(saved.path, { skipPageTrack: true }); return fingerprint(current, route, saved.path.split("?")[0]) !== saved.fingerprint; })); return results.some(Boolean); }, clearSession: function () { sessionStorage.removeItem(csrfKey); }, errorMessage: safeErrorMessage };
}(window));
