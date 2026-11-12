# ADR 0003: Tray self-update from GitHub Releases

## Status

Accepted

## Decision

The Windows tray checks the current application's public `/version` endpoint and compares it with the latest stable GitHub Release tag. It checks at startup, every four hours, and on demand. Prereleases are not considered.

Release-please PR creation and updates also build a manual-install `vX.Y.Z-preview` GitHub prerelease from the PR's manifest version and exact head commit. Stable and preview builds share the packaging workflow. Preview tags and assets are refreshed in place, never marked latest, and published only after the archive, installer, and checksums are uploaded and the PR head is still current. The main-branch release workflow consumes release-please's PR output directly, since PR events created with `GITHUB_TOKEN` do not start workflows; PR events additionally cover direct edits to the release branch.

The preview installer uses the same application identity and installation folder as stable Setup, replacing any existing installation while retaining user data. `/version` preserves `-preview`. The tray still queries only stable releases and permits a preview to update to its matching stable version or a newer stable version, but never offers previews or downgrades.

The installed version is independent of the GitHub check: a release API failure leaves the local version visible and reports the remote error separately. The background runspace treats HTTP errors as terminating so GitHub rate limits are not masked by a later release-validation error.

Tagged `vX.Y.Z` releases contain a self-contained `win-x64` publish, the built frontend, and the installation/update scripts. Self-contained publishing avoids requiring users to install a .NET runtime. The tray launches a copy of the installed updater script from the system temporary directory, so the updater can stop and replace the tray and server files independently.

Release-please creates a draft and its tag first; the release workflow publishes it only after both assets have uploaded. The tray does not offer a newer release until both assets are uploaded. The updater also retries briefly if assets are missing, to handle stale or incomplete release metadata.

The copy runs in an on-demand scheduled task with no time trigger, avoiding a second launch after the initial start. Like the server and tray tasks, it uses `conhost.exe --headless` so PowerShell has no visible console window. The tray hides its icon before starting the updater and restores it if task startup fails. After acquiring the update lock, the updater saves the tray task definition and stops the tray before downloading the release. Task creation and start errors are reported by the tray instead of announcing that the update has started. The updater removes its temporary task when it finishes.

The updater retrieves the latest release metadata from the fixed repository over HTTPS, accepts only the expected release asset names and GitHub release download URLs, and verifies the archive SHA-256 before extraction or execution. This checksum protects against corruption and mismatched assets; it is not an independent publisher signature because the archive and checksum are distributed through the same GitHub release. Release publishing and account security remain part of the trust boundary.

The updater swaps only `%LOCALAPPDATA%\SessionInsights\app`. It leaves the surrounding data directory untouched and retains the replaced app as a timestamped backup. Before the swap it waits for the tray and server processes to exit, and retries directory renames for up to 60 seconds on sharing, lock or access-denied violations. It restarts the server and checks `/health` for up to 180 seconds (Microsoft Defender can hold a new, unsigned release executable on first run); on failure it restores the prior app and scheduled tasks. If an update fails after stopping the tray but before stopping the server, it restores the saved tray task. Successful updates finish silently and restore the tray; failures still display an error dialog. `update.log` is retained outside the app folder, including the failing script location and rollback progress.

Failure cleanup releases the update lock before displaying the error dialog, so a completed rollback cannot block another attempt while the dialog remains open. An updater that cannot acquire the lock does not stop or restart tasks owned by the active updater. Only sharing violations are reported as another update in progress; other lock errors retain their actual cause.

On a health-check timeout, the log also records the last HTTP error, server task state and result, and the process listening on the configured port (if any). Rollback retains a replaced but failed application in an `app-failed-*` directory for inspection instead of deleting it.

## Consequences

- Users can update without a source checkout, pnpm, or a .NET SDK/runtime.
- Users must trust this repository's release maintainers and GitHub's HTTPS delivery; the SHA-256 file alone does not authenticate a publisher.
- Rollback retains disk space for previous application folders.
- `scripts/update.ps1` remains the source-checkout workflow for developers.
