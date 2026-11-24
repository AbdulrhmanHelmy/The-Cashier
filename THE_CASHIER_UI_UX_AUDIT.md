# The Cashier 1.1.0 UI/UX audit

**Audit date** 19 November 2026 · **Interface** Arabic RTL · **Scope** visual and interaction review of the verified release stage

## Executive summary

The strongest parts of the release are its Arabic-first checkout, consistent navy-and-gold controls, grouped administration, and reports that connect summaries to underlying records. The seeded workflows exposed the main usability risk where operators move between lists, actions and confirmation. Posting feedback is brief, important actions compete with surrounding chrome, and reports and printing use different conventions for presenting output. The home screen prioritizes launching workflows and branding. The reports center contains the operational numbers.

Start with checkout completion, asynchronous feedback, and destructive-action hierarchy. Then improve report readability and compact layouts. Severity below reflects usability impact on the observed workflow. No critical UX issue was confirmed in the exercised paths.

## Environment and method

- Verified build from `artifacts/release-1.1.0-qa-final/stage`. Release desktop SHA-256 `a425d839710d29eae50d9540eee12029f190c0f172d5cb0b22a3639642bf886c`. Release host SHA-256 `7b7b999ef6600d7800b9b29a0985e709ee7c85bb6a2c5529725177750c0eba39`.
- The running release OWIN host served the staged frontend over loopback on a dedicated port. A temporary copy received the existing x64 SQLite interop DLL to run the standalone host on Windows 11 Pro. Browser captures use an isolated Chromium profile, predominantly 1600×1000 at device scale 1, with smaller viewport checks listed in the index. Browser-mode and native evidence are identified separately.
- Fixtures were written through the running release API into a temporary SQLite database. The core dataset contained 22 grocery and household products, five categories, an inactive item, reorder thresholds, six Arabic-named customers, six suppliers, an administrator, a restricted cashier, five posted multi-line purchases, and eight initial cash or credit sales. Later evidence adds a UI-posted purchase and cash sale, two installment sales, two partial sale returns, three expenses, one funded cash account, three employees and attendance events, three maintenance tickets, two synthetic electronic payment instruments with recorded sales, and a synthetic company profile. Captures were taken through real UI routes and controls. API responses and rendered invoice PDFs were used to verify selected outcomes.
- Existing working files and live operator data were outside the audit. Native activation, trial startup and the WebView2 first-run window were exercised inside an offline Windows Sandbox guest. Production issuance, connected payment providers, and physical printing were outside the exercised environment.

## Coverage

The package contains **194 PNGs**. The release registry defines **132 routes**, including 56 hidden entries and 40 compatibility redirects. The index maps **72 registry routes to a screenshot under their own route ID**, with equivalent screens cross-linked where they are available. 68 of 76 non-hidden entries were captured. The 8 remaining non-hidden IDs are unfinished, feature-gated destinations documented individually in the index. All 18 routes in the current reports center, including the overview, have visual evidence. 3 captures show the native WinForms or WebView2 experience.

The visual set covers initial pages, supported filters, form and modal examples, loaded data, empty and loading states, and real business workflow results. Coverage is representative across state families. Every field permutation and hardware-dependent state was not reproduced.

Use the [screenshot index](THE_CASHIER_UI_SCREENSHOT_INDEX.md) to find the default view, populated states, extensions below the fold, context-specific dialogs and remaining coverage gaps for each release registry entry.

## Module review

### Sign-in, home and navigation

Native activation makes the trial path available and labels its five-day duration. The request code takes a large part of the screen and exposes a dense technical block above the task choices. In the native first-run window, the administrator creation action fell below the visible area while the branded panel remained prominent. Leave less fixed space above the form and keep the primary action in view at short desktop heights. The authenticated native home also shows a long hyphenated user name wrapping to three lines and the shift status truncating in the same header. Allow the status text to remain readable when the account label grows. Evidence `001-native-activation-and-trial.png`, `002-native-first-run.png`, and `005-native-webview-authenticated-home.png`.

