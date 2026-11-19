# Offline business backups

In **الإعدادات → البيانات والصيانة**, an administrator can create a backup in a chosen local folder, download a listed archive, configure the daily schedule, or inspect and restore an archive. Standalone WebView2 provides the Windows folder picker and opens the configured folder. Browser and LAN sessions enter a path on the server, download copies with the browser, and use the browser file picker to upload a restore file. Paths can point to a connected USB disk. Each creation uses a unique filename and reports the final server path and byte size after publication.

## Included data

The snapshot carries the complete persisted SQLite business database and its migration history. This includes:

- Company, tax, general, activity, feature and product-capability settings.
- Application users, salted password hashes, roles, screen permissions and user audits.
- Products, variants, categories, units, barcodes, identifiers, prices and bundles.
- Warehouses, balances, batches, serial/IMEI identities, cost layers, allocations, stock documents and movements.
- Customers, suppliers, statements, balances, payments, collections and installments.
- Purchases, sales, posted invoices, returns, payment methods, delivery orders and drivers.
- Treasury, expenses, employees, shifts, attendance, advances and payroll.
- Maintenance, alerts, report source records, operation keys and audit history.
- Persisted printer selections, receipt/label designs, embedded PNG logos, and print history.

The repository stores print and label PNGs as base64 inside SQLite-backed template settings. Product images and attachments have no external persisted business storage in the current build. Packaged application branding is installed with the application. Session-only demonstrations and unsaved forms are outside the persistence boundary.

## Archive and local state

Format 1 is a single-disk ZIP with exactly two entries, **manifest.json** and **business.sqlite**. The manifest records the format, application assembly version, newest migration, creation time in UTC, archive GUID, database entry name, uncompressed byte count and SHA-256 of the database. The archive is unencrypted. Anyone with access to it can read the business records and password hashes. SHA-256 detects damage. Archive authorship is unauthenticated.

The engine uses SQLite Online Backup API page batches to capture committed records, including WAL transactions. On private staging storage it empties **TrialState** and **Sessions** with secure deletion and runs VACUUM before packaging. It checks SQLite integrity and foreign keys, flushes a temporary archive next to its destination, then reopens and validates it before a same-directory rename. The ZIP parser bounds the central directory, requires exact entry names and counts, bounds the manifest to 8 KiB and the unpacked database to 2 GiB, streams the upload and extraction, and checks the SHA-256 and byte count. Archive paths never become extraction targets and executable entries have no accepted slot. A crash before publication can leave a temporary file on the selected disk. The history lists finished archives only. The host removes abandoned operation staging beside the database on startup.

The signed permanent activation, device identity and trial records, DPAPI trial file, trial Registry data, desktop saved passwords, WebView profile, live login/CSRF sessions, and license issuer material are excluded. Backup folder configuration, automatic bookkeeping, recovery files and host lock files remain local to the destination installation. Manually created archives are never pruned by automatic retention.

## Daily schedule

Automatic backups start disabled. Choose a writable local Windows folder and turn them on. The default suggested folder is **Backups** next to the server database. The host runs at the first suitable opportunity each local calendar day, including after startup. It checks once a minute, waits at least five minutes after failure, and serializes archive operations. An administrator can turn the schedule off even while the configured disk is unavailable. It retains seven automatic archives for that installation in the current configured folder after a verified success. Moving folders leaves old folders and manual copies available. The screen reports the most recent success and failure separately. The operation audit also records management HTTP outcomes.

## Restore and recovery

Inspect a file first. The preview shows its date, source version, migration and included component. The confirmation grants one restore of the staged file for ten minutes. The administrator must confirm that current business data will be replaced. The server drains existing API requests, returns a maintenance response to new API requests, and rechecks authorization after draining. Before replacement it creates a verified archive under **RestoreRecovery** next to the database.

Restore format 1 requires the same migration set and SQLite schema definitions as the installed database. Install and activate the compatible build, complete normal local administrator setup, and then import. A future release may add explicit forward compatibility. Schema drift in older upgraded databases can also require a matching installation. A backup from a newer or otherwise different schema is rejected before business data changes.

The server validates the staged database again, copies the existing destination **TrialState** row while holding the same Windows mutex used by licensing, and uses SQLite Online Backup API in the reverse direction. SQLite commits all pages through a single fully synchronous destination transaction. On restart, SQLite recovers its rollback journal or WAL, leaving the pre-restore or fully restored database. There is no application-level file swap. The emergency archive is retained if a later step fails. The new database has empty **Sessions**. The host clears the company/general settings cache before reopening API access, and subsequent requests open fresh SQLite connections. The operator signs in again with a user from the restored archive. If that password is unavailable, the Windows operator can stop the host and use the existing local account password-reset command before signing in and, if needed, importing the emergency archive. Browser and device preferences such as the theme stay local.

The permanent-license file, protected trial file and Registry are untouched. A backup from another device carries no usable activation or trial allowance. On a clean Windows installation, normal activation and trial eligibility rules still apply before the restore UI is available. Offline user-level state cannot reliably distinguish a genuinely new installation from a machine whose local artifacts were all removed or whose operating system was replaced. Importing a backup neither checks nor grants trial eligibility.

## Deployment boundaries

Use one AlKhayal host per local SQLite database and run it under the Windows account that owns the destination trial state. The host holds a file lease shared with the account provisioning commands. Restore assumes other tools do not edit that database directly. Restoration requires enough disk for the upload, database staging, validation, emergency archive, and SQLite journal or WAL. Reinstall recovery and corrupt-file handling are tested on disposable databases. After a Release build, run **powershell.exe -NoProfile -File scripts/test-offline-restore-crash.ps1** to terminate a disposable restore while the rollback journal exists and verify recovery and staging cleanup. Windows 7 still requires the supported legacy WebView2 distribution and separate runtime validation.
