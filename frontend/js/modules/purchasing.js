(function (global) {
  "use strict";
  var U = global.AlKhayalUtils, UI = global.AlKhayalUI;
  function esc(value) { return U.escape(value == null ? "" : String(value)); }
  function returnBadge(invoice) { return invoice.returnStatus === "full" ? '<span class="badge warning" data-invoice-return-status="full">مرتجعة بالكامل</span>' : invoice.returnStatus === "partial" ? '<span class="badge warning" data-invoice-return-status="partial">مرتجعة جزئيًا</span>' : ""; }
  function moneyScaled(value) { var n = BigInt(String(value || "0")), negative = n < 0n; if (negative) n = -n; var cents = (n + 50n) / 100n, whole = (cents / 100n).toString(), fraction = (cents % 100n).toString().padStart(2, "0"); return (negative ? "-" : "") + whole + "." + fraction; }
  function numScale(value, places) {
    var text = String(value || "0").trim(), parts = text.split("."), fraction = (parts[1] || "").padEnd(places, "0");
    if (fraction.length > places || !/^\d+$/.test(parts[0] || "0") || !/^\d*$/.test(fraction)) throw new Error("تحقق من دقة الكمية والسعر.");
    return BigInt(parts[0] || "0") * (10n ** BigInt(places)) + BigInt(fraction || "0");
  }
  function parsePhysicalUnits(text, withImei) {
    return String(text || "").split(/\r?\n/).map(function (row) { var parts = row.trim().split("|"), serial = (parts[0] || "").trim(); return serial ? { serial: serial, imeis: withImei ? (parts[1] || "").split(",").map(function (x) { return x.trim(); }).filter(Boolean) : [] } : null; }).filter(Boolean);
  }
  function totalFor(lines) {
    return lines.reduce(function (sum, line) {
      var raw = numScale(line.quantity, 6) * numScale(line.purchaseUnitPrice, 2), total = ((raw + 500000n) / 1000000n) * 100n;
      return sum + total;
    }, 0n);
  }
  function makeKey(id) {
    var keyName = "alkhayal_purchase_post_" + id, key = sessionStorage.getItem(keyName);
    if (!key) { key = "p-" + id + "-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 10); sessionStorage.setItem(keyName, key); }
    return key;
  }
  function options(rows, value, label) {
    return (rows || []).map(function (x) { return '<option value="' + esc(x.id) + '"' + (String(x.id) === String(value) ? " selected" : "") + ">" + esc(label(x)) + "</option>"; }).join("");
  }
  function currentView(context) {
    return context.isCurrent ? context.isCurrent() : !context.isActive || context.isActive();
  }
  function editorAvailable(context) {
    return !context.canNavigate || context.canNavigate("purchase-management");
  }
  function renderPurchase(screen, container, context) {
    var state = { invoice: null, suppliers: [], supplierResults: [], selectedSupplier: null, supplierQuery: "", supplierSearchPage: 1, supplierSearchTotal: 0, supplierSearchToken: 0, supplierLoading: false, warehouses: [], treasuryAccounts: [], treasuryAccountId: null, lines: [], products: [], productQuery: "", productPage: 1, productTotal: 0, productSearchOpen: false, productActiveIndex: -1, productSearchMessage: "", busy: false, addingProduct: false, feedback: "", lastPosted: null, dirty: false, loadToken: 0, supplierId: null, warehouseId: null, invoiceNumber: "", invoiceDate: null, paymentMode: "credit", notes: "" };
    container.isWorkspaceDirty = function () { return state.dirty; };
    container.beforeRouteReplace = function () { return !state.busy && !state.addingProduct && (!state.dirty || global.confirm("توجد تغييرات غير محفوظة في فاتورة الشراء. هل تريد تجاهلها؟")); };
    function purchaseStatus() {
      return state.feedback || (state.addingProduct ? "جارٍ تحميل بيانات الصنف…" : state.dirty ? "تغييرات غير محفوظة — احفظ المسودة قبل النشر" :
        state.invoice ? "مسودة محفوظة — جاهزة للنشر" : "احفظ المسودة قبل النشر");
    }
    function refreshPurchaseStatus() {
      var status = container.querySelector("[data-purchase-state]");
      if (status) status.textContent = purchaseStatus();
      var post = container.querySelector("[data-purchase-post]");
      if (post) post.disabled = !!(state.busy || state.addingProduct || state.dirty);
    }
    function markDirty() {
      state.dirty = true;
      state.feedback = "";
      refreshPurchaseStatus();
    }
    function supplierResultsHtml() {
      var rows = state.supplierResults.map(function (supplier) { return '<button class="button purchase-supplier-option" type="button" role="option" data-select-supplier="' + esc(supplier.id) + '">' + esc(supplier.name) + (supplier.phone ? ' — ' + esc(supplier.phone) : '') + '</button>'; }).join("");
      var more = state.supplierResults.length < state.supplierSearchTotal ? '<button class="button" type="button" data-more-suppliers' + (state.supplierLoading ? ' disabled' : '') + '>عرض المزيد (' + state.supplierResults.length + ' من ' + state.supplierSearchTotal + ')</button>' : "";
      return (state.supplierLoading ? '<span class="muted purchase-supplier-loading" role="status">جارٍ البحث عن الموردين...</span>' : "") + rows + more + (!state.supplierLoading && state.supplierQuery && !rows ? '<span class="muted">لا يوجد مورد مطابق.</span>' : "");
    }
    function productResultsHtml() {
      if (state.productSearchMessage) return '<div class="purchase-search-state" role="status">' + state.productSearchMessage + '</div>';
      var rows = state.products.map(function (product, index) {
        var active = index === state.productActiveIndex;
        return '<button class="purchase-result' + (active ? ' is-active' : '') + '" type="button" role="option" aria-selected="' + (active ? 'true' : 'false') + '" data-product-index="' + index + '" data-add-product="' + esc(product.id) + '"><strong>' + esc(product.name) + '</strong>' + (product.primarySku ? '<small dir="ltr">' + esc(product.primarySku) + '</small>' : '') + '</button>';
      }).join("");
      if (!rows) rows = '<div class="purchase-search-state muted">لا توجد أصناف مطابقة.</div>';
      return '<div class="purchase-product-options" role="listbox" aria-label="نتائج بحث المنتجات">' + rows + '</div>' +
        '<div class="pagination"><span>صفحة ' + state.productPage + '</span><div class="pagination-buttons"><button class="button" type="button" data-purchase-products-page="' + (state.productPage - 1) + '"' + (state.productPage <= 1 ? ' disabled' : '') + '>السابق</button><button class="button" type="button" data-purchase-products-page="' + (state.productPage + 1) + '"' + (state.productPage * 20 >= state.productTotal ? ' disabled' : '') + '>التالي</button></div></div>';
    }
    function renderProductResults() {
      var target = container.querySelector("#purchaseResults");
      if (!target) return;
      var search = container.querySelector("#purchaseSearch");
      if (search) search.setAttribute("aria-expanded", String(state.productSearchOpen));
      target.hidden = !state.productSearchOpen;
      target.innerHTML = state.productSearchOpen ? productResultsHtml() : "";
      var active = target.querySelector(".purchase-result.is-active");
      if (active && active.scrollIntoView) active.scrollIntoView({ block: "nearest" });
    }
    function searchSuppliers(query, pageNumber, append) {
      var token = ++state.supplierSearchToken;
      state.supplierSearchTimer = null;
      if (!query) { state.supplierLoading = false; state.supplierResults = []; state.supplierSearchTotal = 0; state.supplierSearchPage = 1; return; }
      state.supplierLoading = true;
      if (!append) { state.supplierResults = []; state.supplierSearchTotal = 0; }
      state.supplierSearchPage = pageNumber || 1;
      var target = container.querySelector("#purchaseSupplierResults");
      if (target) target.innerHTML = supplierResultsHtml();
      return AlKhayalApi.request("/api/suppliers?search=" + encodeURIComponent(query) + "&page=" + state.supplierSearchPage + "&pageSize=20").then(function (result) {
        if (token !== state.supplierSearchToken || query !== state.supplierQuery || !currentView(context)) return;
        var items = result.items || [];
        state.supplierResults = append ? state.supplierResults.concat(items) : items;
        state.suppliers = state.supplierResults;
        state.supplierSearchTotal = result.totalCount || 0;
        state.supplierLoading = false;
        var currentTarget = container.querySelector("#purchaseSupplierResults");
        if (currentTarget) { currentTarget.hidden = false; currentTarget.innerHTML = supplierResultsHtml(); }
      }).catch(function (error) {
        if (token !== state.supplierSearchToken || query !== state.supplierQuery || context.isActive && !context.isActive()) return;
        state.supplierLoading = false;
        var currentTarget = container.querySelector("#purchaseSupplierResults");
        if (currentTarget) { currentTarget.hidden = false; currentTarget.innerHTML = '<span class="field-error">' + esc(error.message || "تعذر البحث عن الموردين.") + '</span><button class="button" type="button" data-retry-suppliers>إعادة المحاولة</button>'; }
      });
    }
    function scheduleSupplierSearch(query) {
      if (state.supplierSearchTimer) clearTimeout(state.supplierSearchTimer);
      ++state.supplierSearchToken;
      state.supplierQuery = query.trim();
      if (state.selectedSupplier) markDirty();
      state.supplierId = null;
      state.selectedSupplier = null;
      var summary = container.querySelector("[data-purchase-supplier-summary]");
      if (summary) summary.innerHTML = '<small class="purchase-required-hint" id="purchaseSupplierSelected">لم يتم اختيار مورد بعد.</small>';
      state.suppliers = [];
      state.supplierSearchPage = 1;
      state.supplierResults = [];
      state.supplierSearchTotal = 0;
      var select = container.querySelector("#purchaseSupplier");
      if (select) select.value = "";
      state.supplierLoading = !!state.supplierQuery;
      var results = container.querySelector("#purchaseSupplierResults");
      if (results) { results.hidden = !state.supplierQuery; results.innerHTML = supplierResultsHtml(); }
      if (!state.supplierQuery) { state.supplierLoading = false; return; }
      state.supplierSearchTimer = setTimeout(function () { searchSuppliers(state.supplierQuery, 1, false); }, 250);
    }
    function displayLineTotal(line) {
      if (!line) return "";
      return line.lineTotalFormatted != null ? line.lineTotalFormatted :
        line.lineTotalScaled != null ? moneyScaled(line.lineTotalScaled) : "";
    }
    function page() {
      var total = "—";
      try { total = moneyScaled(totalFor(state.lines).toString()); } catch (e) { /* Invalid input has no preview. */ }
      var lineRows = state.lines.length ? state.lines.map(lineHtml).join("") : '<tr class="purchase-lines-empty"><td colspan="7"><div class="empty-state"><strong>لم تُضف أصناف بعد</strong><span>ابحث بالاسم أو الكود أو امسح الباركود لإضافة أول صنف.</span><button class="button" type="button" data-focus-product>إضافة صنف</button></div></td></tr>';
      var supplierSelected = state.selectedSupplier ? '<div class="purchase-selected-supplier"><div><span>المورد المختار</span><strong id="purchaseSupplierSelected">' + esc(state.selectedSupplier.name) + '</strong><small>' + esc("المستحق الحالي " + U.money(state.selectedSupplier.payable || 0)) + '</small></div><button class="button" type="button" data-change-supplier>تغيير</button></div>' : '<small class="purchase-required-hint" id="purchaseSupplierSelected">لم يتم اختيار مورد بعد.</small>';
      container.innerHTML = UI.pageHeader(screen) + '<article class="purchase-create">' +
        '<section class="card purchase-section purchase-head"' + (state.busy ? ' inert' : '') + '><div class="purchase-section-title"><div><span class="purchase-section-index">١</span><div><h2>بيانات الفاتورة</h2><p>بيانات المورد والاستلام والتسوية</p></div></div></div><div class="card-body form-grid"><div class="field purchase-supplier-field"><label for="purchaseSupplierSearch">المورد <span aria-hidden="true">*</span></label><div class="purchase-search-control"><input class="input" id="purchaseSupplierSearch" type="search" autocomplete="off" placeholder="ابحث بالاسم أو الهاتف" value="' + esc(state.supplierQuery || "") + '" aria-controls="purchaseSupplierResults" aria-autocomplete="list"></div><div id="purchaseSupplierResults" class="purchase-supplier-results" role="listbox" aria-label="نتائج بحث الموردين" aria-live="polite"' + ((!state.supplierLoading && !state.supplierResults.length) || state.selectedSupplier ? ' hidden' : '') + '>' + supplierResultsHtml() + '</div><select class="select" id="purchaseSupplier" required hidden aria-hidden="true" tabindex="-1"><option value="">اختر المورد</option>' + (state.selectedSupplier ? options([state.selectedSupplier], state.supplierId, function (x) { return x.name + " — مستحق " + U.money(x.payable || 0); }) : "") + '</select><div data-purchase-supplier-summary>' + supplierSelected + '</div></div>' +
        '<div class="field"><label for="purchaseWarehouse">المخزن</label><select class="select" id="purchaseWarehouse" required>' + options(state.warehouses, state.warehouseId, function (x) { return x.name; }) +
          (state.invoice && !state.warehouses.some(function (x) { return Number(x.id) === Number(state.warehouseId); })
            ? '<option selected value="' + esc(state.warehouseId) + '">' + esc(state.invoice.warehouseName || state.warehouseId) + ' — غير متاح حاليًا</option>' : '') + '</select></div>' +
        '<div class="field"><label for="purchaseNumber">رقم فاتورة المورد</label><input class="input" id="purchaseNumber" maxlength="80" value="' + esc(state.invoiceNumber) + '"></div>' +
        '<div class="field"><label for="purchaseDate">تاريخ الفاتورة</label><input class="input" id="purchaseDate" type="date" required value="' + esc(state.invoiceDate == null ? new Date().toISOString().slice(0,10) : state.invoiceDate) + '"></div>' +
        '<div class="field"><label for="purchaseMode">طريقة الشراء</label><select class="select" id="purchaseMode"><option value="credit"' + ((state.paymentMode || state.invoice && state.invoice.paymentMode) === "credit" ? " selected" : "") + '>آجل</option><option value="cash"' + ((state.paymentMode || state.invoice && state.invoice.paymentMode) === "cash" ? " selected" : "") + '>نقدي — دفع من خزنة أو بنك</option></select></div>' +
        '<div class="field" id="purchaseTreasuryField"'+(state.paymentMode!=="cash"?' hidden':'')+'><label for="purchaseTreasury">الخزنة أو البنك للدفع</label><select class="select" id="purchaseTreasury"'+(state.paymentMode==="cash"?' required':'')+'><option value="">اختر الخزنة أو البنك</option>'+options(state.treasuryAccounts,state.treasuryAccountId,function(x){return x.name+" — "+(x.type==="bank"?"بنك":"خزنة")+" — الرصيد "+x.balance+" ج.م";})+'</select><small class="muted">'+(state.treasuryAccounts.length?'يُخصم إجمالي الفاتورة عند النشر فقط.':'لا توجد خزن أو بنوك متاحة. أضف حسابًا وأودع رصيده أولًا.')+'</small></div>' +
        '<div class="field purchase-notes-field"><label for="purchaseNotes">ملاحظات</label><input class="input" id="purchaseNotes" value="' + esc(state.notes) + '"></div></div></section>' +
        '<section class="card purchase-section purchase-items-card"' + (state.busy ? ' inert' : '') + '><div class="purchase-section-title"><div><span class="purchase-section-index">٢</span><div><h2>أصناف الفاتورة</h2><p>ابحث ثم عدّل الوحدة والكمية والأسعار مباشرة</p></div></div><span class="purchase-line-count">' + state.lines.length + ' بند</span></div><div class="card-body"><div class="purchase-search purchase-product-picker"><label for="purchaseSearch">إضافة صنف</label><div class="purchase-search-control"><input class="input" id="purchaseSearch" type="search" autocomplete="off" placeholder="اسم الصنف أو الكود أو الباركود" value="' + esc(state.productQuery) + '" role="combobox" aria-autocomplete="list" aria-expanded="' + (state.productSearchOpen ? 'true' : 'false') + '" aria-controls="purchaseResults"><span class="purchase-search-hint"><kbd dir="ltr">Enter</kbd> للاختيار · <kbd dir="ltr">Esc</kbd> للإغلاق</span></div><div id="purchaseResults" class="purchase-results" aria-live="polite"' + (state.productSearchOpen ? '' : ' hidden') + '>' + (state.productSearchOpen ? productResultsHtml() : '') + '</div></div><div class="table-wrap purchase-lines-wrap"><table class="data-table purchase-lines-table"><colgroup><col class="purchase-col-product"><col class="purchase-col-unit"><col class="purchase-col-quantity"><col class="purchase-col-price"><col class="purchase-col-price"><col class="purchase-col-total"><col class="purchase-col-action"></colgroup><thead><tr><th>الصنف والمتغير</th><th>الوحدة</th><th>الكمية</th><th>سعر الشراء</th><th>سعر البيع</th><th>الإجمالي</th><th><span class="sr-only">إجراء</span></th></tr></thead><tbody id="purchaseLines">' + lineRows + '</tbody></table></div></div></section>' +
        '<section class="card purchase-section purchase-summary-card"><div class="purchase-summary"><div><span class="purchase-summary-label">إجمالي الفاتورة</span><strong class="purchase-total"><span class="sr-only">إجمالي البنود</span><span data-purchase-total>' + esc(total) + '</span></strong><small>مجموع الكمية × سعر الشراء — دون ضرائب أو خصومات</small></div><div class="purchase-actions"><span class="muted" data-purchase-state role="status">' + esc(purchaseStatus()) + '</span>' + (state.lastPosted && (!context.canNavigate || context.canNavigate("purchase-details")) ? '<button class="table-action" type="button" data-purchase-last-posted>عرض الفاتورة المنشورة <bdi>' + esc(state.lastPosted.invoiceNumber) + '</bdi></button>' : '') + '<div><button class="button" type="button" data-purchase-save ' + (state.busy || state.addingProduct || state.invoice && state.invoice.status === "posted" ? "disabled" : "") + '>حفظ المسودة</button>' + (state.invoice ? '<button class="button primary" type="button" data-purchase-post ' + (state.busy || state.addingProduct || state.dirty || state.invoice.status === "posted" ? "disabled" : "") + '>نشر الفاتورة واستلام البضاعة</button>' : "") + '</div></div></div></section></article>';
      if(state.invoice&&state.invoice.status==="posted"&&global.AlKhayalPrinting)global.AlKhayalPrinting.mount(container.querySelector(".purchase-actions"),"purchase",state.invoice.id);
      container.querySelectorAll("[data-line-field]").forEach(function (input) {
        input.onchange = lineChanged;
        if (input.getAttribute("inputmode") === "decimal") input.oninput = lineChanged;
      });
      container.onclick = click;
      container.oninput = function (event) { syncHeader(event.target); };
      container.onchange = function (event) { syncHeader(event.target); };
      var supplierSearch=container.querySelector("#purchaseSupplierSearch");
      supplierSearch.oninput=function(){scheduleSupplierSearch(this.value);};
      supplierSearch.onkeydown = function (event) {
        if (event.key === "ArrowDown" || event.key === "Enter") {
          var first = container.querySelector("[data-select-supplier]");
          if (!first || container.querySelector("#purchaseSupplierResults").hidden) return;
          event.preventDefault();
          if (event.key === "Enter") first.click();
          else first.focus();
        }
      };
      var search = container.querySelector("#purchaseSearch");
      var delayedSearch=U.debounce(function(query,pageNumber,token){if(token===productSearchToken && state.productSearchOpen)searchProducts(query,pageNumber);},180);
      search.oninput=function(){++productSearchToken;state.productQuery=search.value.trim();state.productPage=1;state.productActiveIndex=-1;state.products=[];state.productSearchMessage="";if(!state.productQuery){state.productSearchOpen=false;renderProductResults();return;}state.productSearchOpen=true;state.productSearchMessage='<span class="muted">جارٍ البحث عن المنتجات...</span>';renderProductResults();delayedSearch(state.productQuery,1,productSearchToken);};
      search.onkeydown=productSearchKeydown;
    }
    function lineHtml(line, index) {
      var track = line.tracking || {};
      var trackingHtml = (track.expiry ? '<label><span>تاريخ الصلاحية <strong aria-hidden="true">*</strong></span><input class="input" type="date" data-line-field="expirationDate" value="' + esc(line.expirationDate || "") + '" required aria-required="true"></label>' : "") + (track.serial ? '<label class="purchase-track-serial"><span>الأرقام التسلسلية' + (track.imei ? ' — سيريال | IMEI مفصولًا بفاصلة' : '') + '</span><textarea class="input" data-line-field="identifiers" rows="2" placeholder="رقم لكل جهاز">' + esc(line.identifierText || "") + '</textarea></label>' : "");
      var main = '<tr class="purchase-line-main' + (trackingHtml ? ' purchase-line-has-tracking' : '') + '" data-line="' + index + '"><td class="purchase-product-cell"><strong title="' + esc(line.productName) + '">' + esc(line.productName) + '</strong><select class="select" data-line-field="variant" aria-label="متغير ' + esc(line.productName) + '">' + options(line.variants, line.variantId, function (v) { return v.name; }) + '</select></td><td><select class="select" data-line-field="unit" aria-label="وحدة ' + esc(line.productName) + '">' + options(line.units, line.productUnitId, function (u) { return u.name + (u.isBase ? " (أساسية)" : ""); }) + '</select></td><td><input class="input" data-line-field="quantity" inputmode="decimal" value="' + esc(line.quantity) + '" aria-label="كمية ' + esc(line.productName) + '"></td><td><input class="input" data-line-field="purchaseUnitPrice" inputmode="decimal" value="' + esc(line.purchaseUnitPrice) + '" aria-label="سعر شراء ' + esc(line.productName) + '"></td><td><input class="input" data-line-field="saleUnitPrice" inputmode="decimal" value="' + esc(line.saleUnitPrice) + '" aria-label="سعر بيع ' + esc(line.productName) + '"></td><td class="purchase-line-total">' + esc(line.lineTotal || "—") + '</td><td class="purchase-line-action"><button class="button danger" type="button" data-remove-line="' + index + '">حذف</button></td></tr>';
      return main + (trackingHtml ? '<tr class="purchase-line-tracking" data-line="' + index + '"><td colspan="7"><div class="purchase-tracking">' + trackingHtml + '<p class="field-error" data-line-error role="alert"' + (line.validationError ? '' : ' hidden') + '>' + esc(line.validationError || '') + '</p></div></td></tr>' : '');
    }
    function lineChanged(event) {
      var row = event.target.closest("[data-line]"), index = Number(row.getAttribute("data-line")), line = state.lines[index], key = event.target.getAttribute("data-line-field");
      if (key === "variant") line.variantId = Number(event.target.value);
      if (key === "unit") { line.productUnitId = Number(event.target.value); var chosen=line.units.find(function(u){return u.id===line.productUnitId;}); if(chosen){line.purchaseUnitPrice=chosen.purchasePrice!=null?chosen.purchasePrice:line.basePurchasePrice;line.saleUnitPrice=chosen.salePrice!=null?chosen.salePrice:line.baseSalePrice;} line.lineTotal = ""; markDirty(); page(); container.querySelector('[data-line="' + index + '"] [data-line-field="unit"]').focus(); return; }
      if (key === "quantity" || key === "purchaseUnitPrice" || key === "saleUnitPrice") line[key] = event.target.value;
      if (key === "expirationDate") line[key] = event.target.value;
      line.validationError = "";
      markDirty();
      var lineError = row.querySelector("[data-line-error]"); if (lineError) { lineError.hidden = true; lineError.textContent = ""; }
      if (key === "identifiers") { line.identifierText = event.target.value; line.physicalUnits = parsePhysicalUnits(line.identifierText, line.tracking && line.tracking.imei); }
      if (key === "quantity" || key === "purchaseUnitPrice" || key === "variant") {
        line.lineTotal = "";
        var totalCell = row.querySelector(".purchase-line-total");
        if (totalCell) totalCell.textContent = "—";
      }
      var total = container.querySelector("[data-purchase-total]");
      try { total.textContent = moneyScaled(totalFor(state.lines).toString()); } catch (ignore) { total.textContent = "—"; }
    }
    function syncHeader(input) {
      if (!input || !/^purchase(?:Supplier|Warehouse|Number|Date|Mode|Treasury|Notes)$/.test(input.id)) return;
      markDirty();
      if (input.id === "purchaseSupplier") state.supplierId = Number(input.value);
      if (input.id === "purchaseWarehouse") state.warehouseId = Number(input.value);
      if (input.id === "purchaseNumber") state.invoiceNumber = input.value;
      if (input.id === "purchaseDate") state.invoiceDate = input.value;
      if (input.id === "purchaseMode") {
        var previousMode = state.paymentMode;
        state.paymentMode = input.value;
        var treasury = container.querySelector("#purchaseTreasury");
        container.querySelector("#purchaseTreasuryField").hidden = input.value !== "cash";
        treasury.required = input.value === "cash";
        if(input.value !== "cash"){state.treasuryAccountId=null;treasury.value="";}
        if(previousMode!==input.value)setTreasurySnapshotTracking(input.value==="cash").catch(function(error){if(context.isActive&&!context.isActive())return;UI.toast("تعذر تحديث قائمة الخزن للحساب النقدي: "+(error.message||""),"warning");});
      }
      if (input.id === "purchaseTreasury") state.treasuryAccountId = Number(input.value) || null;
      if (input.id === "purchaseNotes") state.notes = input.value;
    }
    function setTreasurySnapshotTracking(enabled) {
      if(!enabled){AlKhayalApi.untrackPageSnapshot(screen.route,"/api/treasury/accounts");return Promise.resolve();}
      return AlKhayalApi.request("/api/treasury/accounts").then(function(accounts){if(context.isActive&&!context.isActive())return;state.treasuryAccounts=accounts||[];var select=container.querySelector("#purchaseTreasury");if(select){select.innerHTML='<option value="">اختر الخزنة أو البنك للدفع</option>'+options(state.treasuryAccounts,state.treasuryAccountId,function(x){return x.name+" — "+(x.type==="bank"?"بنك":"خزنة")+" — الرصيد "+x.balance+" ج.م";});select.value=state.treasuryAccountId?String(state.treasuryAccountId):"";}});
    }
    var productSearchToken=0;
    async function searchProducts(query, pageNumber) {
      var token=++productSearchToken;
      query=String(query||"").trim();
      if (!query) { state.products=[];state.productTotal=0;state.productActiveIndex=-1;state.productSearchMessage="";state.productSearchOpen=false;renderProductResults();return; }
      state.productPage=pageNumber||1;
      var requestedPage=state.productPage;
      state.productSearchOpen=true;state.productSearchMessage='<span class="muted">جارٍ البحث عن المنتجات...</span>';renderProductResults();
      try {
        var result = await AlKhayalApi.request("/api/inventory/products?search=" + encodeURIComponent(query) + "&page="+requestedPage+"&pageSize=20");
        var target = container.querySelector("#purchaseResults");
        if (token!==productSearchToken||!target||!container.contains(target)||!currentView(context)) return;
        state.products = (result.items || []).filter(function (x) { return x.productType === "stock" && x.isActive && !x.isArchived; });
        state.productTotal=Number(result.totalCount||0);state.productActiveIndex=state.products.length?0:-1;state.productSearchMessage="";renderProductResults();
      } catch (error) { var currentTarget=container.querySelector("#purchaseResults");if(token===productSearchToken&&currentTarget&&container.contains(currentTarget)&&currentView(context)){state.productSearchOpen=true;state.productSearchMessage='<span class="field-error">'+esc(error.message||"تعذر البحث عن المنتجات.")+'</span><button class="button" type="button" data-purchase-products-retry>إعادة المحاولة</button>';renderProductResults();} }
    }
    function closeProductSearch() {
      ++productSearchToken;
      state.productSearchOpen=false;state.productActiveIndex=-1;renderProductResults();
      var search=container.querySelector("#purchaseSearch");if(search)search.setAttribute("aria-expanded","false");
    }
    function addProductFromSearch(id) {
      if (state.busy || state.addingProduct) return;
      state.addingProduct = true;
      closeProductSearch();
      var search = container.querySelector("#purchaseSearch");
      search.disabled = true;
      container.querySelector("[data-purchase-save]").disabled = true;
      refreshPurchaseStatus();
      state.productSearchOpen = true;
      state.productSearchMessage = '<span class="muted">جارٍ تحميل بيانات الصنف…</span>';
      renderProductResults();
      return addProduct(id).then(function (added) {
        if (!currentView(context)) return;
        state.addingProduct = false;
        state.productQuery = added ? "" : state.productQuery;
        state.productPage = 1;
        state.productTotal = 0;
        state.products = [];
        state.productSearchOpen = false;
        state.productSearchMessage = "";
        page();
        if (!context.isActive || context.isActive()) container.querySelector("#purchaseSearch").focus();
      });
    }
    function productSearchKeydown(event) {
      if (event.key === "Escape") { if(state.productSearchOpen){event.preventDefault();closeProductSearch();} return; }
      if (!state.productSearchOpen || !state.products.length) return;
      if (event.key === "ArrowDown" || event.key === "ArrowUp") {
        event.preventDefault();
        var direction=event.key === "ArrowDown"?1:-1;
        state.productActiveIndex=(state.productActiveIndex+direction+state.products.length)%state.products.length;
        renderProductResults();return;
      }
      if (event.key === "Enter" && state.productActiveIndex >= 0) {
        event.preventDefault();
        addProductFromSearch(state.products[state.productActiveIndex].id);
      }
    }
    async function addProduct(id) {
      try {
        var product = await AlKhayalApi.request("/api/inventory/products/" + encodeURIComponent(id));
        if (!currentView(context)) return false;
        var variant = (product.variants || []).filter(function (x) { return x.isActive && !x.isArchived; })[0], unit = (product.units || []).filter(function (x) { return x.isActive && x.isPurchaseUnit; })[0] || (product.units || []).filter(function (x) { return x.isActive && x.isBase; })[0];
        if (!variant || !unit) throw new Error("لا يوجد متغير أو وحدة شراء نشطة لهذا الصنف.");
        var enabled = function (key) { return (product.capabilities || []).some(function (x) { return x.key === key && (x.isAssigned || x.state === "enabled-and-applicable" || x.state === "assigned-but-disabled"); }); };
        state.lines.push({ productName: product.name, variants: product.variants.filter(function (x) { return x.isActive && !x.isArchived; }), units: product.units.filter(function (x) { return x.id === unit.id || (x.isActive && x.isPurchaseUnit); }), variantId: variant.id, productUnitId: unit.id, quantity: "1", basePurchasePrice: variant.effectivePurchasePrice || product.purchasePrice || "0", baseSalePrice: variant.effectiveSalePrice || product.salePrice || "0", purchaseUnitPrice: unit.purchasePrice != null ? unit.purchasePrice : (variant.effectivePurchasePrice || product.purchasePrice || "0"), saleUnitPrice: unit.salePrice != null ? unit.salePrice : (variant.effectiveSalePrice || product.salePrice || "0"), lotNumber: "", expirationDate: "", tracking: { batch: enabled("batch-tracking"), expiry: enabled("expiry-dates"), serial: enabled("serial-numbers"), imei: enabled("imei-tracking") }, identifierText: "", physicalUnits: [] });
        markDirty();
        return true;
      } catch (error) { UI.toast(error.message, "danger"); return false; }
    }
    function formData() {
      return { supplierId: Number(container.querySelector("#purchaseSupplier").value), warehouseId: Number(container.querySelector("#purchaseWarehouse").value), invoiceNumber: container.querySelector("#purchaseNumber").value, invoiceDate: container.querySelector("#purchaseDate").value, paymentMode: container.querySelector("#purchaseMode").value, treasuryAccountId: state.paymentMode==="cash" ? (Number(container.querySelector("#purchaseTreasury").value)||null) : null, notes: container.querySelector("#purchaseNotes").value,
        lines: state.lines.map(function (x) { return { variantId: x.variantId, productUnitId: x.productUnitId, quantity: x.quantity, purchaseUnitPrice: x.purchaseUnitPrice, saleUnitPrice: x.saleUnitPrice, lotNumber: x.lotNumber || null, expirationDate: x.expirationDate || null, physicalUnits: x.tracking && x.tracking.serial ? parsePhysicalUnits(x.identifierText || "", x.tracking.imei) : [] }; }) };
    }
    function validateTracking() {
      var first = null;
      state.lines.forEach(function (line, index) {
        line.validationError = "";
        if (line.tracking && line.tracking.expiry && !/^\d{4}-\d{2}-\d{2}$/.test(String(line.expirationDate || ""))) line.validationError = "أدخل تاريخ صلاحية صحيحًا لهذا الصنف.";
        if (line.validationError && first === null) first = index;
      });
      if (first !== null) { page(); var input = container.querySelector('[data-line="' + first + '"] [data-line-field="expirationDate"]'); if (input) input.focus(); return false; }
      return true;
    }
    async function saveDraft() {
      if(state.busy || state.addingProduct)return;
      if(state.paymentMode==="cash"&&!state.treasuryAccountId){UI.toast("اختر الخزنة أو البنك للدفع النقدي.","danger");container.querySelector("#purchaseTreasury").focus();return;}
      if(!validateTracking()){UI.toast("أكمل بيانات التتبع المطلوبة داخل بند الفاتورة.","danger");return;}
      state.busy = true; state.feedback = "جارٍ حفظ المسودة…"; page();
      try {
        var body = JSON.stringify(formData()), saved = await AlKhayalApi.request(state.invoice ? "/api/purchases/" + state.invoice.id : "/api/purchases/drafts", { method: state.invoice ? "PUT" : "POST", body: body });
        if (!saved || !saved.id || saved.status !== "draft") throw new Error("تعذر تأكيد حفظ المسودة. راجع سجل المشتريات.");
        state.lines.forEach(function (line, index) {
          line.lineTotal = displayLineTotal((saved.lines || [])[index]);
        });
        if (context.state) context.state.invoiceId = saved.id;
        state.invoiceNumber = saved.invoiceNumber || state.invoiceNumber; state.invoice = saved; state.dirty = false; state.feedback = ""; state.busy = false; page(); UI.toast("تم حفظ مسودة فاتورة الشراء.", "success");
      } catch (error) { state.busy = false; state.feedback = error.message || "تعذر حفظ المسودة."; page(); UI.toast(state.feedback, "danger"); }
    }
    async function postInvoice() {
      if (!state.invoice || state.busy || state.addingProduct) return;
      if(!validateTracking())return;
      if(state.dirty){UI.toast("احفظ تعديلات بنود الفاتورة قبل النشر.","danger");return;}
      if(state.invoice.paymentMode!==state.paymentMode||Number(state.invoice.treasuryAccountId||0)!==Number(state.treasuryAccountId||0)){UI.toast("احفظ المسودة بعد تغيير طريقة الدفع أو الخزنة قبل النشر.","danger");return;}
      state.busy = true; state.feedback = "جارٍ نشر الفاتورة واستلام البضاعة…"; page();
      var posted;
      try {
        posted = await AlKhayalApi.request("/api/purchases/" + state.invoice.id + "/post", { method: "POST", body: JSON.stringify({ operationKey: makeKey(state.invoice.id) }) });
        if (!posted || !posted.invoice || Number(posted.invoice.id) !== Number(state.invoice.id) || posted.invoice.status !== "posted") throw new Error("استجابة النشر غير مكتملة.");
      } catch (error) {
        state.busy = false;
        state.feedback = (!error.status || error.status >= 500 || error.code === "invalid_response")
          ? "تعذر تأكيد نتيجة النشر. راجع سجل المشتريات أو أعد نشر المسودة نفسها."
          : (error.message || "تعذر النشر.") + " — راجع الرسالة قبل إعادة المحاولة.";
        page();
        UI.toast(state.feedback, "danger");
        return;
      }
      state.lastPosted = posted.invoice;
      state.feedback = "تم نشر الفاتورة واستلام البضاعة.";
      ++productSearchToken;
      ++state.supplierSearchToken;
      if (state.supplierSearchTimer) clearTimeout(state.supplierSearchTimer);
      if (context && context.state) delete context.state.invoiceId;
      state.invoice = null; state.dirty = false; state.supplierId = null; state.selectedSupplier = null; state.suppliers = []; state.supplierResults = []; state.supplierQuery = ""; state.lines = []; state.products = []; state.productQuery = ""; state.productPage = 1; state.productTotal = 0; state.productSearchOpen = false; state.productActiveIndex = -1; state.productSearchMessage = ""; state.invoiceNumber = ""; state.invoiceDate = new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10); state.paymentMode = "credit"; state.treasuryAccountId = null; state.notes = "";
      AlKhayalApi.resetPageSnapshot(screen.route);
      try { var fresh = await Promise.all([AlKhayalApi.request("/api/inventory/warehouses"), AlKhayalApi.request("/api/treasury/accounts")]); state.warehouses = (fresh[0] || []).filter(function (x) { return x.isActive; }); state.treasuryAccounts = fresh[1] || [];AlKhayalApi.untrackPageSnapshot(screen.route,"/api/treasury/accounts"); if (!state.warehouses.some(function (x) { return Number(x.id) === Number(state.warehouseId); })) state.warehouseId = (state.warehouses.filter(function (x) { return x.isDefault; })[0] || state.warehouses[0] || {}).id || null; }
      catch (refreshError) { UI.toast("تم ترحيل الفاتورة، لكن تعذر تحديث بيانات المخازن والخزنة.", "warning"); }
      state.busy = false;
      page(); UI.toast("تم نشر الفاتورة " + (posted.invoice.invoiceNumber || "") + " واستلام البضاعة. الشاشة جاهزة لفاتورة جديدة.", "success");
    }
    async function loadInvoice(id) {
      var requestedId=Number(id),token=++state.loadToken;
      if (!requestedId || requestedId < 1) { container.innerHTML=UI.pageHeader(screen)+'<div class="empty-state"><strong>تعذر استكمال المسودة</strong><span>معرّف مسودة الشراء غير صالح.</span><button class="button" type="button" data-new-purchase-from-error>فاتورة جديدة</button></div>';container.onclick=function(e){if(e.target.closest("[data-new-purchase-from-error]"))context.navigate("purchase-management",{invoiceId:null});};return; }
      try {
        var invoice = await AlKhayalApi.request("/api/purchases/" + requestedId);
        if(token!==state.loadToken || !currentView(context))return;
        if(!invoice||Number(invoice.id)!==requestedId)throw new Error("تعذر التحقق من مسودة الشراء المطلوبة.");
        if(invoice.status!=="draft")throw new Error("لا يمكن استكمال هذه الفاتورة لأنها لم تعد مسودة قابلة للتعديل.");
        var detail=await Promise.all([AlKhayalApi.request("/api/suppliers/" + invoice.supplierId),Promise.all((invoice.lines || []).map(async function (line) {
          var product = await AlKhayalApi.request("/api/inventory/products/" + line.productId);
          line.productName = product.name; line.variants = product.variants.filter(function (x) { return Number(x.id)===Number(line.variantId)||(x.isActive && !x.isArchived); }); line.units = product.units.filter(function (x) { return Number(x.id)===Number(line.productUnitId)||x.isActive; });
          var enabled = function (key) { return (product.capabilities || []).some(function (x) { return x.key === key && (x.isAssigned || x.state === "enabled-and-applicable" || x.state === "assigned-but-disabled"); }); };
          var savedUnits=line.physicalUnits||[],savedImei=savedUnits.some(function(u){return u.imeis&&u.imeis.length;});
          line.tracking = { batch: enabled("batch-tracking")||!!line.lotNumber, expiry: enabled("expiry-dates")||!!line.expirationDate, serial: enabled("serial-numbers")||savedUnits.length>0, imei: enabled("imei-tracking")||savedImei };
          line.identifierText = (line.physicalUnits || []).map(function (u) { return u.serial + (line.tracking.imei && u.imeis && u.imeis.length ? "|" + u.imeis.join(",") : ""); }).join("\n");
          line.lineTotal = displayLineTotal(line); return line;
        }))]);
        if(token!==state.loadToken || !currentView(context))return;
        state.invoice = invoice; state.treasuryAccountId = invoice.treasuryAccountId || null; state.supplierId = invoice.supplierId; state.warehouseId = invoice.warehouseId; state.paymentMode = invoice.paymentMode; state.invoiceDate = invoice.invoiceDate; state.invoiceNumber = invoice.invoiceNumber || ""; state.notes = invoice.notes || "";
        state.selectedSupplier = detail[0]; state.suppliers = [state.selectedSupplier]; state.supplierResults = [state.selectedSupplier]; state.supplierQuery = state.selectedSupplier.name; state.lines=detail[1];
        state.dirty = false;
        page();
        setTreasurySnapshotTracking(state.paymentMode==="cash").catch(function(error){if(context.isActive&&!context.isActive())return;UI.toast("تعذر تحديث أرصدة الخزن: "+(error.message||""),"warning");});
      }
      catch (error) { if(token!==state.loadToken || !currentView(context))return;state.invoice=null;state.lines=[];container.innerHTML=UI.pageHeader(screen)+'<div class="empty-state"><strong>تعذر استكمال المسودة</strong><span>'+esc(error.message||"المسودة غير موجودة أو لم تعد قابلة للتعديل.")+'</span><button class="button" type="button" data-new-purchase-from-error>فاتورة جديدة</button></div>';container.onclick=function(e){if(e.target.closest("[data-new-purchase-from-error]"))context.navigate("purchase-management",{invoiceId:null});}; }
    }
    function click(event) {
      if (state.busy) return;
      if (event.target.closest("[data-purchase-last-posted]") && state.lastPosted) { context.navigate("purchase-details", { invoiceId: state.lastPosted.id }); return; }
      var add = event.target.closest("[data-add-product]"), remove = event.target.closest("[data-remove-line]"), save = event.target.closest("[data-purchase-save]"), post = event.target.closest("[data-purchase-post]");
      var supplierChoice=event.target.closest("[data-select-supplier]");if(supplierChoice){var supplierId=Number(supplierChoice.getAttribute("data-select-supplier")),supplier=state.supplierResults.find(function(item){return Number(item.id)===supplierId;});state.supplierId=supplierId;state.selectedSupplier=supplier||null;state.supplierQuery=supplier?supplier.name:"";state.supplierLoading=false;markDirty();if(state.supplierSearchTimer){clearTimeout(state.supplierSearchTimer);state.supplierSearchTimer=null;}++state.supplierSearchToken;page();container.querySelector("#purchaseSupplierSearch").focus();return;}
      var changeSupplier=event.target.closest("[data-change-supplier]");if(changeSupplier){var supplierSearch=container.querySelector("#purchaseSupplierSearch");if(supplierSearch){supplierSearch.focus();supplierSearch.select();}return;}
      if(event.target.closest("[data-more-suppliers]")){if(state.supplierLoading)return;searchSuppliers(state.supplierQuery,state.supplierSearchPage+1,true);return;}
      if(event.target.closest("[data-retry-suppliers]")){searchSuppliers(state.supplierQuery,state.supplierSearchPage,state.supplierResults.length>0);return;}
      if(event.target.closest("[data-focus-product]")){var productSearch=container.querySelector("#purchaseSearch");if(productSearch)productSearch.focus();return;}
      if (event.target.closest("[data-purchase-products-retry]")) { searchProducts(state.productQuery,state.productPage);return; }
      var productPage=event.target.closest("[data-purchase-products-page]");
      if(productPage&&!productPage.disabled){searchProducts(state.productQuery,Number(productPage.getAttribute("data-purchase-products-page")));return;}
      if (add) { addProductFromSearch(Number(add.getAttribute("data-add-product")));return; }
      if (remove) { var index = Number(remove.getAttribute("data-remove-line")); state.lines.splice(index, 1); markDirty(); page(); var next = container.querySelector('[data-remove-line="' + Math.min(index, state.lines.length - 1) + '"]'); (next || container.querySelector("#purchaseSearch")).focus(); }
      if (save) saveDraft(); if (post) postInvoice();
    }
    async function init() {
      try {
        container.innerHTML = UI.pageHeader(screen) + '<div class="card empty-state" role="status">جارٍ تحميل بيانات الشراء...</div>';
        var data = await Promise.all([AlKhayalApi.request("/api/inventory/warehouses"), AlKhayalApi.request("/api/treasury/accounts")]);
        if (!currentView(context)) return;
        state.treasuryAccounts = data[1] || []; state.warehouses = (data[0] || []).filter(function (x) { return x.isActive; });AlKhayalApi.untrackPageSnapshot(screen.route,"/api/treasury/accounts");
        state.warehouseId = (state.warehouses[0] || {}).id || null;
        if (context && context.state && context.state.invoiceId != null) await loadInvoice(context.state.invoiceId);
        else page();
      } catch (error) { if (!currentView(context)) return; container.innerHTML = UI.pageHeader(screen) + '<div class="empty-state"><strong>تعذر تحميل بيانات الشراء</strong><span>' + esc(error.message) + '</span><button class="button" data-purchase-retry>إعادة المحاولة</button></div>'; container.onclick = function (e) { if (e.target.closest("[data-purchase-retry]")) init(); }; }
    }
    init();
  }
  function renderPurchaseList(screen, container, context) {
    container.innerHTML = UI.pageHeader(screen,'<button class="button" type="button" data-purchases-export>تصدير</button>') + '<section class="card purchase-list-card"><div class="card-head"><div><h2>فواتير الشراء</h2><small class="muted">المسودات والفواتير المنشورة</small></div>' + (editorAvailable(context) ? '<button class="button primary" data-new-purchase>فاتورة جديدة</button>' : '') + '</div><div class="card-body" data-purchase-list>جارٍ التحميل…</div></section>';
    var exportButton=container.querySelector("[data-purchases-export]");if(exportButton)exportButton.onclick=function(){global.AlKhayalExports.choose({dataset:"purchases",arabicName:"فواتير الشراء",fileName:"فواتير-الشراء",filters:{}},this);};
    var body = container.querySelector("[data-purchase-list]");
    var loading = false, page = 1;
    async function load(requestedPage) {
      if (loading) return;
      loading = true;
      body.innerHTML = '<div role="status">جارٍ التحميل…</div>';
      try {
        var result = await AlKhayalApi.request("/api/purchases/page?page=" + requestedPage);
        if (!currentView(context) || !body.isConnected) return;
        var rows = result.items, last = Math.max(1, Math.ceil(result.totalCount / 50));
        page = result.page;
      body.innerHTML = rows.length ? '<div class="table-wrap purchase-list-wrap"><table class="data-table purchase-list-table"><thead><tr><th>التاريخ</th><th>رقم الفاتورة</th><th>المورد</th><th>المخزن</th><th>الإجمالي</th><th>الحالة</th><th>الإجراءات</th></tr></thead><tbody>' + rows.map(function (x) { return '<tr><td class="purchase-list-date">' + esc(x.invoiceDate) + '</td><td class="purchase-list-reference"><button class="table-action" data-view-purchase="' + esc(x.id) + '">' + '<bdi>' + esc(x.invoiceNumber) + '</bdi></button></td><td class="purchase-list-supplier"><strong>' + esc(x.supplierName) + '</strong></td><td>' + esc(x.warehouseName) + '</td><td class="numeric purchase-list-amount">' + esc(x.totalFormatted || moneyScaled(x.totalScaled)) + '</td><td class="purchase-list-status"><span class="badge ' + (x.status === "posted" ? "success" : "warning") + '">' + (x.status === "posted" ? "منشورة" : "مسودة") + '</span> ' + returnBadge(x) + '</td><td class="purchase-list-actions"><button class="button" data-view-purchase="' + esc(x.id) + '">عرض الفاتورة</button>' + (x.status === "posted" && x.returnStatus !== "full" && (!context.canNavigate || context.canNavigate("returns-overview")) ? '<button class="button return-action" data-create-purchase-return="' + esc(x.id) + '">إنشاء مرتجع</button>' : '') + (x.status === "draft" && editorAvailable(context) ? '<button class="button" data-edit-purchase="' + esc(x.id) + '">تعديل المسودة</button>' : "") + '</td></tr>'; }).join("") + '</tbody></table></div>' : '<div class="empty-state"><strong>لا توجد فواتير شراء بعد</strong><span>ستظهر الفواتير المحفوظة هنا.</span></div>';
        if (result.totalCount > 0) {
          body.innerHTML += '<nav class="pagination invoice-pagination" aria-label="صفحات فواتير الشراء"><span role="status">صفحة ' + page + ' من ' + last + ' · ' + result.totalCount + ' فاتورة</span><div class="pagination-buttons"><button type="button" class="button" data-purchase-page="' + (page - 1) + '"' + (page === 1 ? ' disabled' : '') + '>السابق</button><button type="button" class="button" data-purchase-page="' + (page + 1) + '"' + (page === last ? ' disabled' : '') + '>التالي</button></div></nav>';
        }
      } catch (error) {
        if (!currentView(context) || !body.isConnected) return;
        body.innerHTML = '<div class="empty-state"><strong>تعذر تحميل سجل المشتريات</strong><span>' + esc(error.message) + '</span><button class="button" data-reload-purchases>إعادة المحاولة</button></div>';
      } finally { loading = false; }
    }
    load(page);
    container.onclick = function (event) { if (event.target.closest("[data-new-purchase]")) context.navigate("purchase-management",{invoiceId:null}); var view = event.target.closest("[data-view-purchase]"); if (view) context.navigate("purchase-details",{invoiceId:Number(view.getAttribute("data-view-purchase"))}); var edit = event.target.closest("[data-edit-purchase]"); if (edit) context.navigate("purchase-management",{invoiceId:Number(edit.getAttribute("data-edit-purchase"))}); var createReturn=event.target.closest("[data-create-purchase-return]");if(createReturn&&(!context.canNavigate||context.canNavigate("returns-overview")))context.navigate("returns-overview",{returnInvoiceType:"purchase",returnInvoiceId:Number(createReturn.getAttribute("data-create-purchase-return")),returnEditor:true}); var targetPage = event.target.closest("[data-purchase-page]"); if (targetPage && !targetPage.disabled) load(Number(targetPage.getAttribute("data-purchase-page"))); if (event.target.closest("[data-reload-purchases]")) load(page); };
  }
  function renderPurchaseDetails(screen, container, context) {
    var id = Number(context.state && context.state.invoiceId);
    function back() { context.navigate("purchases-list", {}); }
    function shell(content) { container.innerHTML = UI.pageHeader(screen, '<button class="button" type="button" data-back-purchases>قائمة المشتريات</button>') + content; var button=container.querySelector('[data-back-purchases]'); if(button)button.onclick=back; }
    if (!Number.isSafeInteger(id) || id < 1) { shell('<div class="empty-state" role="alert"><strong>مرجع فاتورة الشراء غير صالح.</strong></div>'); return; }
    shell('<div class="card empty-state" role="status">جارٍ تحميل فاتورة الشراء…</div>');
    AlKhayalApi.request('/api/purchases/' + id).then(function (x) {
      if (!currentView(context)) return;
      if (!x || Number(x.id) !== id) throw new Error('تعذر التحقق من الفاتورة المطلوبة.');
      var draft=x.status==='draft', rows=(x.lines||[]).map(function(line){var tracking=(line.physicalUnits||[]).map(function(unit){return esc(unit.serial)+(unit.imeis&&unit.imeis.length?' · IMEI '+esc(unit.imeis.join('، ')):'');}).join('<br>');return '<tr><td><strong>'+esc(line.productName)+'</strong>'+(line.variantName?'<small>'+esc(line.variantName)+'</small>':'')+(line.sku?'<small>SKU: '+esc(line.sku)+'</small>':'')+'</td><td>'+esc(line.unitName)+'</td><td class="numeric">'+esc(line.quantity)+'</td><td class="numeric">'+(line.toBaseNumerator&&line.toBaseDenominator&&(+line.toBaseNumerator!==1||+line.toBaseDenominator!==1)?esc(line.toBaseNumerator)+' ÷ '+esc(line.toBaseDenominator):'—')+'</td><td class="numeric">'+esc(line.purchaseUnitPrice)+'</td><td class="numeric">'+esc(line.lineTotalFormatted||moneyScaled(line.lineTotalScaled))+'</td><td>'+esc(line.expirationDate||'—')+'</td><td>'+ (tracking||'—')+'</td></tr>';}).join('');
      var label=function(name,value){return '<div><span>'+name+'</span><strong>'+esc(value||'—')+'</strong></div>';};
      var total=esc(x.totalFormatted||moneyScaled(x.totalScaled));
      shell('<article class="purchase-detail"><section class="card purchase-detail-head"><div class="card-body"><div class="purchase-detail-title"><div><small>فاتورة مشتريات محفوظة</small><h2>'+esc(x.invoiceNumber)+'</h2></div><span class="badge '+(draft?'warning':'success')+'">'+(draft?'مسودة':'منشورة')+'</span></div><div class="purchase-detail-meta">'+label('تاريخ الفاتورة',x.invoiceDate)+label('أنشئت في',x.createdUtc?U.dateTime(x.createdUtc):'')+label('المورد',x.supplierName)+label('مخزن الاستلام',x.warehouseName)+label('المسؤول',x.createdByName)+label('طريقة الشراء',x.paymentMode==='cash'?'نقدي':'آجل')+(x.treasuryAccountName?label('حساب الخزنة / البنك',x.treasuryAccountName):'')+'</div>'+(x.notes?'<p class="purchase-detail-notes"><b>ملاحظات:</b> '+esc(x.notes)+'</p>':'')+'</div></section><section class="card purchase-detail-lines"><div class="card-head"><h2>أصناف الفاتورة</h2><span class="muted">'+(x.lines||[]).length+' بند</span></div><div class="table-wrap"><table class="data-table purchase-detail-table"><thead><tr><th>المنتج</th><th>الوحدة</th><th>الكمية</th><th>معامل التحويل</th><th>سعر الشراء</th><th>الإجمالي</th><th>تاريخ الصلاحية</th><th>التتبع</th></tr></thead><tbody>'+rows+'</tbody></table></div></section><section class="card purchase-detail-total"><div class="card-body"><div class="purchase-detail-total-primary"><span>إجمالي الفاتورة الأصلي</span><strong>'+total+'</strong></div>'+(draft?'':'<div class="purchase-detail-settlement"><span>المدفوع عند النشر <strong>'+(x.paymentMode==='cash'?total:'0.0000')+'</strong></span><span>الآجل الأصلي <strong>'+(x.paymentMode==='credit'?total:'0.0000')+'</strong></span></div>')+'<small>الخصومات والضرائب غير مفصولة في نموذج الشراء. الرصيد المستحق الحالي والمدفوعات اللاحقة يُراجعان في حساب المورد.</small></div></section>'+(draft&&editorAvailable(context)?'<div class="purchase-detail-actions"><button class="button primary" type="button" data-resume-purchase>استكمال وتعديل</button></div>':'')+'</article>');
      if (!draft && global.AlKhayalPrinting) {
        var toolbar = container.querySelector('[data-back-purchases]').parentNode;
        var print = global.AlKhayalPrinting.mount(toolbar, 'purchase', id);
        print.textContent = '🖨 طباعة الفاتورة';
      }
      var edit=container.querySelector('[data-resume-purchase]');if(edit)edit.onclick=function(){context.navigate('purchase-management',{invoiceId:id});};
    }).catch(function(error){if (!currentView(context)) return; shell('<div class="empty-state" role="alert"><strong>تعذر عرض فاتورة الشراء</strong><span>'+esc(error.message||'الفاتورة غير موجودة أو تعذر تحميلها.')+'</span><button class="button" type="button" data-retry-purchase>إعادة المحاولة</button></div>');container.querySelector('[data-retry-purchase]').onclick=function(){renderPurchaseDetails(screen,container,context);};});
  }
  function renderPurchaseReturns(screen, container) {
    var state = { invoices: [], warehouses: [], lines: [], invoiceId: null, warehouseId: null, mode: "supplier_credit", key: null, busy: false, lastResult: null, inputLines: {} };
    function render() {
      var rows = state.lines.map(function (x, i) {
        var saved = state.inputLines[i] || {}, selected = saved.serials || [];
        var serials = (x.physicalUnits || []).map(function (u, j) { return '<label class="return-serial"><input type="checkbox" data-return-unit-line="' + i + '" value="' + j + '"' + (selected.indexOf(u.serial) >= 0 ? " checked" : "") + '> ' + esc(u.serial) + (u.imeis && u.imeis.length ? ' — IMEI ' + esc(u.imeis.join("، ")) : "") + '</label>'; }).join("");
        return '<tr><td>' + esc(x.productName + (x.variantName ? " — " + x.variantName : "")) + (serials ? '<div class="purchase-tracking">اختر الرقم/الأرقام الأصلية المتاحة' + serials + '</div>' : '') + '</td><td>' + esc(x.quantity) + ' ' + esc(x.unitName) + '</td><td>' + esc(x.availableQuantity) + '</td><td><input class="input" data-return-qty="' + i + '" inputmode="decimal" value="' + esc(saved.quantity || "0") + '" ' + (Number(x.availableQuantity) <= 0 ? "disabled" : "") + '></td></tr>';
      }).join("");
      var resultHtml = state.lastResult ? '<div class="notice">تم تسجيل المرتجع. إجمالي تسوية المورد: ' + esc(state.lastResult.totalFormatted || moneyScaled(state.lastResult.totalScaled)) + ' — راجع التسوية وحركات الخزنة من إدارة المرتجعات.</div>' + (state.lastResult.lines || []).map(function (x) { return '<p>' + esc(x.productName) + ': سعر الفاتورة ' + esc(x.accountAmountFormatted || moneyScaled(x.accountAmountScaled)) + '، تكلفة البضاعة المرتجعة الفعلية ' + esc(x.inventoryValueFormatted || moneyScaled(x.inventoryValueScaled)) + '، الفرق المسجل ' + esc(x.varianceFormatted || moneyScaled(x.varianceScaled)) + '</p>'; }).join("") : "";
      container.innerHTML = UI.pageHeader(screen) + '<section class="card"><div class="card-head"><h2>مرتجع شراء</h2></div><div class="card-body form-grid"><div class="field"><label for="returnInvoice">فاتورة الشراء المنشورة</label><select class="select" id="returnInvoice"><option value="">اختر الفاتورة</option>' + options(state.invoices, state.invoiceId, function (x) { return x.invoiceNumber + " — " + x.supplierName + " — " + x.invoiceDate; }) + '</select></div><div class="field"><label for="returnWarehouse">مخزن البضاعة المرتجعة</label><select class="select" id="returnWarehouse"><option value="">اختر المخزن</option>' + options(state.warehouses, state.warehouseId, function (x) { return x.name; }) + '</select></div><div class="field"><label for="returnMode">تسوية المورد</label><select class="select" id="returnMode"><option value="supplier_credit"' + (state.mode === "supplier_credit" ? " selected" : "") + '>رصيد دائن لدى المورد</option><option value="cash_refund"' + (state.mode === "cash_refund" ? " selected" : "") + '>استرداد نقدي في خزنة مرتجعات الشراء</option></select></div></div><div class="card-body" id="returnLines">' + (rows ? '<div class="table-wrap"><table class="data-table"><thead><tr><th>الصنف</th><th>الكمية الأصلية</th><th>المتاح للإرجاع</th><th>الكمية المرتجعة</th></tr></thead><tbody>' + rows + '</tbody></table></div>' : '<p class="muted">اختر الفاتورة والمخزن لعرض الكميات القابلة للإرجاع ذات التكلفة المتتبعة.</p>') + '</div><div class="purchase-actions"><span class="muted">تُحسب تسوية المورد بسعر الشراء الأصلي، وتُسجل تكلفة البضاعة المرتجعة الفعلية والفرق صراحةً.</span><button class="button primary" data-post-return ' + (state.busy || !state.lines.length ? "disabled" : "") + '>تسجيل المرتجع</button></div><div id="returnResult" class="card-body" aria-live="polite">' + resultHtml + '</div></section>';
      container.querySelector("#returnInvoice").onchange = function (e) { state.invoiceId = Number(e.target.value) || null; state.key = null; state.inputLines = {}; loadLines(); };
      container.querySelector("#returnWarehouse").onchange = function (e) { state.warehouseId = Number(e.target.value) || null; state.key = null; state.inputLines = {}; loadLines(); };
      container.querySelector("#returnMode").onchange = function (e) { state.mode = e.target.value; state.key = null; };
      container.querySelector("[data-post-return]").onclick = postReturn;
    }
    async function loadLines() {
      state.lines = [];
      if (state.invoiceId && state.warehouseId) {
        try { state.lines = await AlKhayalApi.request("/api/purchases/" + state.invoiceId + "/returnable-lines?warehouseId=" + state.warehouseId); }
        catch (error) { UI.toast(error.message, "danger"); }
      }
      render();
    }
    async function postReturn() {
      var lines = state.lines.map(function (x, i) { var quantity = container.querySelector('[data-return-qty="' + i + '"]').value, physicalUnits = Array.prototype.map.call(container.querySelectorAll('[data-return-unit-line="' + i + '"]:checked'), function (checkbox) { return x.physicalUnits[Number(checkbox.value)]; }); state.inputLines[i] = { quantity: quantity, serials: physicalUnits.map(function (u) { return u.serial; }) }; return { purchaseInvoiceLineId: x.purchaseInvoiceLineId, quantity: quantity, physicalUnits: physicalUnits }; }).filter(function (x) { return Number(x.quantity) > 0; });
      if (!lines.length) { UI.toast("أدخل كمية مرتجع لبند واحد على الأقل.", "danger"); return; }
      state.key = state.key || ("purchase-return-" + state.invoiceId + "-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 10));
      state.busy = true; render();
      try {
        var result = await AlKhayalApi.request("/api/purchases/" + state.invoiceId + "/returns", { method: "POST", body: JSON.stringify({ warehouseId: state.warehouseId, returnMode: state.mode, operationKey: state.key, lines: lines }) });
        state.key = null;
        state.lastResult = result;
        state.inputLines = {};
        await loadLines();
      } catch (error) { state.busy = false; render(); UI.toast(error.message || "تعذر تسجيل المرتجع.", "danger"); }
      state.busy = false;
    }
    Promise.all([AlKhayalApi.request("/api/purchases"), AlKhayalApi.request("/api/inventory/warehouses")]).then(function (data) { state.invoices = (data[0] || []).filter(function (x) { return x.status === "posted"; }); state.warehouses = (data[1] || []).filter(function (x) { return x.isActive; }); render(); }).catch(function (error) { container.innerHTML = UI.pageHeader(screen) + '<div class="empty-state">' + esc(error.message) + '</div>'; });
  }
  global.AlKhayalRenderers["purchase-management"] = renderPurchase;
  global.AlKhayalRenderers["purchase-list"] = renderPurchaseList;
  global.AlKhayalRenderers["purchase-details"] = renderPurchaseDetails;
  global.AlKhayalRenderers["purchase-returns"] = renderPurchaseReturns;
}(window));