The first-run account form and sign-in give the primary action a clear visual lead. Server connection feedback and the password visibility control are useful. Failed credentials appear beside the form. In the dark treatment, secondary labels and the segmented appearance control are comparatively subdued. The large hero and central badge have clear brand presence but take space from whichever task the user came back to finish. Home, quick access, tabs, search, and sidebar expose overlapping ways to start a workflow. Keep the frequent launch targets and give the remaining chrome lower visual weight. A compact view should retain direct access to the task currently in progress.

Evidence `010-bootstrap-admin.png`, `011-login.png`, `012-login-invalid-credentials.png`, `100-dashboard.png`, `300-global-search-results.png`, `302-quick-access-more.png`.

### Product catalog and inventory

Arabic product and category search makes the directory useful with actual rows. Active styling, inline price columns, product add, and modal editing distinguish common tasks. Mixed Arabic names and numeric prices remain useful in the wide view. Dense row actions and warehouse modes take scanning time, and secondary dark surfaces have close visual weight. The detailed product form exposes many switches before a new operator has finished entering identity and price. Prioritize the required fields and keep optional tracking grouped. Use a clear message for zero matches and distinguish the saved reorder threshold from on-hand quantity. In warehouse adjustments, retain the warehouse and product context beside consequences and totals.

Evidence `303-catalog-filtered.png`, `304-catalog-filter-no-matches.png`, `305-product-create-form.png`, `308-product-archive-confirm.png`, `309-product-barcode-manager.png`, `412-report-low-stock-qa-records.png`, `442-warehouse-count.png`, `443-warehouse-stock-adjustment.png`.

### POS, invoices, returns and delivery

The touch POS separates product tiles and the cart, presents payment type clearly, and makes the total and gold checkout action easy to locate at the main desktop size. Stock metadata, a shift control, and delivery affordances remain visible. In the tested success state, the cart reset and the interface gave brief feedback. A persistent summary of the invoice number, payment, and next action would make the result easier to verify. A small inventory of equally prominent invoice row links increases the chance of selecting the wrong command. Cancellation does explain consequences and requires a reason, which is worth preserving. Return detail links the original document and settlement. Delivery screens establish status filters and an empty state, but delivery fulfillment was not exercised. Retain a last-sale receipt/reprint action in reach, and separate routine row actions from reversals.

Evidence `321-sale-invoice-detail.png`, `322-pos-three-item-cart.png`, `323-sale-posted-feedback.png`, `436-sales-management-qa-records.png`, `438-sale-cancel-confirm.png`, `439-sale-cancel-reason-invalid.png`, `440-sales-list-number-filter.png`, `441-posted-return-detail.png`, `166-delivery-orders.png`.

### Purchasing

The purchase editor gives invoice inputs, line items, and totals distinct sections. Supplier results include contact context, and the selected party card shows the current amount due. The posted detail separates the invoice identity, item records, and total. Draft saving and posting are visually and conceptually distinct, but the next step becomes discoverable late, after saving. The search hint mixes English key names into the Arabic flow. Align cash/credit wording between invoice editing, detail, and report. Keep the publish action and draft status visible with the total when the table extends below the fold.

Evidence `316-purchase-posted-detail.png`, `317-purchase-supplier-choices.png`, `318-purchase-three-items-and-total.png`, `319-purchase-draft-saved-toast.png`, `434-purchases-list-qa-records.png`.

### Customers and suppliers

Lists show real-looking Arabic names, synthetic contact details and differentiated balances. The create/edit dialogs keep data entry in context. The detail, collection and payment actions are useful but have different labels and groupings across the two party types. Improve discoverability of the primary next step from a balance row, make cross-document references clear, and preserve legibility when contact data and a long Arabic company name share a row. Keep validation adjacent to its field and restore focus to the initiating action on close.

