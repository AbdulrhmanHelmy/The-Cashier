# Installer update 1.0.10

Built from the current working tree on 2026-11-17. The installer source is `scripts/AlKhayal-Update.iss`; the generated binary is `D:\AlKhayal-Update-1.0.10\AlKhayal-Production-Setup-1.0.10.exe`.

The Inno script uses the AppId recorded by the earlier external release recipe, installs binaries and frontend into Program Files, and does not package a SQLite database or license file. Existing `%LOCALAPPDATA%\AlKhayal` data and `%PROGRAMDATA%\AlKhayal` activation are outside the install target. It uses Inno's previous install directory and task settings for an update when the AppId matches.

Validation: UI Release build succeeded with zero warnings/errors; frontend tests passed; 314 unit tests passed; Inno 6.7.3 compilation succeeded. Selected integration tests could not build because the .NET Framework 4.8 reference assemblies were unavailable to that project invocation. The original 1.0.9 binary was not installed or run on a disposable machine, so its actual AppId, prerequisite bundle, upgrade behavior, and preservation of live data remain unverified. This 6 MB installer does not embed the approximately 439 MB of prerequisite payloads present in the old setup. Windows 7/WebView2 runtime compatibility and full fresh installation remain unverified.

Do not distribute as a verified drop-in replacement for 1.0.9 until a clean VM upgrade confirms the old AppId, application path, database/license preservation, runtime prerequisites, startup, and uninstall registration. A clean VM fresh install must also confirm .NET Framework 4.8 and WebView2 provisioning.

## Full wrapper

`scripts/AlKhayal-Full-Setup-1.0.10.iss` bundles the exact 1.0.9 executable and the new 1.0.10 installer. It detects an existing AlKhayal UI executable through uninstall registration or the default install path. On a machine with no detected installation it runs the original 1.0.9 setup first, so its original prerequisites are available, then runs 1.0.10. On a detected installation it runs only 1.0.10. The wrapper does not create a separate uninstall entry. This is a packaging bridge, not a verified migration: existing installations at an unregistered nondefault path may not be detected, and the original setup's UI/exit behavior and its AppId have not been validated in a disposable VM. A real upgrade and fresh-install acceptance test are required before customer rollout.
