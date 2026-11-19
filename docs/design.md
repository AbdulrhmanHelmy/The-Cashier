# Design system

The Product Directory retains the full Add Product route and displays Edit directly in each row. Barcode, label printing, activation, and archive actions are available from the row's compact More menu. Quick Add remains a separate route and is absent from the directory toolbar. The warehouse view selector marks only the selected view as active; opening a stock-operation dialog does not imply a persistent selected view. Stock-count filters identify the product scope and variant scope separately.

Product Add/Edit uses paired identity fields, a compact three-column price group, and a desktop side column for optional properties, unit conversions, and identifiers. The columns stack below 1180px. Stock-operation dialogs retain all line fields and multiple products in a single scrolling modal with fixed actions. Stock-count tables align recorded and counted quantities. Movement history renders resolved purchase and sale invoice numbers in Arabic. With the corresponding route permission, a purchase reference opens purchase details and a sale reference opens the existing sale invoice view. Unresolved references show an explicit unavailable-reference label without exposing internal IDs.

In the warehouse manual receipt dialog, the sale-price field shows the current price for the selected product unit as guidance. An explicitly saved unit sale price takes precedence; otherwise the selected variant's base-unit sale price is converted by that unit's ratio. The new sale price remains an explicit entry.

Warehouse receipt, issue, and adjustment dialogs show filtered product matches directly beneath the search field as the user types a name or barcode. A click or Enter selects a result; arrow keys move through matches and Escape closes them. Editing the search clears the previous selection, so a different product must be chosen before saving.

## Shared export interaction

Supported table toolbars use `frontend/js/services/export-service.js`. In WinForms the user chooses Excel/CSV and a native destination before the request starts. Canceling performs no request and shows no loading state. The existing blocking modal remains open through generation and atomic native save; success appears only after saving. A browser fallback downloads the same backend response, and a shared busy guard prevents duplicate exports.

The Arabic Data and Maintenance Settings page offers local archive creation, daily backup settings, history, and file inspection before explicit restore confirmation. The standalone shell provides native folder selection. Manual creation and confirmed restore use the shared centered loading modal with an accessible indeterminate spinner. It blocks dismissal and background interaction until the request succeeds or fails, then restores focus through the shared modal lifecycle. The view uses shared RTL cards, forms and modal controls. See [offline backup and restore](offline-backup.md).

The Operations Settings hub exposes `إعداد الموازين` as the scale-only destination. Its compatibility route remains `business-activities`, but the page reads and writes only persisted scale barcode profiles (prefix, PLU length, weight/price value layout, decimals and check-digit behavior). Business activity profiles remain persisted for other feature dependencies and are not displayed or changed from this page.

The production WinForms host disables WebView2 developer tools and its default browser context menu, and handles only the F5 and Ctrl+R refresh accelerators. Browser-mode development is unchanged. DOM `contextmenu` events remain available, so the Touch POS multi-unit product menu and its touch long-press path continue to use the application handler while ordinary editing, selection, copy/paste, navigation, and keyboard shortcuts remain enabled.

The login picker requests only active account names. Remembered passwords are available only inside the desktop WebView2 shell: the narrow host bridge persists a DPAPI CurrentUser-protected value per normalized account, never in browser storage, SQLite, settings, or logs. Browser login keeps the control unavailable and never bypasses authentication.

Product creation supports virtual bundles such as gift bags. Each bundle has a fixed sale price and a saved recipe of simple stock products with positive whole-number quantities in their base units. POS availability reflects how many complete bundles can be assembled from the selected warehouse. Posting a sale consumes the components through FIFO in the invoice transaction, and returns/cancellations restore or reverse those same component movements. Bundles are kept out of purchasing and stock-count workflows; tracked components, product variants and fractional bundle sales are not supported.

Data-backed screens expose a `تحديث البيانات` action. Screens without the shared page header receive the same action from the shell. It reruns the active route renderer to request current server data without closing/reopening the screen, and restores visible search text so its results are loaded with the same query. On each tab activation the shell rechecks the API reads used by that page and shows a refresh notice only when one of those results changed. The branded home landing page has no data-refresh control or freshness check.

The Payment Methods overview is a compact operational control center rather than a passive card row. It combines persisted active-instrument counts and recorded payment totals with lightweight CSS distribution bars, permission-aware workspace shortcuts and a bounded five-row recent-activity preview. Wallet/terminal directories and payment history share a contextual section switcher, compact summaries, resettable filters, status badges and explicit loading/empty/error states. The workspace remains the sole page-level scroller; tables retain bounded horizontal overflow. No banking, settlement or review state is inferred from invoice-payment data.

The home route is a branded navigation landing page with no KPI, recent-sales, or operational-summary requests. Persisted sales, profit, and receivables reports remain available through their dedicated routes according to account permissions. See [home landing page](home-command-center.md).

Standard Sales and Touch POS derive each new line's selected-unit price from the current exact base-unit variant price, applying unit conversion before rounding to piastres. A base-unit quantity tier uses the selected larger unit's price divided by its conversion factor for the whole line. The displayed per-unit rate is rounded to piastres; the line total is calculated from the larger unit's price and exact conversion ratio, then rounded once. Thus three bottles at a 100 EGP carton threshold display 33.33 EGP per bottle and total 100.00 EGP. A sale consumes available stock up to the selected warehouse's stock floor; any uncovered quantity remains on the invoice and makes profit coverage incomplete. Changing the picker variant uses that variant's price; adding quantity to an existing draft line preserves an explicitly overridden price. Potential inventory margin remains explicitly separate from realized profit. See [pre-Phase 9 audit](phase-9/pre-phase-9-pricing-fifo.md).

The return form explains that sales-refund cashbox balances may become negative. Credit returns settle remaining customer debt first and show the residual cash and account adjustment in saved details. See [sales-refund-overdraft.md](sales-refund-overdraft.md).

The customer directory shows each customer's persisted current balance alongside contact information. Installment Sales searches persisted customers by name, code or phone with the customer search API. Selecting a customer loads the saved account balance and latest statement movements inline and links to the full customer statement. The selected customer and search text live in the installment tab state, so returning from the full statement restores the same selection and search.

Customer management uses a Customers-scoped navy/copper treatment without changing the shared entity behavior: compact rows and aligned text actions retain navy as the primary action language, while copper is limited to hover/focus borders and supporting highlights. The add/edit dialog remains a responsive two-column shared modal with a copper title marker and token-based focus feedback. Customer Statement emphasizes the authoritative current balance in one larger summary card, balances the four period totals alongside it, and uses a compact bounded ledger table. The persisted values, filters, collection/debt actions, receipt printing, and semantic success/warning/danger colors remain unchanged in Light and Dark themes.

Purchase and sales invoice history display saved return-state badges (`مرتجعة جزئيًا` / `مرتجعة بالكامل`) alongside their posting state. Untouched invoices have no return badge; cumulative saved returned quantities determine the distinction. See [returns-management.md](returns-management.md).

