# ADR 0004: Per-user Windows installer that keeps zip-based self-update

## Status

Accepted

## Context

Installing a release meant extracting the zip, copying `app\` to `%LOCALAPPDATA%\SessionInsights\app`, and running two PowerShell scripts with the right flags. Colleagues should get a normal setup wizard. The tray updater (ADR 0003) must keep working, so the installed layout cannot change.

## Decision

Each release also ships `SessionInsights-Setup.exe` (plus `.sha256`), built with Inno Setup in a Windows job of the release workflow from the already uploaded zip. The release is published only after all four assets exist.

- Per-user, no admin (`PrivilegesRequired=lowest`), fixed folder `%LOCALAPPDATA%\SessionInsights\app`, no directory page. The updater and tray assume this folder.
- Setup copies the same `app\` content as the zip, then runs the existing `install-task.ps1 -PublishedApp` and `install-tray.ps1`. Before copying it runs `uninstall-task.ps1` so an existing install is stopped and re-registered, which makes Setup safe to run over an existing install.
- The uninstaller lives in `%LOCALAPPDATA%\SessionInsights\uninstall`, outside the folder the updater replaces. It runs `uninstall-task.ps1` (shipped inside `app\`), which removes the server, tray and any leftover update tasks and processes plus temporary update scripts, then deletes the app folder, the Start Menu shortcut, and finally the whole `%LOCALAPPDATA%\SessionInsights` data folder (token, reports, logs, backups). Uninstall leaves no traces; the folder holds the running uninstaller, so a detached command removes it after Setup exits.
- The updater still downloads and verifies the zip only. It never runs the installer.
- The installer is unsigned for now, so Windows SmartScreen warns on first run.

## Consequences

- Uninstalling deletes the user's reports and settings. Reinstalling starts clean.
- Reinstalling an older Setup over a newer self-updated app downgrades it until the next update check.
- Setup version in Add/Remove Programs is the version it was built from, not the version the updater later installs.
- Signing the installer later only needs a signing step in the `installer` job.
- MSI/WiX and MSIX were rejected: heavier, admin-oriented or harder to combine with self-updating.
