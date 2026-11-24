(function () {
  "use strict";
  // One body-level overlay reuses the already filtered sidebar tree, never a second route list.
  window.AlKhayalSidebarFlyout = function (options) {
    var nav = options.navigation, shell = options.shell, trigger = null, timer = null, hoveredSource = null, panelHovered = false;
    var panel = document.createElement("nav"), title = document.createElement("strong"), body = document.createElement("div");
    panel.id = "sidebarFlyout";
    panel.className = "sidebar-flyout";
    panel.hidden = true;
    panel.dir = "rtl";
    panel.setAttribute("aria-labelledby", "sidebarFlyoutTitle");
    title.id = "sidebarFlyoutTitle";
    title.className = "sidebar-flyout-title";
    body.className = "sidebar-flyout-body";
    panel.appendChild(title);
    panel.appendChild(body);
    document.body.appendChild(panel);
    function collapsed() { return shell.classList.contains("sidebar-collapsed"); }
    function cancelClose() { clearTimeout(timer); timer = null; }
    function close(restoreFocus) {
      cancelClose();
      var previous = trigger;
      if (previous) {
        previous.setAttribute("aria-expanded", String(!collapsed() && previous.closest(".sidebar-group").classList.contains("open")));
        previous.removeAttribute("aria-controls");
      }
      trigger = null;
      hoveredSource = null;
      panelHovered = false;
      panel.hidden = true;
      body.textContent = "";
      if (restoreFocus && previous && nav.contains(previous)) {
        previous.focus();
      }
    }
    function scheduleClose() {
      cancelClose();
      timer = setTimeout(function () {
        if (!panelHovered && hoveredSource !== trigger && !panel.contains(document.activeElement)) close(false);
      }, 160);
    }
    function position() {
      update();
      if (!trigger) return;
      if (!collapsed()) { close(panel.contains(document.activeElement)); return; }
      var rect = trigger.getBoundingClientRect(), navRect = nav.getBoundingClientRect();
      if (rect.bottom <= navRect.top || rect.top >= navRect.bottom) { close(panel.contains(document.activeElement)); return; }
      var header = document.querySelector(".topbar"), footer = document.querySelector(".statusbar");
      var top = Math.max(8, header ? header.getBoundingClientRect().bottom : 8);
      var bottom = Math.min(window.innerHeight - 8, footer ? footer.getBoundingClientRect().top : window.innerHeight - 8);
      var edge = Math.max(8, nav.closest(".sidebar").getBoundingClientRect().left);
      panel.style.width = Math.max(0, Math.min(300, edge - 8)) + "px";
      panel.style.maxHeight = Math.max(0, bottom - top) + "px";
      panel.style.left = Math.max(8, edge - panel.getBoundingClientRect().width) + "px";
      panel.style.top = Math.max(top, Math.min(rect.top, bottom - panel.getBoundingClientRect().height)) + "px";
    }
    function update() {
      nav.querySelectorAll("[data-module-toggle]").forEach(function (button) {
        button.setAttribute("aria-expanded", String(button === trigger || (!collapsed() && button.closest(".sidebar-group").classList.contains("open"))));
      });
      body.querySelectorAll("[data-route]").forEach(function (button) {
        var active = button.getAttribute("data-route") === options.activeRoute();
        button.classList.toggle("active", active);
        if (active) button.setAttribute("aria-current", "page");
        else button.removeAttribute("aria-current");
      });
    }
    function open(button, focusFirst) {
      if (!button || !collapsed()) return;
      cancelClose();
      if (trigger !== button) {
        // Mouse movement must not destroy a keyboard user's focused destination.
        if (!focusFirst && panel.contains(document.activeElement)) return;
        close(false);
        trigger = button;
        title.textContent = button.getAttribute("title");
        var children = button.closest(".sidebar-group").querySelector(".sidebar-children");
        body.appendChild(children.cloneNode(true));
        button.setAttribute("aria-controls", panel.id);
        button.setAttribute("aria-expanded", "true");
        panel.hidden = false;
        update();
        position();
      }
      if (focusFirst) { var first = body.querySelector("[data-route]"); if (first) first.focus(); }
    }
    function source(event) { return event.target.closest("[data-module-toggle]"); }
    nav.addEventListener("pointerover", function (event) { var button = source(event); if (button && !button.contains(event.relatedTarget)) { open(button, false); hoveredSource = button; } });
    nav.addEventListener("pointerout", function (event) { var button = source(event); if (button && !button.contains(event.relatedTarget)) { hoveredSource = null; scheduleClose(); } });
    nav.addEventListener("focusout", scheduleClose);
    nav.addEventListener("keydown", function (event) {
      var button = source(event);
      if (!button || !collapsed()) return;
      if (event.key === "Escape") { event.preventDefault(); close(false); }
      else if (event.key === "ArrowLeft" || event.key === "ArrowDown") {
        event.preventDefault(); open(button, true);
      }
    });
    panel.addEventListener("pointerenter", function () { panelHovered = true; cancelClose(); });
    panel.addEventListener("pointerleave", function () { panelHovered = false; scheduleClose(); });
    panel.addEventListener("focusin", cancelClose);
    panel.addEventListener("focusout", scheduleClose);
    panel.addEventListener("keydown", function (event) {
      if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); close(true); return; }
      if (event.key !== "Tab") return;
      var links = Array.prototype.slice.call(body.querySelectorAll("[data-route]")), index = links.indexOf(document.activeElement);
      if (event.shiftKey && index === 0) { event.preventDefault(); close(true); }
      else if (!event.shiftKey && index === links.length - 1) {
        var buttons = Array.prototype.slice.call(nav.querySelectorAll(".sidebar-group-toggle")), next = buttons[buttons.indexOf(trigger) + 1] || document.querySelector('.sidebar-footer button:not([hidden])');
        if (next) { event.preventDefault(); close(false); next.focus(); }
      }
    });
    panel.addEventListener("click", function (event) {
      var link = event.target.closest("[data-route]");
      if (!link) return;
      var route = link.getAttribute("data-route");
      close(panel.contains(document.activeElement));
      options.navigate(route);
    });
    document.addEventListener("pointerdown", function (event) { if (trigger && !panel.contains(event.target) && !trigger.contains(event.target)) close(false); });
    document.addEventListener("keydown", function (event) {
      if (trigger && event.key === "Escape") { event.preventDefault(); close(panel.contains(document.activeElement)); }
    });
    window.addEventListener("resize", position);
    nav.addEventListener("scroll", position);
    return { open: open, close: close, update: update };
  };
}());