Returns exposes two sidebar pages: `إدارة المرتجعات` with search/add-by-original-invoice number, and `تفاصيل المرتجع` with a searchable saved-return selector. Clicking a management row also opens details. Detail navigation shares the management permission through `permissionRoute` and receives selected return state. Old separate return destinations redirect to management. See [returns-management.md](returns-management.md).

The Purchases sidebar retains only its original first and third destinations: `purchase-management` (إدارة المشتريات) and `purchases-list` (قائمة المشتريات). Supplier picking, expiry and confirmation remain within the purchase workflow. Former standalone `purchase-supplier`, `purchase-reports`, `purchase-expiry` and `purchase-confirm` routes are hidden compatibility redirects to the editor/list, so they do not appear in global search. This is a navigation-only change; persisted invoices and purchase returns are unchanged.

Purchase invoice supplier selection searches the persisted supplier page API after typing, shows a loading state, and fetches 20 matches per request with an explicit load-more action. Opening a new invoice no longer downloads the full supplier directory. Editing a saved draft loads only its selected supplier by ID.

Touch POS cash checkout explains `إيراد الوردية` as its automatic receipt destination and confirms the persisted deposit after success; no additional selection step is required. See [sales-shift-revenue.md](sales-shift-revenue.md).

Touch POS inherits the bounded workspace height through the mounted `.workspace-page` tab wrapper. Its payment header sizes to its wrapped controls, cart lines scroll independently, and the existing checkout summary stays below the lines with local overflow available in short windows. The empty cart retains all checkout controls with Complete Sale disabled.

The Touch POS `فاتورة جديدة` button sits directly below `إتمام البيع`. It confirms before leaving an unposted cart with items, then resets the editor's invoice, lines, customer, discount, payment and business date while retaining warehouse and product browsing. Successful checkout performs this reset automatically and reloads product availability for the next sale. Saved documents remain unchanged. Reset is blocked during save/post and barcode lookup.

The Expenses section exposes `إدارة المصروفات` (saved type list/search/add), `سند صرف` (saved type, cashbox/bank, amount/date/payee/notes, account balance and latest 200 vouchers), and the expenses report. Old expense destinations redirect to these pages. Drafts and uncertain retry identities survive tab changes. See [expenses.md](expenses.md).

The product catalog is a real persisted slice. Its list and forms do not show the shared product mock records or a mock banner. Loading, empty, failure, retry, validation, success, pagination, status, and concurrency-conflict states use the existing visual system. Product Catalog is the visible operational entry and provides contextual add, quick-add, edit, category-management, and unit-management actions. Category and unit management use full-width table-first pages and shared compact centered RTL modals for create/edit, with inline errors, loading/duplicate-save protection, Escape/cancel/close handling, focus management, and active/inactive actions. The product form uses a compact two-column RTL basic grid, inline optional properties, and one sticky Save/Cancel footer. It persists an optional reference purchase cost and offers the same category-add modal without unmounting the draft, refreshing options, and selecting the created category. The inventory sidebar contains Product Catalog, Warehouse Directory, and the stock movement, low stock, and stock valuation reports. Warehouse Directory renders persisted warehouse operations and balances. Product variants, SKU, and barcodes are catalog metadata; quantities, warehouse balances, FIFO cost, and stock movements belong to the inventory API rather than the product master.

Driver Management retains its compact table, search, paging and edit flow, with a row action that opens the singleton Delivery Orders workspace tab using the driver's stable ID. Delivery Orders initializes its existing driver filter from that navigation state before requesting orders, resets to page one, and clears old results during filter reloads; the user can then choose another driver or all drivers. Normal route permission checks still apply to this navigation.

The login card has a mutually exclusive zero-user setup state. It displays `مرحبًا بك في الخيال. يجب إنشاء حساب المدير للبدء.` and a compact `إنشاء حساب المدير` form for username, password, and confirmation. The normal login form remains the only state whenever any user exists. Both states preserve the existing Arabic RTL card, themes, keyboard focus, inline errors, and local-password-hash explanation.

The product name cell shows compact badges for active combination count and configured tracking indicators. Edit shows saved variant names and populated identifier columns. Sizes and colors have no controls, badges or columns. Historical combinations retain their IDs and database links; see [retired product attributes](retired-product-attributes.md). Neither view adds inventory quantities.

New-product creation exposes expiration tracking as its only optional product property. Product type, batch, serial, and IMEI controls are intentionally absent from staff-facing forms. Existing products retain all legacy tracking assignments, conversions, custom attributes, variants, and identifiers when edited; supported hidden capabilities are carried forward in the aggregate request rather than disabled or overwritten. The barcode section is always visible and provides manual entry plus an Arabic internal-barcode generator. The server remains the authority for uniqueness, rejecting codes used by active or retired barcode history.

General Settings uses bounded two-column forms that collapse through the shared 560px/760px breakpoints. Company, general, appearance, and database information are real screens and never claim mock values are persisted; the maintenance screen explicitly labels backup/restore as unavailable. Appearance reuses `AlKhayalTheme` and applies Light, Dark, or System immediately without CSS zoom or a second theme system.

## Purchase product selection

Supplier settlement dialogs require a saved treasury account, show its available balance and explain the atomic deduction. On-account payments explain automatic allocation by earliest due/invoice date, then earliest creation. Saving refreshes both supplier summary and invoice paid/remaining fields. Loading/empty/retry states and locked uncertain retries preserve a single financial command. See [supplier-payment-treasury.md](supplier-payment-treasury.md).

The Suppliers sidebar exposes قائمة الموردين, حسابات الموردين (`supplier-payment`), and the supplier report. The supplier directory stays unchanged. The accounts page has a searchable, paged supplier dropdown and displays only the selected supplier's account and invoices, current payable/credit, and contextual add-debt-invoice and account/invoice-payment dialogs. No invoices load before selection. Search stays mounted while only results refresh. Purchase drafts are marked as not due, invoice sources are explicit, and loading/empty/retry/stale-response behavior is bounded to the relevant card. Other supplier pages are hidden compatibility redirects. See [supplier-accounts.md](supplier-accounts.md).

The cash payment option in Purchase Management reveals a required cashbox/bank selector with current saved balances and an explicit empty option. Credit hides/clears it. The saved selection survives redraw and draft reopening; a payment-account change must be saved before posting. The shared RTL form grid/themes remain unchanged. The hint explains that posting, rather than draft saving, deducts the total. See [purchase-treasury.md](purchase-treasury.md).