Evidence `131-customers-list.png`, `132-customer-statement.png`, `310-customers-list-create.png`, `311-customers-list-edit.png`, `312-suppliers-list-create.png`, `313-suppliers-list-edit.png`, `420-report-customers-qa-records.png`, `421-report-suppliers-qa-records.png`.

### Treasury, expenses and payments

Cash account cards, dedicated deposit/withdrawal forms, and summary bands make financial states findable. Separate cash and electronic channels are sensible. The deposit and expense forms would benefit from more visible account identity, resulting balance where supported, and a consistent amount hierarchy. Wallet and terminal views were captured before and after adding synthetic records, and the second payment report pass shows transactions for both instruments. Distinguish an eligible-but-unconfigured payment method from a screen that has no matching transaction for the selected period.

Evidence `120-payment-overview.png`, `121-payment-wallets.png`, `122-payment-terminals.png`, `123-payment-history.png`, `545-payment-wallets-with-instruments.png`, `546-payment-terminals-with-instruments.png`, `548-report-payment-methods-with-instruments.png`, `143-account-deposit.png`, `408-report-expenses-qa-records.png`, `429-expenses-list-qa-records.png`, `430-accounts-list-qa-records.png`.

### Workforce, shifts and attendance

The open shift is visible on POS, the workforce list and the shift report. Attendance and cashier shifts are distinct destinations. Seeded attendance records make person, date and event status easier to compare. The shift timer and close action share an attention-grabbing red treatment even in normal operation. Give the shift status, its age, and the destructive or settlement action distinct presentations. Train operators on where to correct an attendance record since the terms for shifts and attendance sit close together in navigation.

Evidence `153-shifts-list.png`, `154-shift-add.png`, `155-shift-reports.png`, `403-report-attendance-qa-records.png`, `422-report-shifts-qa-records.png`, `427-employees-list-qa-records.png`, `428-attendance-qa-records.png`.

### Installments and maintenance

Maintenance intake asks for recognizable device, issue and date fields. The report exposes separate status and cost information, with saved ticket rows. The installment report exposes remaining balances and counts and the payment screen asks for the corresponding customer. Use consistent status text and lead with ticket or contract identifiers. The installment entry description refers to a session-calculated schedule while the report and posted invoices show persisted records. Clarify exactly which action persists the invoice or contract. Surface the next due date and what a user is expected to do with it.

Evidence `162-installment-payment.png`, `163-maintenance-intake.png`, `404-report-maintenance-qa-records.png`, `405-report-installments-qa-records.png`, `431-maintenance-tracking-qa-records.png`, `432-installment-system-qa-records.png`.

### Settings, permissions and data maintenance

The single Settings hub groups responsibilities effectively, and the company profile provides one place for identity data. The user dialog and permissions view expose the distinction between users and grants. Dense grouped grants would be faster to review with selected-count summaries per group, and explicit distinctions between inherited, selected, and unavailable permissions. Backup contains useful run status and migration diagnostics. An archive created successfully outside the configured folder still left the adjacent list empty. Show the new artifact alongside a path label or link to the configured-directory history. Restore gives a strong consequence warning and was cancelled at confirmation.

Evidence `101-settings-program.png`, `314-user-create.png`, `315-cashier-permissions.png`, `325-backup-result.png`, `400-settings-company-qa-records.png`, `444-restore-confirmation.png`.

### Accessibility, RTL and compact windows

Arabic is presented as the primary language. Labels and right-aligned text are readable in the primary desktop composition, and dates, document IDs and numbers are given their own cells. Tables, form copy, currency totals and navigation chrome become progressively denser at smaller sizes. Report headers and filters take vertical space before the first record. Keep identifiers and numeric values directionally isolated, make key status distinguishable by text and shape, and test visible focus after modal close. Muted and link colors on dark surfaces warrant measured contrast checks. The screenshot review alone does not establish WCAG conformance, keyboard completion or screen-reader announcements across all modules.

