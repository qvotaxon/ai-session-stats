# ADR 0003: Tray self-update from GitHub Releases

## Status

Accepted

## Decision

The Windows tray checks the current application's public `/version` endpoint and compares it with the latest stable GitHub Release tag. It checks at startup, every four hours, and on demand. Prereleases are not considered.

Tagged `vX.Y.Z` releases contain a self-contained `win-x64` publish, the built frontend, and the installation/update scripts. Self-contained publishing avoids requiring users to install a .NET runtime. The tray launches a copy of the installed updater script from the system temporary directory, so the updater can stop and replace the tray and server files independently.

The copy runs in an on-demand scheduled task with no time trigger, avoiding a second launch after the initial start. Like the server and tray tasks, it uses `conhost.exe --headless` so PowerShell has no visible console window. The tray hides its icon before starting the updater and restores it if task startup fails. After acquiring the update lock, the updater saves the tray task definition and stops the tray before downloading the release. Task creation and start errors are reported by the tray instead of announcing that the update has started. The updater removes its temporary task when it finishes.

The updater retrieves the latest release metadata from the fixed repository over HTTPS, accepts only the expected release asset names and GitHub release download URLs, and verifies the archive SHA-256 before extraction or execution. This checksum protects against corruption and mismatched assets; it is not an independent publisher signature because the archive and checksum are distributed through the same GitHub release. Release publishing and account security remain part of the trust boundary.

The updater swaps only `%LOCALAPPDATA%\SessionInsights\app`. It leaves the surrounding data directory untouched and retains the replaced app as a timestamped backup. Before the swap it waits for the tray and server processes to exit, and retries directory renames for up to 15 seconds on sharing or lock violations. It restarts the server and checks `/health` for up to 30 seconds; on failure it restores the prior app and scheduled tasks. If an update fails after stopping the tray but before stopping the server, it restores the saved tray task. Successful updates finish silently and restore the tray; failures still display an error dialog. `update.log` is retained outside the app folder, including the failing script location and rollback progress.

On a health-check timeout, the log also records the last HTTP error, server task state and result, and the process listening on the configured port (if any). Rollback retains a replaced but failed application in an `app-failed-*` directory for inspection instead of deleting it.

## Consequences

- Users can update without a source checkout, pnpm, or a .NET SDK/runtime.
- Users must trust this repository's release maintainers and GitHub's HTTPS delivery; the SHA-256 file alone does not authenticate a publisher.
- Rollback retains disk space for previous application folders.
- `scripts/update.ps1` remains the source-checkout workflow for developers.