Purchase Management loads its first 20 catalog results automatically after supplier/warehouse initialization. The bounded inline product picker remains visible above invoice lines; it supports empty and single-character searches by name/SKU/barcode, previous/next pages, explicit loading/empty/error/retry states, and keyboard buttons. Search and pagination repaint only `#purchaseResults`, preserving `#purchaseSearch` and invoice header/line inputs. Only active, nonarchived stock products are offered. Product selection loads persisted variants/units/reference prices through the existing inventory/catalog API. No stock-on-hand is required to purchase a product. Responses are guarded by request identity and active-route state. The picker uses shared theme tokens, intrinsic sizing and the 760px breakpoint; no separate viewport scroller is added.

Purchase Management includes a supplier search by name, phone, code or contact above the invoice header. Results are filtered from persisted supplier records; selecting one sets the supplier ID used by the draft/post API. The invoice keeps its selected supplier through redraws and draft saves.

The back control sits at the far left of the quick-access row, aligned beneath the username. It remains visible beside the shortcuts. It follows the user's in-app screen navigation and activates the previous visited workspace tab. Existing mounted tab state remains intact, and the control stays disabled until there is a previous screen.

## Treasury accounts

The persisted treasury module exposes قائمة الخزنة والبنك, إيداع رصيد and سحب رصيد only. The unified cash/bank table provides add/edit/delete dialogs, a persistently mounted name/code search, loading/empty/retry/error states and exact two-decimal balances. Posting forms select an account and display its current balance; a saved receipt shows the movement ID, amount, local time and balance after posting. Uncertain requests retain and lock their draft for retry. Used accounts cannot be deleted, even at zero. Hidden legacy aliases resolve to the directory. These screens use shared forms/dialogs/themes and add no viewport owner. See [treasury.md](treasury.md).

## Visual language

Supplier management uses a compact, supplier-scoped table and modal treatment. Each supplier row exposes one contextual `كشف حساب` action that opens the existing singleton Supplier Accounts workspace tab with the stable supplier ID. Supplier Accounts uses one paged server-search selector, loads the selected supplier only, and combines the authoritative supplier projection, statement endpoint, and invoice endpoint in one workspace. Date filters are sent to the existing statement API; the movement-type control only filters the returned authoritative movements. Overdue totals remain labelled unavailable because the current account contract does not expose an authoritative aggregate.

The Reports Center uses the single `report-overview` sidebar destination. Its compact navy/gold heading, period controls, two-tier permission-filtered KPI hierarchy, search, and registry-driven report categories share one surface. Category cards use local SVG icons and concise links. The workspace scrolls vertically; cards and category columns use intrinsic grids. See [reports-center.md](reports-center.md) for the route inventory, metric sources, access rules, and local-date periods.

The product uses deep navy for structure and trust, muted gold for primary actions and identity, neutral data surfaces, restrained shadows, and compact desktop density. `tokens.css` is the semantic source for color, spacing, radii, elevation, and focus treatment. `themes.css` maps those semantics for Light, Dark, and System modes; modules must consume tokens instead of hard-coded theme colors.

The UI uses local system fonts only. It has no runtime CDN, icon font, remote image, or remote-font dependency. Motion is limited and disabled through the reduced-motion preference.

The compact transparent AlKhayal mark lives at `frontend/assets/alkhayal-mark.png`. It appears in the login lockup, authenticated top bar, favicon, invoice preview, printer preview, and large home emblem. The sidebar header shows the Arabic name without the mark, and its toggle is centered when the sidebar collapses. The home wordmark uses the original lettering from `frontend/assets/alkhayal-logo-reference.png` inside a clipped image viewport; the source file is unchanged. Other brand text remains live text using the existing local system font stack. See [home landing page](home-command-center.md).

The Settings hub exposes an administrator-only Operation Log with user, local time, method/path, HTTP result and filters by text, date range and outcome. The API records mutation attempts and successful login/session establishment without request bodies or query strings. Read-only API calls are not included. See operation-audit.md.

## RTL shell

The document is Arabic and `dir="rtl"`. The authenticated shell has a top bar, global screen search, a compact quick-access row, closable workspace tabs, a right-hand module sidebar, a scrollable content workspace, and a persistent status bar. Quick access always shows the highest-priority permitted daily actions: Touch POS, quick product add, purchase creation, customers, warehouses, returns, treasury, and alerts. The row measures its available width after sidebar and DPI changes and moves remaining actions into a keyboard-accessible More menu. The former hidden preference is removed on startup. Workspace tabs retain their mounted page DOM while users navigate elsewhere, preserving in-progress cart, search, and form input state for the lifetime of the open tab. Selecting a route from the sidebar or shell quick-access row removes and recreates its already-open tab to load fresh data; its unsaved-change guard runs first. Home landing shortcuts activate an already-open workspace tab without replacing its mounted state. Contextual in-screen navigation continues to reuse the open tab while applying its requested route state. A tab's complete page name remains available as its hover tooltip and accessible name when the visible label is shortened. Closing a tab discards that in-memory state; it is not a database save. Right-clicking a workspace tab opens a context menu to close all open tabs; the dashboard remains pinned, and each open editor keeps its existing unsaved-change guard. The outer layout uses an explicit physical direction while text/content surfaces restore RTL so the sidebar remains on the right without reversing Arabic content.

At compact widths the sidebar collapses to icons. Selecting a module opens a keyboard-accessible route dialog rather than depending on hover. Long Arabic labels wrap or truncate with accessible names, and tables remain horizontally scrollable.

## Interaction model

`screens.json` registers each route's Arabic title, module, description, renderer, and optional data/workflow metadata. Navigation from the sidebar, Settings hub, quick-access row, global search, workspace tabs, and in-screen links resolves through the same registry. Administrative destinations use `settingsGroup`/`settingsOrder`; legacy aliases use hidden `redirectTo` metadata. No second hard-coded Settings route list is maintained.

Page freshness snapshots include only data relevant to the active screen. Treasury account balance changes are excluded from customer and workforce screens that use account names/types only; Purchase Management tracks treasury balances only while cash payment is selected and shown. Modal-only supplier payment account lookups do not mark the underlying page stale.

Reusable components include page headers, explicit mock-data notices, cards, KPIs, tables, filters, pagination, forms, validation messages, badges, empty states, modal dialogs, toasts, chart-like bars, invoice editors, pickers, and print previews. Buttons either perform a session action, navigate, alter UI state, export a real CSV, or clearly identify preview-only behavior.

The customer module exposes `إدارة العملاء`, `كشف حساب العميل`, and the customer balances report. The statement displays persisted current debt and filtered movements, with contextual collection/increase dialogs; `customer-credit-payment` is a hidden redirect to it. Current debt is distinct from period closing balance. See [customer-statement.md](customer-statement.md). The standalone `إضافة عميل`, `تعديل بيانات عميل`, and `اشتراكات العملاء` entries are not sidebar links. Customer creation and editing live inside `إدارة العملاء`: the Arabic `+ إضافة عميل` action opens the customer form and the per-row `تعديل` action opens the same form with the saved values, and both save through the persisted customer API. `customer-add` and `customer-edit` remain registered as hidden contextual routes, and `customer-subscriptions` remains registered and hidden until it is implemented. Customer creation and editing retain their existing persistence behavior.