## Popups, feedback and errors

| Area | Evidence | Assessment |
| --- | --- | --- |
| Authentication | `012-login-invalid-credentials.png` | Error is close to the login action and avoids revealing which credential was wrong. |
| Catalog | `305-product-create-form.png`, `306-product-create-invalid.png`, `308-product-archive-confirm.png`, `309-product-barcode-manager.png` | Archive explains the visibility outcome. Keep field-level errors in view after submit. |
| Party and user management | `310-customers-list-create.png` through `315-cashier-permissions.png` | Modal pattern is consistent. Permission groups are lengthy and benefit from a selection summary and a clear edit/commit boundary. |
| Purchase and checkout | `319-purchase-draft-saved-toast.png`, `323-sale-posted-feedback.png` | A transient toast can be easy to miss when attention is on items, payment, or receipt. Retain the latest committed document in the workspace. |
| Invoice cancellation | `438-sale-cancel-confirm.png`, `439-sale-cancel-reason-invalid.png` | Good reason validation. The dominant gold button is also used for normal positive actions. Use explicit danger styling and keep the safe return action obvious. |
| Backup / restore | `324-backup-destination.png`, `325-backup-result.png`, `444-restore-confirmation.png` | Explicit restore review and completion status. Long filesystem paths and the empty history after a successful out-of-folder backup compete for attention. |
| Native and print | See coverage and print output below. | Printer setup and receipt previews are identified in the index, including their capture method. |

## Reports and print output

The overview gives a quick read of sales and purchases, then lists the available reports by category. Colored accents and totals carry useful emphasis in the dark theme. Filters, overview cards, and the live table compete vertically in the densest reports. Format amounts consistently and place the reporting basis, such as posting date and excluded costs, immediately beside the value it qualifies. Keep headings and active scope visible on longer tables and in an export.

