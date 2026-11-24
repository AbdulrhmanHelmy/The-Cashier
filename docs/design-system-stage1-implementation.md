# The Cashier 1.1.0 Stage 1 implementation

## Source changes

| Area | Files and behavior |
| --- | --- |
| Tokens and appearance | `frontend/css/tokens.css`, `themes.css`, `base.css`, and `frontend/js/core/theme.js` establish the shared palette, typography and appearance preference. Both HTML entry points load the theme before CSS. `frontend/assets/fonts` contains four local IBM Plex Sans Arabic faces and their license. |
| Shared presentation | `frontend/css/components.css` covers button variants, controls, tables, cards, dialogs, badges, notices, charts and toasts. `frontend/js/components/ui.js` associates shared field errors, improves keyboard traversal in dialogs and adds accessible text with status symbols to toasts. |
| Shell and feature surfaces | `frontend/css/layout.css`, `home.css`, `login.css`, `modules.css` and the home frame assets migrate common surfaces and feature-specific color rules. Reports and printing settings inherit the shared tokens. Global search is layered above quick access. |
| Workflow feedback | `frontend/js/login.js`, `modules/catalog.js` and `modules/sales.js` add validation feedback or status presentation. The sale result uses the server-confirmed invoice and existing receipt action. In the mounted POS, it stays visible across redraws and can be dismissed to continue selling. Invoice cancellation uses Danger treatment and retains reason validation. |
| Desktop and hosting | `src/AlKhayal.Server.Host/AlKhayalServer.cs` serves WOFF2 with its MIME type. WinForms changes are limited to startup, activation and trial palettes plus the initial WebView2 surface. |
| Regression checks | `frontend/tests/design-system-tests.js` checks token resolution, contrast pairs, prepaint script placement and appearance lifecycle. Existing dashboard and delivery checks cover the new token-backed styles. |

The release registry contains 132 entries. There are 76 visible entries with 68 implemented routes rendered in both themes. The Reports Center and its 17 report destinations all rendered. The feature-gated operational shift report required enabling Advanced Shifts on the isolated database. Eight visible registry entries are marked unimplemented and stayed unavailable in the fixture. Sixteen hidden entries without redirects were outside the browser sweep, except contextual product creation. Forty hidden compatibility redirects are listed in the coverage manifest and retain their registry definitions.

Purchase Draft and Posted already use distinct feedback and invoice states. Backup already shows the returned location when the existing host response supplies one. The current permission and report cost-completeness messages retain their semantics. Printing settings use the new shell colors. Paper, barcode and generated receipt previews keep their existing output palette.

Some module-specific compact sizes, product category tints and native Windows control styles remain. The native startup palette follows Windows independently of the persisted browser preference. The browser test used Microsoft Edge on Windows with the real loopback API and a temporary SQLite database. Current WebView2 DPI and physical device qualification remain open. Chromium 109 is the intended minimum for the shared frontend and was not exercised on Windows 7.
