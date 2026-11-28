# ADR-0004 – Per-user Windows installer that keeps zip-based self-update

## Status

Accepted

## Prologue (Summary)

In the context of installing a release, which meant extracting the zip, copying `app\` and running two PowerShell scripts with the right flags, facing the need for a normal setup wizard while the tray updater (the tray self-update ADR) keeps working, so the installed layout cannot change, we decided for a per-user, no-admin `SessionInsights-Setup.exe`, built with a pinned Inno Setup, that installs into the fixed `%LOCALAPPDATA%\SessionInsights\app` folder while the updater keeps using only the zip, to achieve a setup wizard for colleagues without changing self-update, accepting that uninstalling deletes the user's reports and settings and that the unsigned installer makes Windows SmartScreen warn on first run.

_Prologue added when this record was converted to the Alexandrian template (#102). It restates the record below and changes no decision._

## Discussion (Context)

Installing a release meant extracting the zip, copying `app\` to `%LOCALAPPDATA%\SessionInsights\app`, and running two PowerShell scripts with the right flags. Colleagues should get a normal setup wizard. The tray updater (ADR 0003) must keep working, so the installed layout cannot change.

## Solution (Decision)

Each release also ships `SessionInsights-Setup.exe` (plus `.sha256`), built with a version- and checksum-pinned Inno Setup in a read-only Windows job from the workflow's archive artifact. A separate publishing job, gated by owner approval in the `release` environment, uploads all four assets before publishing. Build jobs do not receive release-write credentials.

- Per-user, no admin (`PrivilegesRequired=lowest`), fixed folder `%LOCALAPPDATA%\SessionInsights\app`, no directory page. The updater and tray assume this folder.
- Setup copies the same `app\` content as the zip, then runs the existing `install-task.ps1 -PublishedApp` and `install-tray.ps1`. Before copying it runs `uninstall-task.ps1` so an existing install is stopped and re-registered, which makes Setup safe to run over an existing install.
- The uninstaller lives in `%LOCALAPPDATA%\SessionInsights\uninstall`, outside the folder the updater replaces. It runs `uninstall-task.ps1` (shipped inside `app\`), which removes the server, tray and any leftover update tasks and processes plus temporary update scripts, then deletes the app folder, the Start Menu shortcut, and finally the whole `%LOCALAPPDATA%\SessionInsights` data folder (token, reports, logs, backups). Uninstall leaves no traces; the folder holds the running uninstaller, so a detached command removes it after Setup exits.
- The updater still downloads and verifies the zip only. It never runs the installer.
- The installer is unsigned for now, so Windows SmartScreen warns on first run.

## Consequences (Results)

- Uninstalling deletes the user's reports and settings. Reinstalling starts clean.
- Reinstalling an older Setup over a newer self-updated app downgrades it until the next update check.
- Setup version in Add/Remove Programs is the version it was built from, not the version the updater later installs.
- Signing the installer later only needs a signing step in the `installer` job.
- MSI/WiX and MSIX were rejected: heavier, admin-oriented or harder to combine with self-updating.

## Amendment 2026-11-28: The tray updater is ADR-0005 (#131)

This amendment changes no decision above. The Discussion refers to the tray updater as "ADR 0003". That number was renumbered: the tray updater is ADR-0005 (`0005-tray-self-update.md`, #51), and ADR-0003 is now only the continue-in-CLI launch record.
