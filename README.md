# ai-session-stats
A tool for visualizing ai sessions, their tool calls and other relevant performance metrics

## Installing

Download `SessionInsights-Setup.exe` from the [latest release](https://github.com/qvotaxon/ai-session-stats/releases/latest) and run it. No admin rights or .NET install are needed. It installs to `%LOCALAPPDATA%\SessionInsightspp`, starts the server and tray icon at every logon, and offers to open http://localhost:5180. The installer is not code-signed yet, so Windows SmartScreen may warn: choose **More info**, then **Run anyway**. Uninstall from Windows **Installed apps**; your data under `%LOCALAPPDATA%\SessionInsights` is kept.

Updates are applied by the tray, not by the installer, so you only need Setup once.

## Windows releases and updates

Releases are automated with [release-please](https://github.com/googleapis/release-please) from [Conventional Commits](https://www.conventionalcommits.org/) on `main`: `fix:` bumps the patch version, `feat:` the minor version, and `!` / `BREAKING CHANGE:` a breaking bump (minor while below 1.0.0); other types (`chore:`, `docs:`, ...) do not release. Merges open or update a release PR that updates `CHANGELOG.md` and `version.txt`; merging that PR tags `vX.Y.Z` and creates a draft GitHub Release. The workflow builds and uploads `SessionInsights-win-x64.zip` and its `.sha256` file, then builds `SessionInsights-Setup.exe` (Inno Setup, `scripts/installer`) from that zip, before publishing the release. If validation, build, or upload fails, the release remains a draft; rerun the failed workflow jobs to complete it. The archive includes the self-contained Windows x64 application, frontend, and installation/update scripts. It does not require the .NET runtime or SDK on the target machine. Releases are framework-independent; prereleases are not offered to the tray updater.

After installing a release and registering the tray icon, the tray checks for a stable update when it starts and every four hours. Use **Check for updates** to check on demand, then **Update to vX.Y.Z** to apply it. The updater downloads only assets attached to this repository's HTTPS releases, verifies the archive SHA-256 before extraction, and performs the file swap in a detached process. The previous application folder is kept as a backup; user data under `%LOCALAPPDATA%\SessionInsights` remains outside the app folder. If the new server does not pass its health check within 180 seconds, the updater restores the previous app and scheduled tasks. Update status and failures are shown in a tray balloon or dialog and recorded in `update.log`.

Developers updating from a source checkout should continue to use `scripts/update.ps1`.

If GitHub's unauthenticated API rate limit is reached, wait for it to reset before checking again. This does not make the locally installed version unavailable. On older releases, dismiss all update error dialogs before retrying: those dialogs can keep the update lock held even after rollback has finished.