| Report | Screenshot evidence | Visual review |
| --- | --- | --- |
| Overview | [401-report-overview-qa-records.png](UI_AUDIT_SCREENSHOTS/401-report-overview-qa-records.png), [402-report-overview-scroll-1.png](UI_AUDIT_SCREENSHOTS/402-report-overview-scroll-1.png) | Color-coded totals and a categorized report catalog. Keep selected date scope visible while scrolling. |
| Sales | [417-report-sales-qa-records.png](UI_AUDIT_SCREENSHOTS/417-report-sales-qa-records.png), [418-report-sales-scroll-1.png](UI_AUDIT_SCREENSHOTS/418-report-sales-scroll-1.png), [447-report-sales-1366.png](UI_AUDIT_SCREENSHOTS/447-report-sales-1366.png) | Ledger rows and net/return/cancel totals. Table starts lower on smaller screens. |
| Purchases | [419-report-purchases-qa-records.png](UI_AUDIT_SCREENSHOTS/419-report-purchases-qa-records.png) | Invoice links, paid and owed amounts. Give date basis a compact persistent label. |
| Returns | [406-report-returns-qa-records.png](UI_AUDIT_SCREENSHOTS/406-report-returns-qa-records.png) | Saved returns, total and original references. Strengthen the distinction between sale and purchase reversal. |
| Payment methods | [407-report-payment-methods-qa-records.png](UI_AUDIT_SCREENSHOTS/407-report-payment-methods-qa-records.png), [548-report-payment-methods-with-instruments.png](UI_AUDIT_SCREENSHOTS/548-report-payment-methods-with-instruments.png) | Cash and credit aggregates, plus wallet and card transaction rows in the seeded pass. Use explicit scope for collected versus outstanding amounts. |
| Customer balances | [420-report-customers-qa-records.png](UI_AUDIT_SCREENSHOTS/420-report-customers-qa-records.png) | Six parties and balance columns. Long names and running amounts need enough column width. |
| Supplier payables | [421-report-suppliers-qa-records.png](UI_AUDIT_SCREENSHOTS/421-report-suppliers-qa-records.png) | Six parties with payable metrics. Keep settlement and balance columns explicitly named. |
| Shifts | [422-report-shifts-qa-records.png](UI_AUDIT_SCREENSHOTS/422-report-shifts-qa-records.png) | One real open shift and cash evidence. Differentiate open status from variance. |
| Attendance | [403-report-attendance-qa-records.png](UI_AUDIT_SCREENSHOTS/403-report-attendance-qa-records.png) | Three staff records with dates and statuses. Short dates and fixed column widths help comparison. |
| Maintenance | [404-report-maintenance-qa-records.png](UI_AUDIT_SCREENSHOTS/404-report-maintenance-qa-records.png) | Three device records, current state and figures. Keep ticket, device and customer scannable. |
| Installments | [405-report-installments-qa-records.png](UI_AUDIT_SCREENSHOTS/405-report-installments-qa-records.png) | Two posted contracts and balance summaries. Clarify overdue versus expected next payments. |
| Expenses | [408-report-expenses-qa-records.png](UI_AUDIT_SCREENSHOTS/408-report-expenses-qa-records.png) | Three vouchers with category totals. The category summary and line table are useful together. |
| Net profit | [409-report-net-profit-qa-records.png](UI_AUDIT_SCREENSHOTS/409-report-net-profit-qa-records.png), [410-report-net-profit-scroll-1.png](UI_AUDIT_SCREENSHOTS/410-report-net-profit-scroll-1.png), [411-report-net-profit-scroll-2.png](UI_AUDIT_SCREENSHOTS/411-report-net-profit-scroll-2.png) | Incomplete cost coverage visibly qualifies the total. Expose excluded rows next to the aggregate. |
| Low stock | [412-report-low-stock-qa-records.png](UI_AUDIT_SCREENSHOTS/412-report-low-stock-qa-records.png) | On-hand/threshold evidence with a result. Use control labels that differentiate threshold and physical count. |
| Inventory movement | [413-report-inventory-movements-qa-records.png](UI_AUDIT_SCREENSHOTS/413-report-inventory-movements-qa-records.png), [414-report-inventory-movements-scroll-1.png](UI_AUDIT_SCREENSHOTS/414-report-inventory-movements-scroll-1.png), [512-inventory-movements-page-2.png](UI_AUDIT_SCREENSHOTS/512-inventory-movements-page-2.png), [513-inventory-movements-page-2-rows.png](UI_AUDIT_SCREENSHOTS/513-inventory-movements-page-2-rows.png), [514-inventory-movements-page-3.png](UI_AUDIT_SCREENSHOTS/514-inventory-movements-page-3.png), [515-inventory-movements-page-3-rows.png](UI_AUDIT_SCREENSHOTS/515-inventory-movements-page-3-rows.png) | Traceable invoice references. Separate quantity, source and date and preserve headers over scroll and page changes. |
| Inventory valuation | [415-report-inventory-valuation-qa-records.png](UI_AUDIT_SCREENSHOTS/415-report-inventory-valuation-qa-records.png), [416-report-inventory-valuation-scroll-1.png](UI_AUDIT_SCREENSHOTS/416-report-inventory-valuation-scroll-1.png) | Stock and cost totals. Keep valuation method and unknown values visible in print or export. |
| Top products | [423-report-top-products-qa-records.png](UI_AUDIT_SCREENSHOTS/423-report-top-products-qa-records.png), [424-report-top-products-scroll-1.png](UI_AUDIT_SCREENSHOTS/424-report-top-products-scroll-1.png) | Ranked product rows. A compact totals-first view would improve scanning. |
| Product profit | [425-report-product-profit-qa-records.png](UI_AUDIT_SCREENSHOTS/425-report-product-profit-qa-records.png), [426-report-product-profit-scroll-1.png](UI_AUDIT_SCREENSHOTS/426-report-product-profit-scroll-1.png) | Cost, sales and margin rows. Table density and monetary precision merit a separate print layout. |

