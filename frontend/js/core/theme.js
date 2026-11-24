(function (global) {
  "use strict";
  var key = "alkhayal_theme", media = global.matchMedia ? global.matchMedia("(prefers-color-scheme: dark)") : null;
  var current = "system";
  function valid(mode) { return mode === "light" || mode === "dark" || mode === "system"; }
  try { var saved = global.localStorage.getItem(key); if (valid(saved)) current = saved; } catch (storageUnavailable) { /* The in-memory preference remains usable. */ }
  function preference() { return current; }
  function resolved(mode) { return mode === "system" ? media && media.matches ? "dark" : "light" : mode; }
  function updateButtons(mode) { Array.prototype.forEach.call(document.querySelectorAll("[data-theme-choice]"), function (button) { var active = button.getAttribute("data-theme-choice") === mode; button.classList.toggle("active", active); button.setAttribute("aria-pressed", active ? "true" : "false"); }); }
  function apply(mode) { var selected = mode || preference(); document.documentElement.setAttribute("data-theme", resolved(selected)); document.documentElement.setAttribute("data-theme-mode", selected); updateButtons(selected); }
  function set(mode) {
    if (!valid(mode)) return;
    current = mode;
    try { global.localStorage.setItem(key, mode); } catch (storageUnavailable) { /* Keep the preference for this page. */ }
    apply(mode);
  }
  document.addEventListener("click", function (event) { var choice = event.target.closest("[data-theme-choice]"); if (choice) set(choice.getAttribute("data-theme-choice")); });
  if (media) { var listener = function () { if (preference() === "system") apply("system"); }; if (media.addEventListener) media.addEventListener("change", listener); else if (media.addListener) media.addListener(listener); }
  global.addEventListener("storage", function (event) { if (event.key === key || event.key === null) { current = valid(event.newValue) ? event.newValue : "system"; apply(); } });
  apply(); document.addEventListener("DOMContentLoaded", function () { apply(); }); global.AlKhayalTheme = { apply: apply, set: set, preference: preference };
}(window));
