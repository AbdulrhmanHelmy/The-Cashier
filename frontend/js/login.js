(function () {
  "use strict";
  var form = document.getElementById("loginForm"), username = document.getElementById("username"), password = document.getElementById("password"), remember = document.getElementById("remember"), forget = document.getElementById("forgetPassword"), note = document.getElementById("credentialNote"), error = document.getElementById("formError"), button = document.getElementById("loginButton"), spinner = document.getElementById("spinner"), status = document.getElementById("statusText"), dot = document.getElementById("statusDot");
  var bootstrapPanel = document.getElementById("bootstrapPanel"), bootstrapForm = document.getElementById("bootstrapForm"), bootstrapUsername = document.getElementById("bootstrapUsername"), bootstrapPassword = document.getElementById("bootstrapPassword"), bootstrapConfirm = document.getElementById("bootstrapConfirmPassword"), bootstrapError = document.getElementById("bootstrapError"), bootstrapButton = document.getElementById("bootstrapButton"), bootstrapSpinner = document.getElementById("bootstrapSpinner");
  var desktopCredentials = window.chrome && window.chrome.webview && window.chrome.webview.hostObjects && window.chrome.webview.hostObjects.sync && window.chrome.webview.hostObjects.sync.alkhayalCredentials;
  function cleanName() { return username.value.trim(); }
  function loadSavedPassword() { password.value = ""; remember.checked = false; forget.hidden = true; if (!desktopCredentials || !cleanName()) return; try { var value = desktopCredentials.get(cleanName()); if (value) { password.value = String(value); remember.checked = true; forget.hidden = false; } } catch (ignored) {} }
  if (desktopCredentials) { remember.disabled = false; note.hidden = false; note.textContent = "تُحفظ كلمة المرور محليًا ومحمية لحساب Windows الحالي فقط."; username.addEventListener("change", loadSavedPassword); username.addEventListener("blur", loadSavedPassword); forget.addEventListener("click", function () { try { desktopCredentials.remove(cleanName()); } catch (ignored) {} password.value = ""; remember.checked = false; forget.hidden = true; }); }
  else { note.hidden = false; note.textContent = "حفظ كلمة المرور متاح داخل تطبيق سطح المكتب فقط."; }
  AlKhayalApi.request("/api/auth/login-users").then(function (users) { var list = document.getElementById("loginUsers"); (users || []).forEach(function (user) { var option = document.createElement("option"); option.value = user.username; option.label = user.displayName ? user.displayName + " — " + user.username : user.username; list.appendChild(option); }); }).catch(function () {});

  AlKhayalApi.request("/api/auth/me").then(function () { location.replace("/index.html"); }).catch(function () {
    AlKhayalApi.request("/api/auth/bootstrap-status").then(function (data) {
      if (data && data.required) { form.hidden = true; bootstrapPanel.hidden = false; document.querySelector(".login-heading h2").textContent = "إعداد التشغيل الأول"; document.querySelector(".login-heading p").textContent = "أنشئ حساب المدير المحلي للمتابعة"; bootstrapUsername.focus(); }
    }).catch(function () {});
  });
  document.getElementById("togglePassword").addEventListener("click", function () { password.type = password.type === "password" ? "text" : "password"; this.textContent = password.type === "password" ? "إظهار" : "إخفاء"; this.setAttribute("aria-label", this.textContent + " كلمة المرور"); });
  fetch("/api/system/health", { credentials: "same-origin" }).then(function (response) { if (!response.ok) throw new Error(); status.textContent = "متصل بالخادم"; dot.style.background = "var(--success)"; }).catch(function () { status.textContent = "الخادم غير متاح"; dot.style.background = "var(--danger)"; });

  form.addEventListener("submit", async function (event) {
    event.preventDefault(); document.getElementById("usernameError").textContent = ""; document.getElementById("passwordError").textContent = ""; error.textContent = "";
    if (!username.value.trim()) { document.getElementById("usernameError").textContent = "أدخل اسم المستخدم."; username.focus(); return; } if (!password.value) { document.getElementById("passwordError").textContent = "أدخل كلمة المرور."; password.focus(); return; }
    button.disabled = true; spinner.hidden = false;
    try { await AlKhayalApi.request("/api/auth/login", { method: "POST", body: JSON.stringify({ username: cleanName(), password: password.value }) }); if (desktopCredentials) { if (remember.checked) desktopCredentials.save(cleanName(), password.value); else desktopCredentials.remove(cleanName()); } location.replace("/index.html"); }
    catch (loginError) { if (loginError.code === "invalid_credentials") error.textContent = "اسم المستخدم أو كلمة المرور غير صحيحة."; else if (loginError.code === "disabled_account") error.textContent = "هذا الحساب موقوف. راجع مسؤول النظام."; else if (loginError.code === "network_error") error.textContent = "تعذر الوصول إلى الخادم المحلي."; else error.textContent = loginError.message || "تعذر تسجيل الدخول."; }
    finally { button.disabled = false; spinner.hidden = true; }
  });

  bootstrapForm.addEventListener("submit", async function (event) {
    event.preventDefault(); bootstrapError.textContent = ""; document.getElementById("bootstrapUsernameError").textContent = ""; document.getElementById("bootstrapPasswordError").textContent = ""; document.getElementById("bootstrapConfirmError").textContent = "";
    if (!bootstrapUsername.value.trim()) { document.getElementById("bootstrapUsernameError").textContent = "أدخل اسم المستخدم."; bootstrapUsername.focus(); return; }
    if (!bootstrapPassword.value) { document.getElementById("bootstrapPasswordError").textContent = "أدخل كلمة المرور."; bootstrapPassword.focus(); return; }
    if (bootstrapPassword.value !== bootstrapConfirm.value) { document.getElementById("bootstrapConfirmError").textContent = "كلمة المرور وتأكيدها غير متطابقين."; bootstrapConfirm.focus(); return; }
    bootstrapButton.disabled = true; bootstrapSpinner.hidden = false;
    try { await AlKhayalApi.request("/api/auth/bootstrap", { method: "POST", body: JSON.stringify({ username: bootstrapUsername.value.trim(), password: bootstrapPassword.value, confirmPassword: bootstrapConfirm.value }) }); location.replace("/index.html"); }
    catch (setupError) { bootstrapError.textContent = setupError.message || "تعذر إنشاء حساب المدير."; if (setupError.code === "bootstrap_unavailable") { bootstrapPanel.hidden = true; form.hidden = false; } }
    finally { bootstrapButton.disabled = false; bootstrapSpinner.hidden = true; }
  });
}());