## Authentication and themes

The login screen provides username/password fields, password visibility, optional username remembrance, health status, loading/error states, theme selection, and native form submission. Passwords and bearer tokens are never stored in web storage. Theme and harmless navigation preferences persist locally; the CSRF token remains in session storage.

## Data honesty

Authentication and the documented customer, supplier, product-catalog, warehouse/inventory, user, activity, feature, and general/company settings slices are real and SQLite-backed. Routes outside those implemented slices keep their stable navigation identity but render one uniform Arabic `هذه الشاشة قيد الإنشاء` state; they do not expose sample records or session-only mutation controls. The dashboard uses bounded, permission-filtered SQLite-backed report, alert, workforce, delivery, treasury, catalog, and sales endpoints for its summaries; it never presents fabricated business totals, dates, alerts, or activity. Unsupported or unauthorized sections remain unavailable rather than showing placeholders as business facts.

The sidebar consumes semantic light/dark tokens for its background, surfaces, borders, text, icons, hover, and active states. The active child and its parent module are both indicated. Theme choice remains persisted by `AlKhayalTheme` as Light, Dark, or System across navigation and reload.

Business activity profiles are also real and SQLite-backed. `الإعدادات ← نوع النشاط التجاري` uses shared cards, fields, badges, confirmation dialogs, and toasts. When no profile exists it becomes the first authenticated route; login remains unchanged. Activity cards support search and multi-selection, the primary activity is explicit, and recommendation deltas are shown before an administrator confirms saving. Non-administrators receive a read-only view. The layout collapses at the shared 1050px and 760px breakpoints and consumes the same Light, Dark, and System tokens.

Feature management is real and SQLite-backed at `الإعدادات ← الوحدات والميزات`. Cards show effective state, source, recommendation, and dependencies. Administrator changes require confirmation and update the shell only after persistence succeeds. The two-column grid collapses at 760px and shares the existing tokens and workspace scroller.

Settings is the single administration entry. Its compact searchable landing screen groups only existing routes under General Settings, Activity & Features, Users & Security, Operations, and Maintenance & Data. The group cards are derived from registry metadata, use existing local SVG/icon conventions and semantic tokens, and collapse to one column at 760px. Configuration-only sidebar groups are removed; daily shift approval remains an employee operation while shift configuration remains under Settings.

User management is real and SQLite-backed at `الإعدادات ← المستخدمون والصلاحيات`. The stable route is `users-list` and its renderer key is `user-management`. The table supports name/username search plus role and state filters. Add/edit and password reset use separate dialogs. Cashier permissions are grouped by Arabic module name, searchable, selectable in bulk, and show disabled-module grants as stored but inactive. The permission layout becomes one column at 760px and uses semantic theme tokens. Legacy routes `user-permissions`, `sales-permissions`, and `settings-permissions` are hidden administrator-only aliases that resolve to `users-list`.

## Accessibility and compatibility

All interactive controls are keyboard reachable, focus-visible, labelled in Arabic, and usable without color alone. Native semantic controls are preferred. The implementation avoids unsupported Chromium 109 APIs and transpilation assumptions. Visual verification covers both light/dark themes and compact RTL layout; actual Windows 7/WebView2 qualification remains pending.

Search and list screens must keep the text input persistently mounted: typing repaints only the results region (a dedicated results host such as `#catalogResults`, `#salesListResults`, `[data-settings-results]`, `#activityGrid`, or the shared `[data-table-host]`/`[data-list-count]`/`[data-list-pagination]` nodes), never the focused input's ancestor. Refocus-after-redraw (`focus()` + `setSelectionRange` after rebuilding the input) is a prohibited substitute for this pattern because it breaks Arabic IME composition and fast typing. Async search flows must discard non-current responses via a flow-local token plus the shell render-generation guard (`context.isActive()`), so a stale response or a request left in flight when switching tabs can never repaint another screen; delegated handlers bound to the workspace container are reset at each screen render. POS retains its dedicated scanner path: `Enter` in the POS search performs an exact barcode lookup, while ordinary typing is a live debounced search that never requires Enter.

The single scale barcode profile is configured by an administrator in the Business Activities setup page, including first-run setup. It describes the EAN-13 prefix and PLU/value positions. Product SKU is the scale PLU. Weight payloads enter POS as kilograms, while price payloads are converted to kilograms using the saved product price per kg. The profile supports configurable EAN-13 layouts instead of assuming one scale vendor's fixed pattern.

## UI foundation repair contract

The shell owns one viewport: `.app-shell` fills the WebView2 viewport, `.app-main` owns the fixed application chrome rows, and `.workspace` owns page-level vertical scrolling. The sidebar navigation scrolls independently; the sidebar footer remains in the final grid row. Grid/flex boundaries must use `min-width: 0` and `min-height: 0` so long Arabic content can scroll instead of expanding the shell.

Do not add a second `100vh` or `calc(100vh - ...)` to a module. Use intrinsic responsive grids and bounded local scrolling only for genuinely wide tables, search results, product catalogs, cart lines, and modal bodies. The shared breakpoints are 1180px, 1080px, 1050px, 760px, and 560px. At 1080px the sidebar becomes a persisted 64px icon rail without overwriting the user's preference.

The WinForms host shows a lightweight navy/gold native splash with the official mark before licensing, API, or WebView2 startup work. The splash closes after the first successful login-page navigation and also closes on startup failure. The host is Per-Monitor DPI aware through `src/AlKhayal.UI/app.manifest` and `AutoScaleMode.Dpi`. It starts as a standard maximized window using the desktop working area, leaving the Windows taskbar visible and functional. WebView2 starts at zoom factor 1.0, disables built-in zoom/pinch controls, and blocks only desktop-host browser zoom gestures. Standard browser accessibility zoom remains unrestricted. See `docs/ui-audit.md` for the confirmed causes and verification limits.

Sidebar headers keep the brand and collapse control in one adjacent flex cluster. The collapsed 64px rail centers every navigation, theme, settings, and logout SVG through `justify-content: center`, zero horizontal padding, and fixed 21–22px icon boxes; do not compensate with positional offsets or transforms.
# Warehouse Directory in Task 2.4

The real Warehouse Directory uses the current navy/gold RTL shell, themes, workspace scrolling, and existing modal components. Inventory navigation still contains only Product Catalog and Warehouse Directory. Contextual actions open compact multi-line forms; count and movement history render within the directory. Legacy stock routes redirect there. See [warehouse-management.md](warehouse-management.md) for workflows and [inventory-ledger.md](inventory-ledger.md) for what is persisted.

