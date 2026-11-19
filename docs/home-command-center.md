# Branded home landing page

The pinned `dashboard` route remains a navigation landing page inside the existing shell and workspace tab system. Its central gold-edged frame combines the official emblem, original Arabic wordmark, subtitle, and description. Permission-filtered shortcut cards surround it on both sides; the primary sales actions sit at the top of the right side. The former quick-access heading and welcome card are absent. The home route suppresses the shell's generic refresh control and page-staleness check. The home workspace fills its allotted height without adding another scroll area.

## Original brand assets

- `frontend/assets/alkhayal-mark.png` is the project's 256×263 transparent official emblem. The login lockup, top bar, favicon, and WinForms splash also use this file. Home renders it at full opacity with no filter or recoloring and `object-fit: contain`.
- `frontend/assets/alkhayal-logo-reference.png` is the unchanged 1254×1254 original full-logo artwork. `frontend/assets/alkhayal-wordmark.png` is a lossless crop of its Arabic lettering and diacritics, with source pixels preserved. The crop removes the source artwork's large white margins and slogan; its natural aspect ratio is used at every viewport size, with no offset or clipped heading. The central frame uses `frontend/assets/home-frame.svg`, a scalable decorative outline that does not alter either logo asset.
- No separate wordmark SVG, font file, or brand font is included in the project. Secondary interface text uses the existing `--font` stack (Tahoma, Segoe UI, Arial). The WinForms icon is `src/AlKhayal.UI/Assets/alkhayal.ico`.

Shortcut categories select canonical destinations from `screens.json` modules. Every candidate must be registered, navigation-visible, free of a compatibility redirect, and allowed by the shell's effective `canNavigate` check (persisted screen grants and effective features). The searchable all-screens picker uses the same filter and rechecks access at click time. Feature and access change events refresh the mounted home cards. Home actions use `context.navigate`, which activates an existing tab and retains an in-progress Touch POS cart.

The `التنبيهات` card reads `AlKhayalAlerts.count()`, the same unread-count source used by the shell quick-access indicator. Its compact badge updates on `alkhayal:alerts-changed`, hides at zero, and disappears with the shortcut when access is revoked. The card does not load or summarize alert records itself.

The former daily statistics, recent sales, alerts, and summary panels remain absent from home. Their APIs, saved records, and dedicated reports remain. Card and identity dimensions compress with workspace size. The shield stays above the bottom decorative ribbon. Live authenticated WebView2 and Chromium 109 qualification remain deployment checks.
