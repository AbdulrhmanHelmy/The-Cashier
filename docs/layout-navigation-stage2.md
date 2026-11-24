# Stage 2 layout and navigation

The authenticated shell has a right-hand sidebar, one quick-action header, a workspace tab bar, a scrollable page and a compact status footer. The browser and desktop share the same RTL shell. The native caption and system window controls are supplied by WinForms.

## Sidebar

- Expanded width is 272 px. The 64 px rail keeps the authentic mark, expansion control, navigation icons and compact footer controls. The brand panel uses Deep Teal and a Gold accent.
- The navigation scrolls independently between the brand and account footer. The footer carries the authenticated name and role, three appearance modes, the authorized Settings hub and sign out.
- Daily operations groups Home, Sales, Inventory, Purchases and Customers. Business management groups Suppliers, Payment Methods, Treasury, Expenses, Returns, Workforce and Branches. Additional operations groups Installments and Maintenance. Reports and Alerts appear under Reports and Monitoring. Delivery stays within its existing Sales module. Future registry modules without a presentation group appear under Additional Operations.
- A module with one authorized destination opens it directly. Payment Methods declares an overview landing in the registry. Its label opens the overview and its separate chevron controls the expanded submenu. The collapsed flyout also includes the overview.
- The active child opens its parent. At most one other group is held open by manual choice. The collapsed flyout sits on the workspace side of the rail and reuses the filtered links. Enter or Space opens it and focuses the first item. Left Arrow or Down Arrow also enters it. Escape returns focus to the trigger. Pointer hover is optional.
- Sidebar width and manual group are stored per authenticated user. On first use the shell adopts a previous browser-wide preference if present. Without a saved width, the rail is the default at 1080 px and below. Explicit choices survive window resizing and reload.

## Header

The header is normally 64 px high. It contains navigation search, up to four permitted primary actions, More, Back, the notification bell, the configured standalone branch label and the current shift control. The clock remains visible in the status footer alongside the existing local-mode label and version. The shift control uses the workforce API and its existing dialog. At tighter widths the status and elapsed time stay visible while the action caption remains available to assistive technology.

Quick actions follow registry order. Touch POS opens the operational POS workspace and resumes an open cart. Quick Add opens the catalog product form. Purchase opens the purchase editor and resumes any open draft. Add Customer opens the real creation dialog through Customer Management. The remaining warehouse, returns and treasury shortcuts move into More, which adjusts to the width of the workspace. The bell opens the full, permission-filtered Alerts interface and shows the unread count from the existing service.

Search matches Arabic titles, normalized English route names, descriptions and available aliases. It includes authorized creation shortcuts listed by the registry. Settings subsections stay in the Settings hub. Arrow keys highlight results, Enter opens the selection, Escape dismisses the list, and Ctrl+K focuses the search field. F1, F3, F10 and the existing POS shortcuts retain their bindings.

## Workspace

Home stays pinned when authorized. Normal navigation focuses an existing workspace by resolved route. A contextual link supplying route state can replace the existing document only after its view guard allows it. Tabs keep in-memory views, route state, filters and scroll across ordinary switching. The row scrolls horizontally and the list button shows all open workspaces. Buttons have accessible route-specific names. Left and Right Arrow move focus in RTL order. Home and End reach the ends. The tab context menu retains the close-all command.

The selected tab has a Gold underline and uses the current theme surface. Purchase, sales and the full-page catalog editor expose dirty state to the tab. Closing or replacing a guarded tab respects its cancel or pending result. Open tabs and draft inputs are held in memory during the session.

The workspace owns page-level scrolling. Header overflow uses actual available width and local popovers. Narrow workspaces place search and then shift information on their own rows as needed. Light uses a white sidebar and Teal selection tint. Dark uses a charcoal sidebar and Gold state accents. Both inherit the bundled IBM Plex Sans Arabic family and Stage 1 action and focus tokens. System appearance and reduced-motion preferences apply.

See the [route coverage](layout-navigation-stage2-coverage.md), [implementation notes](layout-navigation-stage2-implementation.md) and [QA results](layout-navigation-stage2-qa.md).
