# The Cashier rebrand report

## Clone and independence

- Source: the private upstream repository.
- Destination: `E:\The Cashier`.
- The destination was verified empty before cloning.
- The `origin` remote was removed immediately after cloning. `git remote -v` is empty.
- The original repository was not modified.

## Visible branding changes

- Rebranded the browser title, login, top bar, sidebar, dashboard, breadcrumbs, window title, splash, activation/trial dialogs, startup failure UI, exit confirmation, print previews, print job names, label jobs, owner license-manager UI, and user-facing printer settings.
- Added `TheCashier.slnx` as the independent solution entry point while preserving internal project paths and namespaces.
- The desktop output is `TheCashier.UI.exe` with Product/Company `The Cashier`, version `1.1.0`, and manifest identity `TheCashier.POS`.
- Added the independent production installer `scripts/TheCashier-Production-1.1.0.iss` with AppId `{12CF3562-C3F5-42B2-B5C4-48D2B9F3A8E1}`, install directory/shortcuts/uninstaller name `The Cashier`, and setup output `TheCashier_Setup_1.1.0.exe`.
- Existing UI layout, themes, colors, spacing, controls, typography, and business behavior were not redesigned.

## Official assets

Source of truth: `E:\Navy and Gold Cashier Badge.png` (1254 x 1254).

- `frontend/assets/the-cashier-logo-reference.png`: untouched full-resolution source copy.
- `frontend/assets/the-cashier-mark.png`: 256 x 256 high-quality proportional derivative.
- `frontend/assets/the-cashier-wordmark.png`: 720 x 230 transparent padded derivative containing the complete uncropped badge.
- `src/AlKhayal.UI/Assets/the-cashier.ico`: multi-image Windows icon containing 16, 24, 32, 48, 64, 128, and 256 pixel entries.
- Old logo/wordmark/icon assets were removed from this independent clone.

The conversions are deterministic resize/padding operations; the badge design was not regenerated, recolored, stretched, or cropped.

## Compatibility identifiers intentionally preserved

The following remain `AlKhayal` internally because changing them would risk license invalidation, database/profile loss, broken cookies/bridges, serialized-state incompatibility, or widespread namespace-only churn:

- C# namespaces, internal assembly/project names, JavaScript globals, host-object bridge names, and session/local-storage keys.
- License wire product `AlKhayalPOS`, `ALKREQ`/`ALKLIC` formats, signing bytes, trusted keys, hardware-binding salt, license paths, trial entropy, trial mutex, trial registry/file/security paths, and owner-registry format.
- Existing `%LOCALAPPDATA%\AlKhayal`, `%PROGRAMDATA%\AlKhayal`, database filename, backup/profile/credential paths, and `ALKHAYAL_*` environment variables.

This preserves existing activation, database, user data, backups, and operational compatibility. The installer and application binaries coexist independently, but both products intentionally share legacy business/licensing data stores. Running them concurrently against the same SQLite database is not supported.

Historical reports/docs, old installer scripts, Git history, and test compatibility evidence retain their historical names. They are not shipped as visible application branding by the new production installer.

## Verification

- Production restore: passed for the desktop project and all transitive production projects.
- Release build: passed, 0 warnings, 0 errors. Output: `src/AlKhayal.UI/bin/Release/net48/TheCashier.UI.exe`.
- Frontend regression suite: passed.
- .NET unit suite: 319 passed, 0 failed.
- .NET integration suite: 493 passed, 0 failed (9 minutes 8 seconds).
- License-manager core suite: 7 passed, 0 failed.
- Packaged license-manager test: not a source regression test; it timed out against the separately installed Desktop package because that external package was not rebuilt/reinstalled from this clone.
- Full `.slnx` command: unavailable under installed SDK 8.0.428 (`MSB4068`, `<Solution>` unsupported). Project-level production build and both test-project builds succeeded.
- Installer compilation/runtime install was not executed because the release prerequisite bundle and Inno Setup staging tree were not present under `artifacts/release-1.1.0`.

## Build and run

```powershell
Set-Location 'E:\The Cashier'
dotnet restore src\AlKhayal.UI\AlKhayal.UI.csproj
dotnet build src\AlKhayal.UI\AlKhayal.UI.csproj -c Release --no-restore -m:1
& '.\src\AlKhayal.UI\bin\Release\net48\TheCashier.UI.exe'
```

Tests:

```powershell
node frontend\tests\run-tests.js
dotnet test tests\AlKhayal.UnitTests\AlKhayal.UnitTests.csproj -c Release -m:1
dotnet test tests\AlKhayal.IntegrationTests\AlKhayal.IntegrationTests.csproj -c Release -m:1
node --test tools\AlKhayal.LicenseManager\tests\manager.test.cjs
```

Installer preparation:

1. Publish the UI project into `E:\The Cashier\artifacts\release-1.1.0\stage`.
2. Place the four existing offline prerequisites in `E:\The Cashier\artifacts\release-1.1.0\prerequisites`.
3. Compile `scripts\TheCashier-Production-1.1.0.iss` with Inno Setup 6.
