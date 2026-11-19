"use strict";
// Source/DOM contracts only. These checks do not measure visual geometry.
var assert = require("assert"), fs = require("fs"), path = require("path");
var css = fs.readFileSync(path.join(__dirname, "../css/modules.css"), "utf8");
var sales = fs.readFileSync(path.join(__dirname, "../js/modules/sales.js"), "utf8");
var app = fs.readFileSync(path.join(__dirname, "../js/app.js"), "utf8");
var tokens = fs.readFileSync(path.join(__dirname, "../css/tokens.css"), "utf8");
assert.ok(css.includes("container: touch-workspace / size"), "measure mounted workspace, including sidebar and chrome");
["max-width: 760px", "max-height: 560px"].forEach(function(condition) {
  assert.ok(css.includes("@container touch-workspace (" + condition + ")"), condition);
});
assert.ok(/\.touch-pos\s*\{[^}]*grid-template-rows: auto minmax\(0,1fr\)/.test(css), "toolbar height must be intrinsic");
assert.ok(/\.touch-head\s*\{[^}]*display: flex;[^}]*flex-wrap: wrap/.test(css), "toolbar wraps without empty fixed columns");
assert.ok(/\.touch-pos \.sale-actions\s*\{[^}]*repeat\(2, minmax\(0, 1fr\)\)/.test(css), "both modes use the same bounded action grid");
assert.ok(!/\.touch-pos[^}]*100(?:d)?vh/.test(css), "POS cannot own a second viewport");
assert.ok(css.includes(".touch-pos .touch-lines, .touch-pos .sale-summary { overflow: visible; }"), "short/narrow fallback has a single invoice scroll owner");
assert.ok(css.includes(".touch-pos { display: block; overflow: auto; }"), "overflowing checkout remains reachable");
assert.ok(css.includes(".touch-pos .touch-body { grid-template-columns: minmax(0,1fr); align-items: start; }"), "narrow workspace stacks products above the invoice");
assert.ok(css.includes("grid-template-columns: minmax(90px,1fr) 112px 80px minmax(76px,110px) 36px"), "five bounded desktop columns including large totals");
assert.ok(css.includes("@container touch-cart (max-width: 440px)"), "fallback responds to actual cart width below the desktop minimum");
assert.ok(css.includes(".touch-pos .sale-remove { width: 36px; min-width: 36px; }"), "remove action cannot shrink");
assert.ok(css.includes(".touch-pos .sale-line-name strong { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }"), "long names stay bounded");
assert.ok(!/\.touch-pos \.sale-(?:quantity|line-price|line-total|remove)[^{]*\{[^}]*display:\s*none/.test(css), "essential controls are never hidden");
assert.ok(sales.includes("data-page-refresh") && app.includes('button.closest(".page-refresh-fallback")'), "native refresh suppresses shell fallback");
assert.ok(css.includes("grid-template-columns: minmax(0,1fr) minmax(520px,1.2fr)"), "products stay compact while the invoice receives about 55% and a practical minimum");
assert.ok(css.includes(".touch-pos .touch-product-scroll { container: touch-browse / inline-size; }"), "measure actual product scroller after sidebar/pane allocation");
assert.ok(css.includes(".touch-pos .touch-product-grid { grid-template-columns: minmax(0,1fr); }"), "very narrow panes start with one stretching column");
var productRules=Array.from(css.matchAll(/@container touch-browse \(min-width: (\d+)px\)\s*\{\s*\.touch-pos \.touch-product-grid\s*\{\s*grid-template-columns: repeat\((\d+),minmax\(0,1fr\)\);\s*\}\s*\}/g),function(match){return {width:Number(match[1]),columns:Number(match[2])};});
assert.deepStrictEqual(productRules,[{width:225,columns:2},{width:340,columns:3},{width:455,columns:4}],"one to four compact equal columns; no capped-width cards or extra wide-screen columns");
function productColumns(width){return productRules.reduce(function(columns,rule){return width>=rule.width?rule.columns:columns;},1);}
[[100,1],[224,1],[225,2],[339,2],[340,3],[454,3],[455,4],[1200,4]].forEach(function(sample){
  assert.equal(productColumns(sample[0]),sample[1],"product container width "+sample[0]);
  if(sample[1]>1)assert.ok((sample[0]-(sample[1]-1)*7)/sample[1]>=108,"cards retain compact touch width");
});
// Arithmetic checks the declared sizing policy, not browser-rendered geometry.
[1024,1280,1366,1440,1600,1920].forEach(function(viewport){
  [Number(tokens.match(/--sidebar-collapsed:\s*(\d+)px/)[1]),Number(tokens.match(/--sidebar-width:\s*(\d+)px/)[1])].forEach(function(sidebar){
    var available=viewport-sidebar-16-8,tracks=available-18-17,cartWidth=Math.max(520,tracks*1.2/2.2),browseWidth=tracks-cartWidth;
    assert.ok(cartWidth>=520&&browseWidth>0,"bounded panels at "+viewport+" / sidebar "+sidebar);
    assert.ok(cartWidth-14>=90+112+80+76+36+20+4,"cart fits all five cells without narrow fallback");
    if(viewport>=1366)assert.equal(productColumns(browseWidth),4,"four cards at standard desktop widths / sidebar "+sidebar);
  });
});
function hasClass(node, name) { return (node.getAttribute("class") || "").split(/\s+/).indexOf(name) >= 0; }
module.exports = function(host, existing) {
  var sections = host.querySelectorAll("section"), pos = sections.find(function(n) { return hasClass(n, "touch-pos"); });
  assert.ok(pos, "shared Touch POS root");
  var head = pos.children[0], body = pos.children[1], cart = body.children[1];
  assert.ok(hasClass(head, "touch-head") && hasClass(body, "touch-body"));
  assert.equal(pos.getAttribute("dir"),"rtl","product browsing keeps Arabic ordering");
  var products=body.children[0],browser=products.children[1];
  assert.ok(hasClass(products,"touch-products")&&hasClass(browser,"touch-product-scroll"),"new and draft modes use the measured product pane");
  assert.ok(hasClass(browser.children[0],"touch-product-grid"),"same responsive product grid in every invoice mode");
  assert.deepStrictEqual(cart.children.map(function(n) { return n.getAttribute("class"); }), ["touch-cart-head", "touch-lines", "sale-summary"], "same header/items/summary structure in both modes");
  ["[data-search]", "[data-customer-search]", "[data-page-refresh]", "[data-back]"].forEach(function(selector) {
    assert.strictEqual(head.querySelectorAll(selector).length, 1, selector + " occurs once in toolbar");
  });
  var ids = host.querySelectorAll("[id]").map(function(n) { return n.getAttribute("id"); });
  assert.strictEqual(new Set(ids).size, ids.length, "unique IDs");
  var summary = cart.children[2];
  assert.equal(host.querySelectorAll("[data-line-discount]").length,0,"Touch has no per-item discount inputs in any mode");
  host.querySelectorAll("[data-line]").forEach(function(row){
    assert.equal(row.children.length,5,"one row: details, quantity, price, total, remove");
    assert.ok(hasClass(row.children[0],"sale-line-name"));
    assert.ok(row.children[0].querySelector("strong").getAttribute("title"),"full name remains accessible");
    assert.ok(row.children[0].querySelector("summary"),"metadata is keyboard expandable");
    if(existing==="posted-view") assert.ok(hasClass(row.children[1],"sale-quantity-value"));
    else assert.ok(row.children[1].querySelector("[data-quantity]"));
    assert.ok(hasClass(row.children[2],"sale-line-price-cell"));
    assert.ok(hasClass(row.children[3],"sale-line-total"));
    if(existing==="posted-view") assert.ok(!Object.prototype.hasOwnProperty.call(row.children[4].attrs,"data-remove"));
    else assert.ok(Object.prototype.hasOwnProperty.call(row.children[4].attrs,"data-remove"));
  });
  ["[data-discount-mode]", "[data-discount]", "[data-total]", "[data-new-touch-invoice]"].forEach(function(selector) { assert.strictEqual(summary.querySelectorAll(selector).length, 1, selector); });
  var draftView=existing==="draft-view",draftEdit=existing==="draft-edit";
  var expected = draftView ? ["[data-resume-draft]"] : draftEdit ? ["[data-clear]", "[data-post]", "[data-cancel-changes]"] : existing==="posted-view" ? [] : existing ? ["[data-save-changes]", "[data-cancel-changes]"] : ["[data-clear]", "[data-post]"];
  var absent = draftView ? ["[data-clear]", "[data-save]", "[data-post]", "[data-save-changes]"] : draftEdit ? ["[data-save-changes]", "[data-resume-draft]"] : existing ? ["[data-clear]", "[data-post]"] : ["[data-save-changes]", "[data-cancel-changes]"];
  expected.forEach(function(selector) { assert.ok(summary.querySelector(selector), selector); });
  absent.forEach(function(selector) { assert.ok(!summary.querySelector(selector), selector + " absent"); });
};
if (require.main === module) console.log("Touch POS source layout contracts passed; run touch-pos-invoice-tests.js for rendered DOM/behavior. Visual geometry requires manual acceptance.");
