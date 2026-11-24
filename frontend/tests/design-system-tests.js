"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const read = file => fs.readFileSync(path.join(__dirname, "..", file), "utf8");
const tokenMap = style => Object.fromEntries([...style.matchAll(/(--tc-[\w-]+):\s*([^;]+);/g)].map(match => [match[1], match[2]]));
const [lightCss, darkCss] = read("css/themes.css").split(':root[data-theme="dark"]');
const primitives = tokenMap(read("css/tokens.css"));

function scheme(css) {
  const tokens = Object.assign({}, primitives, tokenMap(css));
  function value(name) {
    const token = tokens["--tc-" + name];
    assert.ok(token, "Missing token " + name);
    const reference = /^var\(--tc-([\w-]+)\)$/.exec(token);
    return reference ? value(reference[1]) : token;
  }
  return value;
}

function luminance(color) {
  assert.match(color, /^#[a-f\d]{6}$/i);
  const channels = color.slice(1).match(/../g).map(hex => parseInt(hex, 16) / 255)
    .map(channel => channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4);
  return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
}

function contrast(foreground, background) {
  const a = luminance(foreground), b = luminance(background);
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

for (const [mode, css, primary, gold, app] of [
  ["light", lightCss, "#115E59", "#F5B700", "#F6FAF9"],
  ["dark", darkCss, "#14B8A6", "#FFCC33", "#151719"]
]) {
  const color = scheme(css);
  assert.equal(color("brand-primary"), primary);
  assert.equal(color("gold-action"), gold);
  assert.equal(color("gold-on-action"), "#172926");
  assert.equal(color("bg-app"), app);
  if (mode === "dark") {
    const expected = {
      "bg-surface": "#202426", "bg-subtle": "#292E30", "bg-elevated": "#292E30",
      "border-subtle": "#3B4244", "sidebar-bg": "#134E4A", "text-primary": "#F2F7F6",
      "text-secondary": "#B8CECA", "brand-on-primary": "#101A1B"
    };
    for (const [token, value] of Object.entries(expected)) assert.equal(color(token), value, token);
    assert.ok(contrast(color("brand-primary"), color("brand-selected")) >= 3, "Selected-row marker must remain visible");
  }
  for (const [foreground, background, minimum] of [
    ["text-primary", "bg-app", 4.5], ["text-secondary", "bg-surface", 4.5],
    ["text-placeholder", "bg-surface", 4.5], ["border-control", "bg-surface", 3],
    ["border-control", "bg-elevated", 3], ["text-secondary", "bg-elevated", 4.5],
    ["brand-on-selected", "brand-selected", 4.5],
    ["brand-on-primary", "brand-primary", 4.5], ["brand-on-primary", "brand-primary-hover", 4.5],
    ["brand-on-primary", "brand-primary-pressed", 4.5],
    ["gold-on-action", "gold-action", 4.5], ["gold-on-action", "gold-action-hover", 4.5],
    ["gold-on-action", "gold-action-pressed", 4.5], ["gold-text", "bg-surface", 4.5],
    ["danger-on-button", "danger-button", 4.5], ["focus-outer", "bg-elevated", 3],
    ["sidebar-text", "sidebar-bg", 4.5], ["sidebar-active-text", "sidebar-active-bg", 4.5],
    ["success-text", "success-bg", 4.5], ["error", "error-bg", 4.5],
    ["warning", "warning-bg", 4.5], ["info", "info-bg", 4.5]
  ]) {
    const ratio = contrast(color(foreground), color(background));
    assert.ok(ratio >= minimum, mode + " " + foreground + "/" + background + " = " + ratio.toFixed(2));
  }
}

for (const entry of ["index.html", "login.html"]) {
  const html = read(entry);
  assert.ok(html.indexOf('src="js/core/theme.js"') < html.indexOf('href="css/tokens.css"'), entry + " applies the theme before CSS");
}

const attributes = {};
const listeners = {};
const media = { matches: true, addEventListener: (name, callback) => { listeners.system = callback; } };
const document = {
  addEventListener() {},
  querySelectorAll: () => [],
  documentElement: { setAttribute: (name, value) => { attributes[name] = value; } }
};
let saved = "dark";
const window = {
  localStorage: { getItem: () => saved, setItem: (key, mode) => { saved = mode; } },
  matchMedia: () => media,
  addEventListener: (name, callback) => { listeners[name] = callback; }
};
vm.runInNewContext(read("js/core/theme.js"), { document, window });
assert.equal(attributes["data-theme"], "dark");
window.AlKhayalTheme.set("light");
assert.equal(saved, "light");
assert.equal(attributes["data-theme"], "light");
window.AlKhayalTheme.set("system");
assert.equal(attributes["data-theme"], "dark");
media.matches = false;
listeners.system();
assert.equal(attributes["data-theme"], "light");
listeners.storage({ key: "alkhayal_theme", newValue: "dark" });
assert.equal(attributes["data-theme"], "dark");
window.localStorage.setItem = () => { throw new Error("Unavailable"); };
window.AlKhayalTheme.set("light");
listeners.system();
assert.equal(attributes["data-theme"], "light");
console.log("Stage 1 theme tokens, contrast, prepaint initialization, and preference lifecycle passed.");