Warehouse receipt/issue/adjustment/transfer forms save directly with the shared modal and theme tokens. The source is the selected warehouse; reasons and technical keys are absent. Required price inputs are in Arabic, single units/variants are automatic, and tracking fields are conditional on persisted product requirements. Counts show differences and conditional cost/identifier corrections. The balances view calculates total valuation only when the user presses **حساب قيمة المخزون**; its loading state does not block other screens. Missing costs or prices prevent apparently complete totals; documented legacy opening bases are identified. Movement history shows Arabic types, local timestamps, signed quantities, transfer direction and optional notes. See [inventory-fifo.md](inventory-fifo.md).

## Sales workspace and touch cashier

`فواتير البيع` is the standard invoice-oriented workflow and `الكاشير اللمسي` is its separate touch-first presentation. Both use `/api/sales` and the same SQLite posting service. The standard screen opens with persisted invoice history and enters a focused draft editor; the cashier keeps product selection, cart, payment choice, totals, and completion controls inside the available workspace pane. Product results and cart lines have bounded scrolling. Barcode entry uses the catalog lookup endpoint; variants and sale units are selected only when needed. Posted invoices are read-only and must be corrected through a modeled reversal or return.

The sales area also provides invoice payment collection, return processing, customer statement references, and an invoice receipt/reprint view. Invoice history offers draft deletion and guarded cancellation for an unpaid credit invoice; a canceled invoice remains visible and read-only. Cash, paid, returned, and serial-tracked cases show a clear conflict until their reversal policy is modeled. Returns select invoice lines and, for serialized products, the serials being returned. Phase 7.2 replaces the original browser receipt flow with configurable native C# rendering and direct Windows driver submission; spooler acceptance does not confirm physical completion. Automated browser and native visual verification is recorded in `phase-5-progress.md`.

Touch POS opens with a 40-card server page ordered by recent net base-unit sales; more cards load only on request. A horizontal category row stays above the bounded product scroller, and selected warehouse availability appears on cards. Category tint derives from the persisted category ID and uses subtle Chromium 109-compatible overlays. Customer search has a bounded result panel with an explicit “بدون عميل” choice; warehouse search appears only for enabled multi-warehouse mode. The fixed/percentage discount control updates visible tax and total values in place while preserving input focus. A percentage is rounded once to a two-decimal monetary discount before the existing invoice API receives it. Target visual sizes are 1366×768 and approximately 900×600; native WebView2 visual acceptance remains pending.

## Employee, payroll and attendance UX

