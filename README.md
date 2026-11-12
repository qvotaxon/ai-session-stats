# ai-session-stats
A tool for visualizing ai sessions, their tool calls and other relevant performance metrics

## Installing

Download `SessionInsights-Setup.exe` from the [latest release](https://github.com/qvotaxon/ai-session-stats/releases/latest) and run it. No admin rights or .NET install are needed. It installs to `%LOCALAPPDATA%\SessionInsights\app`, starts the server and tray icon at every logon, and offers to open http://localhost:5180. The installer is not code-signed yet, so Windows SmartScreen may warn: choose **More info**, then **Run anyway**. Uninstall from Windows **Installed apps**: it removes everything, including the scheduled tasks, tray icon, Start Menu shortcut and all data (token, reports, logs) under `%LOCALAPPDATA%\SessionInsights`. **Quit Session Insights** in the tray menu stops the server and the tray icon; the Start Menu shortcut starts them again.

Updates are applied by the tray, not by the installer, so you only need Setup once.

## Windows releases and updates

Releases are automated with [release-please](https://github.com/googleapis/release-please) from [Conventional Commits](https://www.conventionalcommits.org/) on `main`: `fix:` bumps the patch version, `feat:` the minor version, and `!` / `BREAKING CHANGE:` a breaking bump (minor while below 1.0.0); other types (`chore:`, `docs:`, ...) do not release. Merges open or update a release PR that updates `CHANGELOG.md` and `version.txt`; merging that PR tags `vX.Y.Z` and creates a draft GitHub Release. The workflow builds and uploads `SessionInsights-win-x64.zip` and its `.sha256` file, then builds `SessionInsights-Setup.exe` (Inno Setup, `scripts/installer`) from that zip, before publishing the release. If validation, build, or upload fails, the release remains a draft; rerun the failed workflow jobs to complete it. The archive includes the self-contained Windows x64 application, frontend, and installation/update scripts. It does not require the .NET runtime or SDK on the target machine. Releases are framework-independent; prereleases are not offered to the tray updater.

### Manual preview installs

Creating or updating the release-please PR also builds `vX.Y.Z-preview`, using the upcoming version from the PR's `.release-please-manifest.json`. It appears in GitHub Releases marked **Pre-release**, never as the latest stable release. Updates to the PR replace the same preview tag and assets; publication waits for the archive, installer, and checksums to finish. This also works when release-please uses the default `GITHUB_TOKEN`, which does not trigger other workflows through its PR events.

To try it, open GitHub Releases, select the preview, and manually download and run `SessionInsights-Setup.exe`. It replaces any existing installation in the same folder (no side-by-side install), preserving your data. Preview builds are never offered by **Check for updates**, on either stable or preview installations. An installed preview can update through the tray to the matching stable version or a newer stable version; it will not downgrade to an older stable version. To replace a preview with an older stable version, manually run that stable release's installer.

### Stable updates

After installing a release and registering the tray icon, the tray checks for a stable update when it starts and every four hours. Use **Check for updates** to check on demand, then **Update to vX.Y.Z** to apply it. The updater downloads only assets attached to this repository's HTTPS releases, verifies the archive SHA-256 before extraction, and performs the file swap in a detached process. The previous application folder is kept as a backup; user data under `%LOCALAPPDATA%\SessionInsights` remains outside the app folder. If the new server does not pass its health check within 180 seconds, the updater restores the previous app and scheduled tasks. Update status and failures are shown in a tray balloon or dialog and recorded in `update.log`.

Developers updating from a source checkout should continue to use `scripts/update.ps1`.

## Changelog

Open **Changelog** from Settings or the header **Options** menu, or choose **Open changelog** from the tray context menu. The in-app `/changelog` page bundles the repository's `CHANGELOG.md` at build time, so release history is available offline and matches the installed build. It does not fetch newer release notes from GitHub; updating the app updates the bundled history.

## Debugging

Open the header **Options** menu (gear icon) and choose **Application Log** to view the latest backend log file. Use **Refresh** after reproducing an issue and **Copy log** to copy the displayed diagnostics. The viewer shows at most the last 256 KiB of the latest `session-insights-*.log` file under `%LOCALAPPDATA%\SessionInsights\logs` (or the configured data root); older logs remain on disk. Logs can include local paths and session identifiers, so review them before sharing.

If GitHub's unauthenticated API rate limit is reached, wait for it to reset before checking again. This does not make the locally installed version unavailable. On older releases, dismiss all update error dialogs before retrying: those dialogs can keep the update lock held even after rollback has finished.