The printer settings loaded after the enumeration state. The add-printer dialog validates missing paper, and it previewed sample, actual sales and actual purchase receipts. The visible receipt is split by a scrollable preview inside the modal, which makes it harder to inspect both branding and totals at once. A fit-to-page control would make complete visual checking faster. Evidence `530-printers-settings-loaded.png` and `531-printer-add.png` through `535-saved-purchase-receipt-preview.png`.

The three application-generated invoice PDFs were rendered at 150 dpi for visual review. These pages demonstrate the output document, including its Arabic text and totals. They do not establish spooler behavior, paper margins on a physical device, thermal cutting, or completion of a Windows print job. No per-report print control was observed in the captured report pages. Invoice print preview and report output should be treated as separate operator tasks.

## Prioritized findings and recommendations

| Priority | Severity · issue type | Evidence and proposed improvement |
| --- | --- | --- |
| 1 | High · Feedback | Checkout resets the cart after posting. Keep invoice number, total, payment type, and print/reopen beside a persistent success state. `322`, `323`, `436`. |
| 2 | High · Dialogs and error prevention | Routine gold and destructive gold look similar in invoice cancellation. Reserve an explicit danger treatment, keep consequences concise, and emphasize the safe exit. `438`, `439`. |
| 3 | High · Print and feedback | Differentiate loading, missing configuration, and a detected Windows printer. Provide a clear fallback and time-bounded retry when printer enumeration is slow. `128-settings-printers.png`, `437-settings-printers-qa-records.png`. |
| 4 | High · Reporting | Show incomplete cost evidence beside profit totals and define amount scope inline. Keep the aggregate and row basis visible together. `409`, `425`. |
| 5 | High · Accessibility | Review link, focus, placeholder, and disabled-state contrast at normal desktop scale on dark surfaces. Verify complete keyboard workflows for checkout, close shift, invoice cancellation, and restore. `305`, `322`, `438`, `444`. |
| 6 | Medium · Tables | Place view and PDF first, and group reversal and secondary commands. Keep row identity in view when actions or columns overflow. `316`, `436`. |
| 7 | Medium · Navigation | Reduce duplicated emphasis across quick access, home tiles, tabs and sidebar. Make active destination and back behavior clear when tabs accumulate. `100`, `302`, `401`. |
| 8 | Medium · Forms | Keep the purchase total, posting state and next action in one stable location. Match cash/credit wording throughout purchasing and reporting. `318`, `319`, `419`. |
| 9 | Medium · Feedback | Present the completed manual backup with its actual file location and explain the separate saved-folder history. `325`, `444`. |
| 10 | Medium · Reporting and print | Standardize default date scope, display precision, sticky column headings and printed page metadata. Prioritize wide movement and product-profit reports. `413`, `415`, `425`. |
| 11 | Medium · Permissions | Show counts of effective grants and selected groups. Give the operator an explanation and escalation path when screens are unavailable. `315` and restricted-role evidence in the index. |
| 12 | Medium · Localization | Align Arabic wording and mixed-direction identifiers across payments, purchases, account statements and PDFs. Clarify the installment transition from calculation to a saved contract. `316`, `405`, `432`. |
| 13 | Medium · Empty states | Give empty results a relevant next action, such as clearing filters, adding a payment instrument, or choosing an invoice. `304`, `121`, `122`, `166`. |
| 14 | Low · Visual hierarchy | Scale down the central home branding and strengthen task labels. Show operational totals where they are already available in the reports center. `100`, `401`. |
| 15 | Low · Consistency | Use a clearer hierarchy for open-shift status, elapsed time and the close action. `322`, `436`. |
| 16 | Medium · Forms | Keep the native first-run primary action in view at a short desktop height. Simplify the priority of trial and activation paths. `001`, `002`. |
| 17 | Medium · Print preview | Provide a fit-to-page view and single scrolling surface for a full receipt review. `533`, `534`, `535`. |