The operational sidebar has an Admin-only إدارة الموظفين section for employee records, payroll, advances and attendance, separate from cashier-shift workflows. New employee codes and transaction numbers are generated from database identities. Employee archive preserves links and history. Payroll shows saved monthly salary, unallocated advances and computed net; payout requires a selected Treasury account and posts its withdrawal with the payroll record. Attendance actions record check-in/out automatically; break duration is entered in minutes and deducted from actual work time. Existing RTL forms, modals, focus and scrolling conventions are reused. See [routes, permissions and limits](phase-7/phase-7-1.md#workforce-payroll-and-breaks).

## Native printer settings and designer — Phase 7.2

Thermal invoice/receipt presets (`58mm`, `80mm`) now use a receipt hierarchy in `NativePrintRenderer`: centered optional logo and company identity, centered document title, compact metadata, full-width wrapping item names with aligned quantity × unit price and line amount, light dashed separators, and an emphasized grand total. Delivery details follow totals; a separately projected delivery fee appears in totals when the persisted sale contains delivery. Visibility and branding settings remain on the existing template. A4/A5 and shift report layout remain on the prior generic renderer path. The settings PNG preview uses the same native layout and crops thermal samples to content height; physical print paper settings and pagination remain unchanged.

The printer Add/Edit modal previews unsaved form values automatically after a 320ms pause. Logo upload, removal, visibility, paper dimensions/preset, document type, header/footer, margins, scale and continuous mode invalidate any pending response immediately. The previous PNG remains visibly marked as stale while a replacement renders or after an error. Closing the modal clears its timer and image data URLs. Thermal preview renders the entire sample strip, while A4/A5 retain first-page preview behavior. Preview never saves settings or submits a print job.

The current `settings-printers` landing view presents configured AlKhayal printers as cards and keeps document-type assignments in a separate section. Add/Edit uses a compact modal for an installed Windows printer, explicit paper choice, logo visibility/upload, footer and real C# sample preview; detailed legacy template controls remain under **إعدادات متقدمة**. The cards report whether a saved Windows printer name is still enumerated, not physical readiness. A test-print button requires explicit confirmation and a saved assignment. The page uses the existing RTL shell and theme tokens.

`settings-printers` is the sole printer administration entry, titled الطابعات والطباعة. Hidden receipt/invoice settings aliases redirect there. The RTL section editor configures saved profiles/templates, paper, fonts, spacing, optional bounded PNG logos, column widths and section ordering. Editor preview is a C#-rendered PNG; normal business actions submit directly with a short busy state and never open preview/dialog/PDF. The panel uses existing theme tokens and a 760px single-column breakpoint. See [Phase 7.2](phase-7/phase-7-2.md).

## Product label designer and workflow — Phase 7.3

The existing printer settings screen includes independent label profiles/templates and a numeric selected-element editor over native PNG preview. Physical mm dimensions, custom stock, roll feed pitch, sheet cells/gaps and initial used cells are explicit. Product directory actions and collapsed saved-product print panels preserve unsaved form fields; production output reads persisted data only. `product-labels` is a hidden contextual, module-gated paged bulk workflow. Old barcode/printer design routes redirect to settings-printers. See [Phase 7.3 design and limitations](phase-7/phase-7-3.md#settings-paper-and-designer).


## Sales payment instruments

Configured wallets and card terminals, atomic full-invoice attribution, administrator-only management/history, legacy contract mapping, electronic refund restrictions and physical cash shift exclusion are documented in [payment-methods.md](payment-methods.md). Migration 074 is additive; original financial records and credit collection workflows remain intact.

## Phase 9.1 transaction report screens

The التقارير group contains Sales, Purchases, Returns and Payment Methods reports using saved SQLite facts, simple date/filter controls, numeric cards and compact paged tables. Shared report helpers retain mounted inputs, stale-response guards and permission-aware original-document/history links. Scoped intrinsic styles reuse RTL Light/Dark card/table tokens and existing responsive breakpoints; no chart library or viewport owner was added. Payment history now accepts an instrument identity in route state. See [routes, labels, accounting distinctions and blocked visual acceptance](phase-9/phase-9-1.md).

The Arabic expense voucher report uses the shared report period controls and paged tables. It labels its totals as saved paid vouchers rather than verified operating expenses. See [Phase 9.2](phase-9/phase-9-2.md#expense-voucher-report--recovery-implementation).

`report-net-profit` is a real report in the التقارير group. It uses the shared date presets, daily/monthly selector, compact exact-value cards and table, paged voucher review and administrator classification/reason controls. Its visible scope is `صافي ربح المبيعات بعد المصروفات المسجلة`. Incomplete source evidence or unclassified vouchers suppress net profit and show missing counts/amounts. Filters remain mounted, responses are guarded against stale renders, uncertain classification retries retain their operation key, and existing RTL Light/Dark tokens and workspace scrolling remain in use. No charts, framework, remote resource or new viewport owner is introduced. [Sources, limits and validation](phase-9/phase-9-2.md).

## Phase 9.2 simple product and stock reports

Sales movement references open the existing Touch POS tab with the latest posted correction revision for the sale. The POS uses its saved invoice lines and totals for viewing; the cart keeps five aligned columns with independently scrolling lines, plain values, and no remove control. Correction starts from a separate explicit action, while saved drafts retain their confirmation flow.

Five real routes use shared `stock-reports.js`: gross product profit, current FIFO valuation, top products, availability and saved stock movements. Numeric cards and compact RTL tables reuse existing tokens/scroll ownership, date presets for event reports, mounted searches, category/warehouse ID filters, status/type/order dropdowns, server pagination and permission-aware document references. Unknown evidence displays `غير مكتمل`; expected inventory margin is visibly distinct from realized gross profit. Availability retains its warehouse-specific zero/available view; Task 6 reorder alerts use a separate product-wide total. Current snapshots have no date presets. No charts or new frontend dependencies. Rendered 1366×768 Light/Dark/native acceptance remains unverified. [Semantics and acceptance](phase-9/phase-9-2.md#frontend-and-compatibility-acceptance).

## Phase 9.3 simple operational reports

Seven التقارير routes use numeric cards, compact server-paged tables, existing date presets, mounted search/person/status filters and permission-aware source links. Current debt/payables/status/overdue labels remain distinct from period event labels. Overview suppresses incomplete profit and displays only server-authorized metrics. Shared Arabic RTL Light/Dark tokens, table wrappers and workspace scrolling are retained. No charts or new frontend dependency. Rendered/native acceptance remains unverified. [Screen inventory and semantics](phase-9/phase-9-3.md).

## Native mandatory activation

Before WebView2 or the login page, the RTL WinForms dialog presents تفعيل برنامج الخيال, explains free perpetual activation, and provides a selectable readonly request display and exactly three action/edit controls: copy request, signed-license input and activate. Only the title-bar X closes startup; save/import/paste/exit buttons are removed. It uses local Segoe UI, navy/gold colors, DPI scaling and keyboard controls. Invalid/insufficient identity stays locked. No trial, skip, guest, expiry, payment or online option exists. Administrator activation installs a machine-wide license; cashiers only read it. [Flow and limitations](phase-10/phase-10-1-licensing.md).

The activation dialog now performs async validation/install/revalidation, disables Activate only while pending, and preserves the pasted code after UAC cancellation/denial. Its two buttons, editable license input, selectable request display and layout are unchanged. Only the bounded helper operation requests elevation; normal startup remains non-elevated. [Storage/UAC hotfix](phase-10/activation-storage-hotfix.md).

## Task 5: invoice history in Touch POS

View now uses the main Touch POS layout with the saved invoice number/date/status, customer and warehouse (including names absent from active selectors), original lines/prices/discounts/tax totals, and paid/returned/outstanding information. It starts in view mode. The first draft mutation asks for Arabic confirmation; rejection leaves the snapshot intact. Save Changes and Cancel replace checkout actions for opened invoices. Cancel discards local lines/header changes. A new-sale cart already in the POS tab stays in tab memory and can be resumed with متابعة الفاتورة الجديدة السابقة. Explicit New Invoice retains its discard confirmation and normal checkout workflow.

Posted, returned, canceled and unknown states display an Arabic explanation that only drafts support editing; they never enter edit mode. Original totals are displayed even if today's tax settings differ. Confirmed draft edits use current settings and existing server validation. Drafts with inactive references or changed catalog prices may require correcting/re-adding the affected item before saving. No obsolete registry routes were removed: the standard new-sale surface and compatibility links remain available. The renderer and stylesheet structure, RTL and bounded shell are retained. [Acceptance and limitations](qa/task-5-touch-pos-invoices.md).
### Correcting a posted invoice in Touch POS

Invoice View continues to use the main Touch POS layout. Eligible posted invoices start read-only and the first cart mutation asks for confirmation. After confirmation, item add/remove, quantity, price, line discount, and invoice discount controls use the normal cart. Header identity fields remain subject to backend invariants. Save Changes asks how to reconcile a total difference before sending one idempotent correction request; Cancel Editing restores the server snapshot without a write.

When an existing invoice enters edit mode, the POS loads its current product/unit details once and keeps the saved line amount while its quantity and price are unchanged. A changed automatic base-unit quantity uses the current tier ratio for the preview; resetting a manual price also has the current product details available. The persisted invoice history remains read-only until the edit is explicitly saved.

With cashier shifts enabled, the original persisted shift must still be open and must be the current user's open shift. With shifts disabled, the persisted invoice business date must equal the server's current local business date. Canceled invoices, prior revisions, prior-day invoices, closed/different-shift invoices, and invoices with returns are view-only and show the backend reason.

## Task 6: unified Alerts

`التنبيهات` (`alerts-list`) is the only Alerts sidebar child and the existing Quick Access Alerts tile remains the only alert shortcut. The old reorder/low-stock routes are hidden compatibility redirects; old grants still open the unified page but expose only stock/reorder types. Full Alerts access exposes stock, expiry and installment records from SQLite. The mounted Arabic type selector filters low stock, reorder, expiry, installments and other; `other` has an honest empty state because it has no persisted generator. No mock notification dataset or fallback is connected to this route. Empty, loading, refresh failure and manual retry states are explicit. Rows link to the exact product editor or installment contract when the user has permission for that destination.

The existing Quick Access bell receives an accessible unread badge (hidden at zero, `!` on refresh failure). A shared cached snapshot serves the page and badge. Successful relevant same-origin writes invalidate it; same-tick invalidations coalesce and in-flight responses are revision guarded. Window focus/manual refresh pick up other-terminal changes without timers/polling. Mark All persists through the API and reloads the authoritative read state before settling. Standard RTL/themed controls and the original shell/viewport remain.

## Task 7: essential shift controls

The header replaces the local-session badge with current shift status, elapsed hours/minutes, an identifier and an Open/Close action (cashier name in its tooltip/detail). Standard Sales and Touch POS reuse the same control. Checkout is disabled with `افتح وردية أولاً` until a shift is confirmed; product browsing and draft work remain available. State refreshes on mount, window focus, shift writes and before posting; elapsed text updates locally without network polling. Backend transaction checks remain authoritative.

Opening reuses the opening-cash form. Closing shows expected cash, counted cash and a live difference; a required explicit confirmation checkbox precedes submission. A stale expected count requires refreshing/recounting. Management retains date/user/status filters and server pages, adds duration and lifetime shift counts, and shows separate paged movement and linked-document/audit histories. Invoice actions respect sales-screen permission.

## Touch POS customer and optional delivery rider

Touch POS keeps the compact customer search control in its header. Its dropdown fixes `إضافة عميل جديد` above the scrollable `بدون عميل` and existing-customer options. The action reuses the Customers page's persisted add modal through `R.openCustomerModal(afterSave)`; successful saves select the returned customer, and cancellation leaves the sale state untouched. Delivery offers `بدون طيار — تعيين لاحقًا`; unassigned orders display `تعيين لاحقًا` in history and print output, while the persisted delivery snapshot stores empty driver name/phone.

## Touch POS unit pricing
Product configuration includes a compact optional additional selling-unit section with conversion and optional price. Touch POS keeps the ordinary card click on the base unit, offers configured units through right-click or a touch long-press menu, displays the per-selected-unit price in each cart line, and provides a line-only price override with reset.

## Touch POS compact workspace layout (2026-11-08)

Touch POS owns its existing `data-page-refresh` button inside the wrapping cashier toolbar, preventing the shell from inserting a separate refresh row above a full-height POS. The global header/sidebar/tabs/footer remain unchanged. The mounted `.sale-touch-page` is a named size container; Chromium 109 supports the CSS container queries used here. Desktop panels share one header/items/summary structure and a two-column action grid for new invoices and existing-invoice edits. Identity/payment metadata wraps compactly; edit permissions, financial calculations, scanner bindings, shift mounting and persistence remain unchanged.

The available workspace, rather than monitor width, determines compression. At 760px or less, the POS uses a single outer scroller and stacks products above the invoice. A workspace height of 560px or less also uses outer POS scrolling with intrinsic cart lines/summary, while retaining side-by-side panels when the workspace is wide enough. Cart lines and summary never rely on a viewport offset or hidden overflow. In the outer-scroll fallback a long cart may require scrolling to reach totals/actions; products retain a bounded browsing scroller. Taller workspaces retain independently scrollable product/cart items and the existing bounded summary overflow for unusually large payment/collection content. Product cards fill equal-width columns, names wrap, and prices/quantities retain their existing controls. See the product-grid sizing policy below. No resize script remounts the cart. Manual visual/DPI/legacy-runtime qualification remains required.


Saved sales drafts (2026-11-08) share the new-invoice Touch POS header, cart lines, totals and responsive action grid. Their compact identity distinguishes view-only and editable modes. View-only exposes Resume/Edit; consented drafts expose normal Clear/Save Draft/Complete Sale plus Cancel Edit, while posted correction retains its existing actions and restrictions. Native three-choice consent uses the shared modal; view-only, cancel, Escape and backdrop dismissal resolve without mutation. Contextual POS navigation preserves the suspended new cart, consumes each requested invoice ID, remembers the successfully loaded ID for refresh, rejects stale loads and guards destructive replacement/close/refresh when dirty. Tab switching retains the mounted state. Product caches are retained across authoritative draft saves without replacing the saved monetary/cancel snapshot. The prior workspace container sizing and single outer-scroll fallback remain unchanged.
# Touch POS compact item rows and invoice discounts (2026-11-08)

The shared Touch invoice renderer uses five RTL cells: product/metadata, quantity, editable unit price, isolated line total, and a fixed remove action. Names truncate with their full accessible text/title retained. Unit/variant, batch/expiry, serial/IMEI and exact-pack pricing notes share one secondary, keyboard-expandable details cell. Weighted quantities retain scale-6 editing and the gram/kilogram dialog; the duplicate weight badge is omitted in Touch mode.

Touch exposes fixed/percentage invoice discounts only. New lines retain the required API `discount: "0.00"`. Any historical nonzero line discount makes that Touch invoice view-only, with an explanation beside the summary and the saved discount disclosed in item metadata. Resume, save, correction and checkout are blocked, including direct handlers/payload guards; New Invoice starts an independent empty cart while the original remains saved. No conversion, financial recalculation or historical rewrite occurs. Backend line-discount support and the standard sales editor remain available under their existing contracts.

Desktop layouts reserve at least 460px for the cart and keep five cells per row. Cart container queries at 440px and 340px provide deliberate multirow fallbacks when the actual panel becomes narrower. Numeric totals can wrap within a bounded cell for unusually large amounts. Existing workspace height/narrow-width scrolling, invoice summary and bottom-action rules remain intact. Chromium 109 supports the existing CSS container-query approach; visual/DPI/Windows 7 qualification remains manual.


## Touch POS product-grid width optimization (2026-11-08)

The body now allocates flexible tracks at 1:1.2 (about 45% products / 55% invoice), subject to a 520px invoice minimum. This reduces product-card width and gives the invoice more room for its line items and totals. Workspaces at 760px or less stack the product browser above the invoice, avoiding a cramped two-panel layout. All five cart cells, control sizes, summary/actions, shared draft layout and short-workspace outer-scroll fallback remain in place. The cart fallback threshold of 440px keeps normal desktop items on one row at the new minimum.

The product scroller is the named inline-size container `touch-browse`. The grid uses one full-width column by default, then two at 225px, three at 340px and four at 455px of actual browsing width. These thresholds keep cards at least 108px wide plus the existing 7px gaps. Four is the maximum even on wide monitors; equal `1fr` tracks fill the narrower product pane. Sidebar state, viewport height and scrollbar allocation do not require JavaScript resize handlers. The renderer and all selling logic are unchanged. Source/DOM tests and sizing arithmetic are automated; actual geometry and native Chromium 109/Windows DPI acceptance remain manual.


## Collapsed sidebar navigation flyout

The 64px rail now opens one temporary RTL navigation overlay on section hover or focus.
The previous limitation came from hiding `.sidebar-children` in compact mode and exposing
those routes only through a click-triggered modal. Expanded sidebar toggling remains unchanged.
`frontend/js/components/sidebar-flyout.js` clones only the hovered section's existing filtered
child DOM: `screens.json` -> `buildSidebar` -> effective feature/screen access filtering remains
the sole navigation source. The current registry has flat route arrays within sections; existing
nested child DOM, if present, stays together in one overlay with indentation, never cascades.
Access reconciliation closes the overlay before rebuilding navigation. Item clicks call the same
`navigate` function and retain route authorization checks, workspace tabs and saved rail preference.

The single body-level fixed overlay sits immediately left of the sidebar, outside its overflow
containers, without changing shell grid sizing. Its width is capped at 300 CSS pixels and available
space; height/top are constrained between the global header and footer. The body scrolls internally
while its title stays visible. Resize and sidebar scroll reposition it; scrolling its source out
of view dismisses it. Layer 40 is above shell chrome (6) and below modal dialogs (100). Shared
sidebar tokens provide light/dark navy/gold styling without animation or new dependencies.

Opening is immediate; a 160ms close delay bridges pointer travel. Pointer presence in either
region or keyboard focus within the overlay prevents dismissal. Keyboard focus opens a section;
Tab/ArrowLeft/ArrowDown enters its destinations, native buttons retain Enter/Space activation,
Shift+Tab at the first item returns to its source, and Tab at the last moves to the next rail
control. Escape dismisses and returns focus when it was inside. Hover cannot replace an overlay
containing keyboard focus. Section buttons expose Arabic labels and current expanded/controls
semantics; the active destination has `aria-current="page"`. Direct footer actions retain their
click behavior and now have explicit Arabic labels/tooltips. Neither hover nor focus writes storage.

Automated verification uses Node DOM/event fixtures, not browser automation. Actual geometry,
Arabic readability, themes and DPI/runtime behavior require owner visual verification; Chromium 109
API compatibility is an implementation constraint, not a tested Windows 7 deployment guarantee.
# Purchase document viewing

`purchase-details` is a hidden deep-link screen reached through the purchase list, stock movement references, purchase reports, and purchase invoice references in supplier accounts. The workspace reuses its single tab and replaces route state when the invoice ID changes. Its read-only RTL layout uses the workspace scroll owner and bounds only the wide line-item table. Draft invoices may offer a separate edit action; posted invoices do not. Values come from the saved purchase read API, including posted inventory-line conversion snapshots where available.


## Report navigation

Every visible report route appears once in the dedicated `التقارير` sidebar group, ordered from the activity summary and sales to purchasing, parties, inventory, finance, and optional-module reports. Operational groups retain only daily actions and directories, keeping the compact sidebar usable when all modules are enabled. Sidebar links and collapsed hover flyouts read the same permission- and feature-filtered registry entries; the Reports group disappears when no report is accessible. Search and workspace tabs still resolve the original routes.

## Settings and feature presentation

The Settings hub is registry-driven but excludes Product Directory's category and unit management routes. Its responsive card grid uses intrinsic columns and the workspace scroll owner. The obsolete warehouse-settings route is a hidden compatibility redirect rather than a visible card.

Modules & Features renders only meaningful optional modules and supported product properties. Core modules stay enabled in the server registry and are not displayed as locked cards. Legacy optional keys remain available to the persistence and API compatibility layers but cannot resurface as administrator choices. The page keeps the existing confirmation, dependency, audit, and persistence paths and uses a compact two-column RTL card grid with the standard 760px single-column fallback. See [the audit](settings-feature-audit.md).

`فواتير البيع` is the single primary sale-invoice destination. The former `sales-list` URL is a hidden compatibility redirect, so existing bookmarks and tabs open the unified invoice workspace without creating a second primary entry. `تفاصيل المرتجع` is likewise a contextual route opened from `إدارة المرتجعات`, inherits that route's permission, and is not a sidebar or search destination. No business function, data, or renderer was removed by this navigation cleanup.

## Printed product documents

The native receipt renderer uses one four-column RTL product table (الصنف، الكمية، السعر، الإجمالي) for thermal and sheet layouts. It wraps descriptions and saved numeric values within their columns. Sales, delivery, purchase, and sales/purchase returns share this layout; shifts and payment vouchers retain document-specific content. The configured PNG logo is printed only when present and the logo section is enabled. The same native rows feed preview and physical output. The 58 mm preset uses smaller type and narrower columns. Saved line values and document totals are displayed without recomputation.

Printer-managed receipt branding: the configured PNG is rendered without changing its aspect ratio; a missing custom PNG prints no application mark. The printer template supplies the editable header and footer. Document-specific delivery/return metadata remains separate from the header text; company footer data does not override a cleared printer footer. Thermal receipts place the optional reference barcode before the configured footer. The preview and physical path share NativePrintRenderer.

Receipt reference barcodes use Code 128 with encoded-length, quiet-zone and two-dot-per-module checks against the printable receipt width. The thermal preview uses 203 DPI where its image-size bound permits. If an optional reference cannot fit at 203 DPI or at the actual printer DPI, the renderer prints its readable reference instead and logs a warning; product-label barcode validation remains independent. Auto-print runs after sale posting, and its notification reports print status separately from the successful sale.
The printing settings screen exposes a document-specific receipt designer for each supported printed type. At desktop widths its compact collapsible controls occupy the right side of the RTL modal and a sticky native PNG preview occupies the left side. The preview zoom control affects only the displayed image. Sample and saved-document sources are labeled separately; saved-document mode uses a searchable list of persisted records. Stale preview images are dimmed until regeneration finishes. Section visibility, order, size, weight, alignment, spacing, and labels are saved with the document profile's template. The existing printer editor and barcode label controls remain separate.

Product Add/Edit forms show a derived Arabic profit margin beside reference purchase and selling prices. Editing purchase and margin calculates selling price to two decimal places; editing purchase and selling price calculates the displayed margin to two places. Zero or absent purchase cost leaves the margin empty. Only the existing purchase/sale price fields are persisted.

## Expiry-only operator workflow

For expiry-tracked stock, purchasing and warehouse receipts show product, quantity, and expiry date. Multiple lines may use different expiry dates. Sales choose product and quantity while the server selects FIFO entries. Linked sales returns select the invoice and quantity; the server restores the latest expiry used by that invoice first. Inventory counts present quantities grouped by expiry date. Internal batch identifiers remain for historical traceability but are not shown in these workflows.

### Suspended POS invoices

The Touch POS cashier starts a new invoice directly when the editor is empty. For unsaved invoice data, a compact RTL dialog offers suspension through the existing sales draft persistence API, explicit discard, or cancellation. The cashier has no separate draft save button. The invoice list button displays a persisted count of visible suspended sales invoices and hides the badge at zero. The count is refreshed on POS entry, suspension, and checkout. Internal `draft` status remains the storage contract.

The sales invoices table shades persisted suspended (`draft`) invoices across their full row with the shared warning surface token. Hover and selected rows retain distinct states. Status badges read `معلقة` for suspended and `مكتملة` for posted invoices; canceled invoices keep `ملغاة`. The shared light and dark theme tokens supply the colors.
## Product and inventory presentation

The product directory retains normal add, search, filter, paging, export, barcode, label, state, and archive actions. Each row shows Edit as the primary action and places its other permitted actions in a keyboard accessible disclosure. The directory toolbar omits Quick Add; the existing quick-add route and implementation remain available elsewhere. Product forms group basic fields, prices, optional capabilities, conversion units, and identifiers in compact RTL sections. Save and Cancel stay in the existing sticky action area.

The warehouse directory groups balance, stock operation, count/transfer, and history controls. Stock movement notes that consist of internal purchase or sale invoice markers show the saved invoice number from the paged movement response and link to the existing document route. Unresolved source records show an Arabic fallback without an invented number. Other notes remain verbatim. Label printing reads the saved template page size; a missing size produces an Arabic configuration error and prevents direct printing.
