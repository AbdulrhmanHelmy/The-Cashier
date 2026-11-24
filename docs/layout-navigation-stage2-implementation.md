# Stage 2 implementation notes

| Files | Responsibility |
| --- | --- |
| `frontend/index.html` | Moves quick actions into the header. Adds the branded sidebar, account footer, dedicated bell and all-workspaces control. |
| `frontend/css/layout.css`, `tokens.css`, `themes.css` | Styles and sizes the shell, tabs, menus and sidebar. Shell tokens isolate the new navigation surfaces from Stage 1 login and home artwork. |
| `frontend/data/screens.json` | Designates the Payment Methods landing and the existing customer creation action. Existing route identifiers, redirects and access metadata are retained. |
| `frontend/js/app.js` | Projects the registry into authorized navigation, ties it to the header and tab strip, migrates sidebar preferences and preserves open views on ordinary navigation. |
| `frontend/js/components/sidebar-flyout.js` | Reuses the filtered tree in a bounded body-level popover with pointer and keyboard dismissal. |
| `frontend/js/modules/catalog.js`, `purchasing.js`, `sales.js` | Supplies dirty indicators for the existing full-page editors and prevents closing while the relevant save or transaction is pending. |
| `frontend/tests/run-tests.js`, `sidebar-flyout-tests.js`, `quickbar-tests.js`, `workspace-tabs-tests.js`, `alerts-tests.js` | Covers registry integrity, compatibility resolution, collapsed navigation, shortcut priority, view reuse and the notification badge. |
| `frontend/tests/stage2-browser.cjs` | Uses Edge to exercise the shipped frontend with synthetic same-origin API responses, capture screenshots and record route navigation evidence. |
| `docs/design.md`, `docs/testing.md` | Link the shell contract and QA commands from the standing project guides. |

The shell replaces the separate shortcut row and old header branding. Existing module and page entry points still use the registry and route-authorization checks. Administration uses the Settings hub. The Alerts badge uses the existing store. The Branches module still exposes its registered entries. The header displays the existing standalone branch label. This deployment has no authenticated active-branch or branch-switch contract.

Sidebar width and group preferences now use a user-suffixed localStorage key. A legacy browser-wide value is migrated to the first authenticated user that loads it. Theme preference follows the established Stage 1 browser setting.

The navigation model keeps its one-tab-per-resolved-route contract. Document links still supply route state to the same contextual editor and invoke its guard. Draft indicators are limited to views that expose a reliable dirty signal. Settings and modal workflows keep their existing validation and dialog protections. Permission or feature revocation can close an inaccessible workspace immediately. Protection against closing the entire native application and arbitrary concurrent document tabs would require separate lifecycle work. Header shortcut pinning uses a stable registry ordering and is deferred.

The frontend remains framework-free and served through the existing OWIN/WebView2 integration. The selected language features, ResizeObserver, container queries, matchMedia and CSS logical properties fit the Chromium 109 target. Native Chromium 109 and legacy Windows qualification remain open. Business APIs, licensing, SQLite migrations, posting and printing paths use the existing implementation.
