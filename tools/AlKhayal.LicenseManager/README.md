# Owner License Manager

Standalone owner-only HTML/CSS/JavaScript dashboard, native Node.js crypto, built-in SQLite. No npm packages or customer-side dependencies.

Deploy with `install-desktop.ps1` using a verified Node 24 LTS runtime; it resolves the Windows Desktop Known Folder and bundles node.exe. Launch the Desktop shortcut. Private keys/registry are stored in the owner's LocalApplicationData, never under public/ or the installed tool.

The Cashier tool uses the **The Cashier License Manager** folder and shortcut, and stores owner state in `%LOCALAPPDATA%\TheCashierLicenseManager`. The original manager's storage is not migrated. Use `-PackageDirectory` with a new directory to stage the same payload without installing a shortcut. Set `THECASHIER_LICENSE_MANAGER_PACKAGE` to that directory when running package tests with disposable owner state.

See `docs/phase-10/phase-10-1-licensing.md` for the original offline protocol and owner workflow. Its AlKhayal installation paths apply to the original product. Use the authorized Cashier signing configuration for deployment. Never ship this tool to POS customers.
