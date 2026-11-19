(function (global) {
  "use strict";
  function visibleCount(widths, available, moreWidth, gap) {
    var total = widths.reduce(function (sum, width) { return sum + width; }, 0) + Math.max(0, widths.length - 1) * gap;
    if (total <= available) return widths.length;
    var used = moreWidth, count = 0;
    while (count < widths.length - 1 && used + gap + widths[count] <= available) {
      used += gap + widths[count];
      count++;
    }
    return count;
  }
  global.AlKhayalQuickbarLayout = { visibleCount: visibleCount };
}(window));
