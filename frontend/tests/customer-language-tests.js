"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path");

function walk(dir, extensions, files) {
  fs.readdirSync(dir, { withFileTypes: true }).forEach(function (entry) {
    var full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name !== "bin" && entry.name !== "obj" && entry.name !== "tests") walk(full, extensions, files);
    } else if (extensions.indexOf(path.extname(entry.name)) >= 0) files.push(full);
  });
}

module.exports = function () {
  var root = path.resolve(__dirname, "../..");
  var frontend = [], backend = [];
  walk(path.join(root, "frontend"), [".js", ".json", ".html", ".css"], frontend);
  walk(path.join(root, "src"), [".cs"], backend);
  frontend.forEach(function (file) {
    assert.ok(!/FIFO/i.test(fs.readFileSync(file, "utf8")), "Customer-facing frontend contains FIFO: " + file);
  });
  backend.forEach(function (file) {
    var source = fs.readFileSync(file, "utf8");
    var quotedArabic = /"(?:[^"\\]|\\.)*?[\u0600-\u06ff](?:[^"\\]|\\.)*?"/g;
    var match;
    while ((match = quotedArabic.exec(source))) {
      assert.ok(!/FIFO/i.test(match[0]), "Customer-facing Arabic message contains FIFO: " + file);
    }
  });
};
