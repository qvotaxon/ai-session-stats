# ai-session-stats
A tool for visualizing ai sessions, their tool calls and other relevant performance metrics

## Windows releases and updates

Tag a commit `vX.Y.Z` to build and publish a GitHub Release containing `SessionInsights-win-x64.zip` and its `.sha256` file. The archive includes the self-contained Windows x64 application, frontend, and installation/update scripts. It does not require the .NET runtime or SDK on the target machine. Releases are framework-independent; prereleases are not offered to the tray updater.

After installing a release and registering the tray icon, the tray checks for a stable update when it starts and every four hours. Use **Check for updates** to check on demand, then **Update to vX.Y.Z** to apply it. The updater downloads only assets attached to this repository's HTTPS releases, verifies the archive SHA-256 before extraction, and performs the file swap in a detached process. The previous application folder is kept as a backup; user data under `%LOCALAPPDATA%\SessionInsights` remains outside the app folder. If the new server does not pass its health check within 30 seconds, the updater restores the previous app and scheduled tasks. Update status and failures are shown in a tray balloon or dialog and recorded in `update.log`.

Developers updating from a source checkout should continue to use `scripts/update.ps1`.
