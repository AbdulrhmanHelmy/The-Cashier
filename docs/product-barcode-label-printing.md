# Product barcode label printing

## Simple printer assignment (November 2026)

The main **تعيين الطابعات** section contains **تعيين طابعة ملصقات باركود** and **مقاس الاستيكر**. The size choices are 35×25, 40×30, 50×30, 60×40 mm, or validated custom dimensions. Saving uses the persisted label assignment API and regenerates the base template for that stock. The product directory, barcode manager, product editor, and batch view use the default assignment.

## Label content designer

The company name in barcode previews and print jobs comes from the saved company profile. If the business name is blank, the label uses **The Cashier**. Settings preview uses sample values for product fields.

Under **تخصيص ملصق الباركود**, add optional content, move it above or below the mandatory barcode, and choose visibility, alignment and font size. The native PNG shows a sample product. An unsaved sticker-size change is previewed with the same margin calculation as printer assignment. Save the assignment first if the printer or sticker size changes, then save the design. The saved design follows the stock when the assignment is changed. **استعادة التصميم الأصلي** followed by **حفظ تصميم الملصق** clears customization and restores the base label for the saved assignment.

The ordered design is saved with the versioned `LabelConfiguration` JSON. Product preview and job submission apply it to the existing stock and margins using the same server layout calculation. An absent or unreadable design falls back to the saved base template. Jobs use the selected barcode ID to load the current barcode, symbology, name, store name and sale-unit price from SQLite. The same native renderer lays out preview and print; the selected driver can still change dot rounding and text rasterization relative to the 300 DPI preview. Blank optional fields render without text and preserve their configured space.

The selected application dimensions are distinct from the driver's active stock. A matching registered paper form is preferred; otherwise the driver must accept a custom form and return matching DEVMODE dimensions. The backend checks the resulting physical page, resolution, and printable area before submitting any label. A mismatch is an actionable error, never permission to center a small label on a larger page. Existing receipt printer assignments are separate.

The local XP-233B driver advertises a named `35x25` stock, but its .NET paper record and named DEVMODE resolve to approximately 37.6×24.9 mm. The job scoped custom-size path validates at 35×25 mm and does not change Windows defaults. An older saved label configuration still describes a 37.592×24.892 mm page; it must be replaced with 35×25 mm through the new UI before a physical test. Static validation and a Windows queue acceptance do not prove physical alignment or barcode scanning.

The product directory action **طباعة باركود** opens a compact print dialog. A single eligible saved barcode is selected automatically; multiple eligible barcodes allow a unit/variant choice. The dialog displays the native PNG for one saved label on the first page, the saved unit price, quantity (default 1), assigned printer and saved paper dimensions. Changing the selected barcode reloads the preview. **طباعة** is enabled once the image loads and printer validation succeeds. Submission uses the previewed profile and template IDs and requests the exact quantity through the existing native backend. Barcode management remains a separate action. The separate batch screen remains available for several products.

Label profiles, templates, dimensions, gaps, margins, and printer assignment are stored in `LabelConfiguration`. The dedicated assignment panel reads installed printers, active DEVMODE paper dimensions, supported forms, DPI, printable area and status through the existing printer service. Saving an assignment creates an automatically sized label template, stores the selected form name, and sets profile copies to one. A one-time manual width/height override is available if the driver's dimensions do not match the physical media; the driver must accept that custom size. A label job reads the current saved barcode, unit, and price from SQLite. Printing does not write inventory, sales, or price records. Batch profiles can multiply label quantities in application layout; Product Directory sends `exactQuantity` so its requested count is exact. Windows always receives one copy of each rendered page.

The preview uses the native label renderer at 300 DPI. The actual driver DPI and printable area are checked before submitting a print job; a preview alone does not prove a particular printer or media can print it. Windows acceptance means submitted to the spooler, not that paper emerged or that a scanner can read it. A printer driver must expose the requested stock size or accept the custom form. The XP-80C receipt path is separate.

The base label centers the product name even when an older saved template used right alignment. Custom designs use the chosen alignment. Arabic direction and long-name fitting and truncation are shared with the base label. The 35×25 mm base preview has pixel-level centering coverage for Arabic, English, and mixed long names.

## Manual target-printer check

1. Restart the app to load the new build. In **الإعدادات ← الطابعات والطباعة ← تعيين طابعة ملصقات باركود**, choose `Xprinter XP-233B`. Confirm that the Windows driver form matches the labels physically loaded, then save. If the media differs, choose its registered form or open the one-time manual override.
2. In **دليل المنتجات**, choose an active product with a saved barcode, then **طباعة باركود**. Choose a unit only when more than one saved barcode is eligible.
3. Request exactly one label and inspect the physical output. Scan it in POS and verify the product, unit, and saved price.
4. Only after that succeeds, test multiple labels and a mixed-product batch. Compare physical counts with requested counts and confirm printer/media alignment.

Receipt paper on an XP-80C can exercise a compatible driver path but does not establish adhesive-label printer compatibility. Windows 7 and Chromium 109 need separate device qualification.

## Local diagnostic, 2026-11-12

Read-only Windows diagnostics found `Xprinter XP-233B` on `USB003`, status `Normal`, driver `Xprinter XP-233B`, reported resolution 203 × 203 DPI. Its **active DEVMODE** selects `USER`, portrait, **78.7 × 101.6 mm** at 203 × 203 DPI. The active Windows print ticket reports `USER` as **76.2 × 101.6 mm**. The driver's enumerated forms report `2 x 4` (53.34 × 101.6 mm) and `USER` (78.74 × 101.6 mm); the difference between the active DEVMODE and print ticket is a driver reporting inconsistency. The printable width is about 75.07 mm, beginning about 1.25 mm from the page edge; the automatic template reserves that area. The built-in 50 × 30 mm label template is not an advertised form. Windows cannot determine which adhesive labels are physically loaded. The running desktop process holds the exclusive host lock for `%LOCALAPPDATA%/AlKhayal/alkhayal.db`, matching its default `HostOptions` path. A read-only query found no `LabelConfiguration` row there, so no barcode printer is assigned in that active database. No printer setting, database row or physical print job was changed during diagnostics.
