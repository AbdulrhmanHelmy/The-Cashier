# The Cashier

<img src="frontend/assets/the-cashier-mark.png" alt="The Cashier official logo" width="128">

**Offline-first Windows point of sale and inventory management.**

The Cashier is an Arabic-first desktop system for retailers who manage sales, stock, purchasing, and day-to-day business operations on Windows. The desktop hosts its web interface locally and stores operational data in SQLite.

## Features

- **Point of sale and invoices** with saved drafts, checkout, returns, cashier shifts, and receipt printing.
- **Product and inventory management** with categories, units, variants, warehouses, stock movements, and valuation.
- **Purchasing** with supplier invoices, returns, and payment tracking.
- **Customer and supplier accounts** with statements, balances, and payments.
- **Business reporting** for sales, purchases, stock, and financial operations, with export and printing where supported.
- **Administration** with user accounts, role and screen permissions, business settings, and local backup and restore.
- **Offline operation** with a local database and device-bound activation.

## Technology

| Area | Implementation |
| --- | --- |
| Windows desktop | C#, Windows Forms, .NET Framework 4.8, x86 desktop executable |
| Business libraries | .NET Standard 2.0 for Core and Application |
| Frontend | HTML, CSS, and vanilla JavaScript hosted in Microsoft Edge WebView2 |
| Local API | ASP.NET Web API 2 with an OWIN self-host |
| Persistence | SQLite, Dapper, and embedded SQL migrations |
| Packaging | Inno Setup 6 |

## Repository layout

```text
TheCashier.slnx       Solution entry point
src/
  AlKhayal.UI/       WinForms desktop and WebView2 host
  AlKhayal.Server.Host/  Local HTTP host
  AlKhayal.Api/      HTTP controllers and API configuration
  AlKhayal.Application/  Business use cases and licensing contracts
  AlKhayal.Core/     Domain models and interfaces
  AlKhayal.Data/     SQLite persistence, migrations, and Windows licensing storage
  AlKhayal.Printing/     Native rendering and printer integration
frontend/           UI, branding assets, and frontend tests
tests/              .NET unit and integration projects
tools/              Owner-only License Manager source
scripts/            Installer definitions and acceptance utilities
docs/               Architecture, API, database, and development notes
```

Internal paths and namespaces retain legacy identifiers.

## Getting started

Use Windows with the .NET Framework 4.8 runtime, a .NET SDK, and the x86 WebView2 Runtime. Restore the project's .NET reference assemblies and dependencies through NuGet. Node.js is needed for the JavaScript tests. Use Node 24 LTS for License Manager development.

From the repository root in PowerShell:

```powershell
dotnet restore src/AlKhayal.UI/AlKhayal.UI.csproj
dotnet build src/AlKhayal.UI/AlKhayal.UI.csproj -c Release --no-restore -m:1
& '.\src\AlKhayal.UI\bin\Release\net48\TheCashier.UI.exe'
```

These project-level commands work with the tested .NET 8 SDK. Open `TheCashier.slnx` with a Visual Studio or .NET toolchain that supports the solution format.

## Configuration

| Setting | Default or purpose |
| --- | --- |
| `THECASHIER_SERVER_URL` | `http://127.0.0.1:5138` in standalone mode |
| `THECASHIER_DATABASE_PATH` | Overrides `%LOCALAPPDATA%\TheCashier\thecashier.db` |
| `THECASHIER_FRONTEND_PATH` | Overrides frontend asset resolution for development |
| `THECASHIER_WEBVIEW2_USER_DATA_FOLDER` | Overrides the Cashier WebView2 profile directory |

The desktop uses Windows application data directories for writable state and keeps machine licensing state under `%PROGRAMDATA%\TheCashier`. Use disposable databases and profiles for development and testing.

## Offline activation

The Cashier uses signed, device-bound licenses scoped to the product. Generate an activation request in the desktop app, obtain the signed license from an authorized issuer, and import it on the requesting device. Activation validation runs locally. The owner-only License Manager source lives in `tools/`.

## Installation and packaging

Use the Release desktop build for development. To prepare an installer candidate, publish the desktop and compile the Inno Setup definition:

```powershell
dotnet publish src/AlKhayal.UI/AlKhayal.UI.csproj -c Release -m:1 -o artifacts/release-1.1.0/stage
ISCC.exe scripts/TheCashier-Production-1.1.0.iss
```

Before compiling, stage the four verified Microsoft offline prerequisite executables listed in the [installer definition](scripts/TheCashier-Production-1.1.0.iss) under `artifacts/release-1.1.0/prerequisites/`. The current definition uses a fixed release-root path. Adjust its `ReleaseRoot` locally when building outside the original checkout location. Generated installers and prerequisite executables are kept outside source control.

## Development and testing

```powershell
node frontend/tests/run-tests.js
dotnet test tests/AlKhayal.UnitTests/AlKhayal.UnitTests.csproj -c Release -m:1
dotnet test tests/AlKhayal.IntegrationTests/AlKhayal.IntegrationTests.csproj -c Release -m:1
node --test tools/AlKhayal.LicenseManager/tests/manager.test.cjs
```

Use the standard Windows temporary directory or another short disposable path for integration tests. Keep test signing material and test records separate from production. See the [testing notes](docs/testing.md) and [acceptance report](THE_CASHIER_FINAL_ACCEPTANCE_REPORT.md).

## Security

Keep production signing keys in the authorized issuance environment. The desktop contains public verification keys. Keep customer databases, activation requests, issued licenses, signing credentials, and personal runtime profiles outside source control. The local API defaults to loopback.

## Release status

**1.1.0 is a release candidate.** The unsigned installer was built. Production-key activation and isolated Windows install, coexistence, upgrade, and uninstall checks are pending. See the [final acceptance report](THE_CASHIER_FINAL_ACCEPTANCE_REPORT.md) for the release decision and evidence.

## License and maintainer

Copyright © The Cashier. This repository contains no project-wide open-source license. Third-party notices are documented separately.

Maintained by [AbdulrhmanHelmy](https://github.com/AbdulrhmanHelmy).
