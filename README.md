# ai-session-stats
A tool for visualizing ai sessions, their tool calls and other relevant performance metrics

## Dashboard and MSDC harness health

The home page shows three recently active sessions, all-history usage totals, recent activity,
and a preview of harness health. **Sessions** now lives at `/sessions`; session-detail URLs are
unchanged. **Harness** (`/harness`) shows local installed-plugin inventories, observed harness
versions, supporting sessions, and attributed tool/hook failure rates.

Installed versions are discovered using read-only `claude plugin list --json` and
`copilot plugin list --json`, never the plugin update command. Historical evidence is separate:
Claude versions are inferred from recorded startup/resume hook paths; Copilot versions identify
individual recorded skill invocations. Missing evidence stays unknown, and a session can contain
multiple versions. No plugin update or access to plugin source is required.

Comparisons keep providers and recorded CLI versions separate. They describe session health,
not proof that the harness caused a failure; project, task, and model differences still matter.
Attribution coverage and supporting sessions remain visible. Lifetime token totals are never
assigned to a harness version. Claude subscription allowance/API credit balances are explicitly
unavailable, rather than estimated from session tokens.

This feature runs locally and does not invoke models or send telemetry. It retains only
allowlisted version evidence and metrics, not raw hook output or installation paths. See
`docs/api-contract.md` for attribution rules and supported CLI metadata shapes.

## Installing