## Suggested roadmap

1. **Feedback and transaction safety.** Standardize status, validation, success, warning and danger styling. Stabilize checkout completion and posting feedback, with operator-flow verification before visual changes elsewhere.
2. **Navigation and shell.** Adjust home and quick-access prominence, compact viewport behavior, tab affordances and responsive keyboard focus.
3. **Forms and tables.** Review the purchase, catalog and party patterns, context-specific actions, numeric alignment, and focus. Standardize empty and error messages.
4. **Reports and print.** Align scope and precision, preserve document IDs in exports, validate report output, and run a separate thermal and standard-paper check in the desktop shell.
5. **Accessibility and release polish.** Measure contrast, run keyboard and screen-reader scenarios, verify long RTL text, and exercise native dialogs, role transitions and recovery paths on the intended runtime and DPI matrix.

## State coverage gaps

| Area | Outstanding states and reason |
| --- | --- |
| Licensing and desktop shell | The trial was started in a clean disposable guest. Expiry, clock and invalid-storage scenarios require separate controlled guest runs. No production signing key was used. |
| Catalog and inventory | No standalone manufacture or branch workflow surfaced. Barcode label dispatch, scale hardware, multi-warehouse transfer, and barcode-scanner recovery were not exercised. |
| Sales, purchasing and returns | Posted multi-line, cash, credit, installments, partial sales returns and cancellation validation are visible. Live gateway rejections, electronic-refund confirmation, a posted purchase return and all delivery status transitions remain unverified. |
| Parties, treasury and workforce | Main forms, selected states and populated lists were sampled. Partial-payment allocation variants, payroll posting, failed deposits and attendance correction permutations were outside the exercised scenarios. |
| Reports and printing | All current reports were opened. The set includes representative filters and pagination. Print-engine preview and receipt PDFs were inspected. Physical output, printer failure, special paper sizes, and report export variants remain unverified. |
| Session, backup and accessibility | Restricted navigation and archive inspection were exercised. Lease failure, session timeout, restore completion, browser or host crashes, and screen-reader announcements require separate runs. |

## Validation and remaining work

- `node frontend/tests/run-tests.js` completed successfully. The audit exercised the running release and verified PNG signatures and dimensions, evidence-to-index mapping and package contents. No rebuild was necessary.
- Native activation, trial start, administrator setup, and authenticated Windows/WebView2 home are captured in `001`, `002`, and `005`. Permanent activation, exhausted or clock-invalid trial, native session failure, crash recovery, exit behavior and native pickers were not all staged. The disposable guest was used for native evidence.
- Real payment gateway responses, physical printer and cash-drawer behavior, barcode scanner input, signed activation, LAN/offline recovery and Windows 7/Chromium 109 were not qualified. Only an ordinary Windows 11 guest and current browser viewports were used.
- No fixture was added for confirmed electronic refunds, posted delivery completion, physical printer faults, multi-warehouse transfer, protected operations after license expiry, or long multi-page physical receipts. Related screens were captured if surfaced. Delivery status transitions, actual Windows print and file picker, report outputs other than inspected invoice PDFs, and the full user-role matrix remain unverified.
- Some destinations require a selected customer, document, or assigned hardware. The index notes contextual aliases and supported equivalents. Native restore confirmation was not required for the browser fallback to inspect the disposable archive. The confirmed restore itself, cancel or delete of existing records, and physical test printing were outside this visual run.
- Per-route duplicate create/edit, empty, validation, error and recovery variants were not exhaustively exercised. Where capture is absent, the registry and manifest specify whether the route is hidden, redirected, gated, contextual or represented by another workflow. The unfinished profit views are observable placeholders, with persisted profit reports in the reports center.
- Responsive review covered layout snapshots rather than multiple OS DPI levels, high-contrast OS mode, assistive technologies, multi-monitor behavior or a complete keyboard-only test matrix.
