# The Cashier design system v1

The Stage 1 identity uses Deep Teal for routine primary actions, strong gold for the final checkout action and brand accents, and neutral surfaces for everyday work. The application retains its RTL navigation and local OWIN WebView2 architecture.

## Token sources

- [tokens.css](../frontend/css/tokens.css) defines the primitive palette, the type scale, 4px-based spacing, dimensions, radii, focus dimensions, motion, and layer levels. Existing feature aliases resolve against the semantic theme so older renderers participate in theme changes.
- [themes.css](../frontend/css/themes.css) defines the Light and Dark roles for backgrounds, text, borders, primary and gold actions, status, disabled controls, focus and sidebar. Dark uses neutral charcoal for the app, standard surfaces, and the raised tier shared by subtle and elevated surfaces. Drawers and dialogs inherit the same document theme.
- [theme.js](../frontend/js/core/theme.js) resolves Light, Dark and System before the stylesheets load. It stores the existing `alkhayal_theme` preference in browser profile storage, updates open same-origin tabs, and responds to System appearance changes. WinForms startup colors follow Windows before the embedded browser reads its saved preference.

| Role | Light | Dark |
| --- | --- | --- |
| App background | `#F6FAF9` | `#151719` |
| Surface | `#FFFFFF` | `#202426` |
| Subtle surface | `#F0F5F3` | `#292E30` |
| Elevated surface | `#FFFFFF` | `#292E30` |
| Subtle border | `#D9E7E3` | `#3B4244` |
| Primary text | `#1D3030` | `#F2F7F6` |
| Primary action | `#115E59` with white ink | `#14B8A6` with `#101A1B` ink |
| Gold action | `#F5B700` | `#FFCC33` |
| Gold action text | `#172926` | `#172926` |
| Subtle selection | `#DFF2EC` | `#264642` |

IBM Plex Sans Arabic weights 400, 500, 600 and 700 are bundled locally as WOFF2 from IBM's `@ibm/plex-sans-arabic@1.1.0` distribution. Redistribution includes the [SIL Open Font License 1.1](../frontend/assets/fonts/license.txt). The fallback sequence is Segoe UI, Tahoma and sans-serif. Caption, body, large body, section, page, and KPI sizes are 12, 14, 16, 18, 24 and 30px respectively. Compact, body and heading line heights are 1.45, 1.6 and 1.35. Shared tables use tabular numerals. Apply `bdi dir="ltr"` or the existing `.ltr` class where an identifier needs directional isolation. Keep the established money and quantity formatting helpers.

## Components and states

| Component | Usage |
| --- | --- |
| `.button.primary` | Routine saves, adds and form submissions. |
| `.button.gold` | An exceptional commercial action, currently Complete Sale. |
| `.button` and `.button.ghost` | Secondary and low-emphasis actions. Keep the action label discoverable at rest. |
| `.button.danger` | An action with material destructive consequences. Keep the record, consequence and safe exit in its confirmation. |
| `.input`, `.select`, `.textarea` | Visible labels, control borders, themed placeholders, inline feedback and distinct read-only and disabled states. Associate an error via `aria-describedby` and mark it with `aria-invalid` when validation runs. |
| `.data-table` | Neutral 44px headers, 48px default rows and horizontal separators. Use a bounded wrapper for wide tables and keep the active row indicated by more than tint when selection applies. |
| `.card`, `.badge`, `.notice`, toast | Use shared surfaces, readable message text, and status icons where appropriate. Keep domain labels such as Draft and Posted distinct from success and error messages. |
| Modal | Use shared title and action regions. Focus stays among visible controls and returns to the opener when it is still mounted. Long content scrolls within the dialog body. |

Use the default 8px radius for buttons and form controls. Standard controls have a 40px baseline and POS checkout uses 56px. Provide a visible keyboard focus indicator and retain native semantics. Set both a disabled state and a clear loading label when an existing request is pending. Prefer reduced motion and keep forced-colors system outlines. Receipt and barcode output previews retain their paper colors. Consult [Stage 1 QA](design-system-stage1-qa.md) for measured contrast and remaining verification.