Download `SessionInsights-Setup.exe` from the [latest release](https://github.com/qvotaxon/ai-session-stats/releases/latest) and run it. No admin rights or .NET install are needed. It installs to `%LOCALAPPDATA%\SessionInsights\app`, starts the server and tray icon at every logon, and offers to open http://localhost:5180. The installer is not code-signed yet, so Windows SmartScreen may warn: choose **More info**, then **Run anyway**. Uninstall from Windows **Installed apps**: it removes everything, including the scheduled tasks, tray icon, Start Menu shortcut and all data (token, reports, logs) under `%LOCALAPPDATA%\SessionInsights`. **Quit Session Insights** in the tray menu stops the server and the tray icon; the Start Menu shortcut starts them again.

Updates are applied by the tray, not by the installer, so you only need Setup once.

## Windows releases and updates

Releases use [release-please](https://github.com/googleapis/release-please) and [Conventional Commits](https://www.conventionalcommits.org/) on `main`: `fix:` bumps the patch version, `feat:` the minor version, and `!` / `BREAKING CHANGE:` a breaking bump (minor while below 1.0.0); other types (`chore:`, `docs:`, ...) do not release. After owner approval, merges open or update a release PR that updates `CHANGELOG.md` and `version.txt`; merging that PR tags `vX.Y.Z` and creates a draft GitHub Release, also after approval. Read-only build jobs produce `SessionInsights-win-x64.zip` and its `.sha256` file, then build `SessionInsights-Setup.exe` (Inno Setup, `scripts/installer`) from that zip. A separate owner-approved publishing job uploads all four assets and publishes the release; it never checks out or executes application code. If validation, build, or upload fails, the stable release remains a draft; rerun the failed workflow jobs to complete it. The archive includes the self-contained Windows x64 application, frontend, and installation/update scripts. It does not require the .NET runtime or SDK on the target machine. Releases are framework-independent; prereleases are not offered to the tray updater.

### Release authorization setup

Before enabling releases, create an environment named `release` in **Settings > Environments**. Set **Required reviewers** to only `qvotaxon`, leave **Prevent self-review** unchecked (the owner must be able to approve their own runs), and disable **Allow administrators to bypass configured protection rules**. The workflow checks these settings and fails closed if they are missing or changed. Both release-please and publication require approval; build and installer jobs have no release-write permissions. Release actions are commit-pinned, and Inno Setup is downloaded at a fixed version and checked against a pinned SHA-256 before installation.

Only events originally triggered by `qvotaxon` and reruns initiated by that account enter release automation. A push, PR update, or tag alone does not authorize publication. Bot-triggered release PR events are not authorized; approve the preview run started by the owner's push instead.

Keep `qvotaxon` as the sole account with repository write/admin access, audit installed GitHub Apps and tokens with write access, and protect the account with strong MFA. Environment approval protects these workflows, not every possible API write: another writer could publish directly or add a different workflow, and an administrator could change the protections. In **Settings > Rules > Rulesets**, protect `main` against deletion/force-pushes and restrict updates to the owner-controlled administration role. Never grant other accounts that bypass role. Repository-level settings must be configured separately from these workflow files.

If `RELEASE_PLEASE_TOKEN` is used, prefer a narrowly scoped token stored as a `release` environment secret rather than a repository-wide secret. It only needs access to this repository's contents and pull requests; the default workflow token is sufficient otherwise.

### Publisher signing

Releases are still unsigned. Authenticode signing requires a trusted code-signing certificate or signing service and credentials configured separately. Signing should cover the installer (including its uninstaller), application executables, and shipped PowerShell scripts before packaging and checksum generation. The updater currently trusts GitHub-hosted release assets and their accompanying checksums; those checksums detect corruption but do not independently authenticate the publisher. Adding updater signature verification requires an explicit publisher trust policy. Signing does not guarantee that browser or SmartScreen reputation warnings disappear immediately.

### Manual preview installs

Owner-authorized release-please PR creation or updates also build `vX.Y.Z-preview`, using the upcoming version from the PR's `.release-please-manifest.json`. After owner approval it appears in GitHub Releases marked **Pre-release**, never as the latest stable release. Updates to the PR replace the same preview tag and assets; publication waits for the archive, installer, checksums, and approval. A changed or closed PR causes publication to fail rather than publish an obsolete preview. This also works when release-please uses the default `GITHUB_TOKEN`, which does not trigger other workflows through its PR events.

To try it, open GitHub Releases, select the preview, and manually download and run `SessionInsights-Setup.exe`. It replaces any existing installation in the same folder (no side-by-side install), preserving your data. Preview builds are never offered by **Check for updates**, on either stable or preview installations. An installed preview can update through the tray to the matching stable version or a newer stable version; it will not downgrade to an older stable version. To replace a preview with an older stable version, manually run that stable release's installer.

### Stable updates

After installing a release and registering the tray icon, the tray checks for a stable update when it starts and every four hours. Use **Check for updates** to check on demand, then **Update to vX.Y.Z** to apply it. The updater downloads only assets attached to this repository's HTTPS releases, verifies the archive SHA-256 before extraction, and performs the file swap in a detached process. The previous application folder is kept as a backup; user data under `%LOCALAPPDATA%\SessionInsights` remains outside the app folder. If the new server does not pass its health check within 180 seconds, the updater restores the previous app and scheduled tasks. Update status and failures are shown in a tray balloon or dialog and recorded in `update.log`.

Developers updating from a source checkout should continue to use `scripts/update.ps1`.

## Changelog

Open **Changelog** from Settings or the header **Options** menu, or choose **Open changelog** from the tray context menu. The in-app `/changelog` page bundles the repository's `CHANGELOG.md` at build time, so release history is available offline and matches the installed build. It does not fetch newer release notes from GitHub; updating the app updates the bundled history.

## Debugging

Open the header **Options** menu (gear icon) and choose **Application Log** to view the latest backend log file. Use **Refresh** after reproducing an issue and **Copy log** to copy the displayed diagnostics. The viewer shows at most the last 256 KiB of the latest `session-insights-*.log` file under `%LOCALAPPDATA%\SessionInsights\logs` (or the configured data root); older logs remain on disk. Logs can include local paths and session identifiers, so review them before sharing.

If GitHub's unauthenticated API rate limit is reached, wait for it to reset before checking again. This does not make the locally installed version unavailable. On older releases, dismiss all update error dialogs before retrying: those dialogs can keep the update lock held even after rollback has finished.
