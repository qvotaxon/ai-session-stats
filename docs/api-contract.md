# Session Insights API contract (v1)

Single source of truth shared by `apps/backend` and `apps/frontend`. JSON is camelCase. Timestamps are ISO-8601 UTC.
Errors are RFC 9457 ProblemDetails. The service listens on `http://127.0.0.1:5180`.

## Security

- Bind to loopback only. Reject (403) any request whose `Host` is not `localhost`/`127.0.0.1`/`[::1]` (with the port), or whose `Origin` (if present) is not one of those hosts.
- Every `/api/**` request needs header `X-Session-Insights-Token: <token>`; missing or wrong is 401. `/health` and `/version` are the anonymous endpoints and return no session data.
- Token: random, generated on first run, stored in `%LOCALAPPDATA%\SessionInsights\token` (user-only ACL). The server injects it into the served `index.html` as `<meta name="si-token" content="...">`. In Vite dev, the proxy reads the same file and adds the header.
- No raw prompt, response, tool arguments or tool output is ever returned. Only metrics, names and counts. The session `title` is truncated to 120 chars. The one exception is `recap` (detail only): the CLI's own summary of the session (Claude `away_summary`, Copilot compaction `<overview>`), truncated to 2000 chars, `null` when the CLI never wrote one.

## Report forks must never appear as sessions

A forked Claude session created by a report run is an implementation detail, not a user session. It must never be listed, counted, fetched via `/api/v1/sessions/{id}` (404), or itself be reportable; otherwise every report would spawn another listed session that can spawn more.

Exclusion is layered, all three required:
1. On run start, the backend records the fork's session id in the run metadata (`forkedSessionId`) once it is known (parse it from the CLI's `--output-format json` result `session_id`, or diff the session files created during the run) and adds it to a persisted exclusion set `%LOCALAPPDATA%\SessionInsights\excluded-sessions.json`.
2. Heuristic fallback: a session whose first user message equals the fixed report prompt (`Give me a session report in markdown format`) and which was not started by a user in the interactive client is excluded (for Claude: a session file that is a fork or has the report prompt as its first user message).
3. Race safety: the index ignores any new session file that appears while a report run is active until its origin is resolved by (1) or (2).

Copilot runs resume in place (no new session), and the report turn is excluded from that session's metrics.

## Endpoints

### GET /health
`200 {"status":"Healthy"}`

### GET /version
`200 {"version":"1.2.3"}` (the installed application version, without a `v` prefix or build metadata). Preview installs return the upcoming version with a `-preview` suffix, for example `1.2.3-preview`.

### GET /api/v1/sessions
Query: `page` (default 1), `pageSize` (default 25, max 100), `source` (`copilot`|`claude`, optional), `search` (matches title/project/id, optional), `sort` (`lastActivityAt`|`startedAt`|`totalTokens`|`toolCallCount`|`durationSeconds`, default `lastActivityAt`, so a continued old session moves to the top), `direction` (`asc`|`desc`, default `desc`).

```json
{ "items": [SessionSummary], "page": 1, "pageSize": 25, "totalCount": 137, "totalPages": 6 }
```

SessionSummary:
```json
{
  "id": "string", "source": "copilot|claude", "title": "string", "project": "string|null",
  "startedAt": "iso", "lastActivityAt": "iso", "endedAt": "iso|null", "durationSeconds": 0,
  "models": ["string"], "totalTokens": 0, "toolCallCount": 0, "hookCount": 0,
  "guardCount": 0, "subagentCount": 0, "latestReportStatus": "queued|running|succeeded|failed|cancelled|null"
}
```

### GET /api/v1/sessions/{id}
404 if unknown or excluded. SessionDetail = SessionSummary plus:
```json
{
  "recap": "string|null", "recapAt": "iso|null",
  "tokens": { "input":0,"output":0,"cacheRead":0,"cacheWrite":0,"reasoning":0,"total":0 },
  "tokensByModel": [{ "model":"","requests":0,"input":0,"output":0,"cacheRead":0,"cacheWrite":0,"reasoning":0 }],
  "toolCalls": { "total":0,"succeeded":0,"failed":0,
    "byTool": [{ "name":"","count":0,"failed":0,"avgDurationMs":0,"totalDurationMs":0 }] },
  "hooks": { "total":0,"failed":0,
    "byType": [{ "type":"","count":0,"failed":0,"avgDurationMs":0 }] },
  "guards": { "total":0,"blockedHooks":0,"permissionDenials":0,"hookFailures":0,
    "items": [{ "kind":"blockedHook|permissionDenial|hookFailure","name":"","count":0 }] },
  "subagents": { "total":0, "byType": [{ "name":"","count":0 }] },
  "skills": [{ "name":"","count":0 }],
  "turns": { "user":0, "assistant":0 },
  "api": { "requestCount":0, "totalApiDurationMs":0 },
  "cost": { "premiumRequests": 0, "nanoAiu": 0 },
  "codeChanges": { "linesAdded":0, "linesRemoved":0, "filesModified":0 },
  "timeline": [{ "bucketStart":"iso","toolCalls":0,"hooks":0,"guards":0,"tokens":0 }],
  "notes": ["string"]
}
```
`cost` and `codeChanges` are `null` when the CLI does not expose them. `timeline` has at most 60 equal-width buckets spanning the session. `notes` lists human-readable "not available for this CLI" remarks. Report turns are excluded from all metrics.

SessionDetail also includes `harness`:
```json
{
  "observations": [
    { "at":"iso|null", "kind":"activation|invocation", "version":"string|null",
      "evidence":"hookPath|invocation|unknown|conflicting", "cliVersion":"string|null" }
  ],
  "activity": [
    { "version":"string|null", "cliVersion":"string|null",
      "toolCalls":0, "toolFailures":0, "hooks":0, "hookFailures":0 }
  ]
}
```

Only the MSDC harness (`msdc-harness`, or qualified identity `msdc-harness@msdc-plugins`) is recognized.
Claude observations come from `SessionStart` hook attachments: only a supported version token from a
`msdc-harness/<version>/` path in `stdout` is retained. No hook stdout or paths are returned or persisted
by this feature. This is **indirect evidence**, not a verified plugin activation record. Missing or
conflicting versions reset attribution to unknown. Tool executions must start and finish strictly
within a recorded activation window; same-boundary events, untimed events, and subagent activity
are unattributed. An untimed activation makes all health attribution for that session unknown.

Copilot observations use populated `pluginName`/`pluginVersion` fields on `skill.invoked` and
`skill.invoked_ref` events outside report turns. They identify individual invocations only; they
never establish a loaded-version window. Tool-telemetry copies are not used as fallback without
a verified correlation contract. Missing versions are not filled from a majority version or the
currently installed plugin.

### GET /api/v1/overview
Returns all-history aggregates from **all listable indexed sessions**, not one list page:
```json
{
  "generatedAt":"iso", "totalSessions":0, "recentlyActiveSessions":0,
  "totalTokens":0, "toolCalls":0, "toolFailures":0, "hooks":0, "hookFailures":0,
  "sessionsWithVersionEvidence":0, "attributedHealthEvents":0, "totalHealthEvents":0,
  "recentSessions":["SessionSummary"],
  "harnessVersions":[
    { "source":"claude|copilot", "version":"string", "cliVersion":"string|null",
      "sessions":0, "directInvocations":0, "activations":0,
      "toolCalls":0, "toolFailures":0, "hooks":0, "hookFailures":0,
      "sessionIds":["string"] }
  ]
}
```
`recentSessions` contains at most three summaries ordered by last activity descending.
`recentlyActiveSessions` counts sessions with last activity in the preceding seven days (UTC).
All other totals cover indexed history, not seven-day usage.
Version rows are grouped by provider, harness version, and recorded CLI version. `sessions` can
overlap between version rows when a session spans updates. `sessionIds` links each row to its evidence.
Health metrics are general session tool/hook outcomes, **not plugin-component failures or causal
regression verdicts**. Guard counts are not added to failure totals; existing reader classifications
are preserved, including Claude hook error/blocking-error records classified as failed executions.
A guard block is not itself evidence of a regression. Lifetime tokens are never distributed between harness versions.

### GET /api/v1/harness/installations
Returns one result per provider:
```json
[
  { "source":"claude|copilot", "status":"installed|notInstalled|unavailable|error",
    "checkedAt":"iso",
    "installations":[{ "version":"string", "enabled":true, "scope":"user|project|local|managed|null" }],
    "error":"string|null" }
]
```
`enabled` can be `null` when the CLI does not expose it. Inventory is read-only and cached for
one minute; each invocation has a 15-second timeout. The only command executed is the resolved
CLI's `plugin list --json`; it never installs, updates, or invokes a model. Lists can be a JSON
array or an object with a `plugins` array. Unknown output structures, unsupported versions,
timeouts and failed commands have explicit error status, never a successful empty inventory.
Raw CLI stdout/stderr and installation paths are not returned or logged.

Inventory runs in the application directory, not every session project, and describes only that
CLI/account/scope. It does not backfill session history or prove what an open session loaded.
Configured `ClaudeConfigDir`/`CopilotHome` are passed to their CLIs; otherwise inherited
`CLAUDE_CONFIG_DIR`/`COPILOT_HOME` apply. Session discovery respects those same homes unless
`ClaudeRoot`/`CopilotRoot` explicitly overrides the transcript directory.

### Reports
ReportRun:
```json
{ "id":"string","sessionId":"string","version":1,
  "status":"queued|running|succeeded|failed|cancelled",
  "cli":"copilot|claude","forked":false,"forkedSessionId":"string|null",
  "startedAt":"iso","finishedAt":"iso|null","durationMs":0,"exitCode":0,"error":"string|null" }
```
- `POST /api/v1/sessions/{id}/reports` → `202` ReportRun (new version). `409` if a run for that session is already queued/running. Prompt is fixed server-side: `Give me a session report in markdown format`.
- `GET /api/v1/sessions/{id}/reports` → `200 [ReportRun]`, newest first.
- `GET /api/v1/sessions/{id}/reports/{runId}` → `200 ReportRun`.
- `GET /api/v1/sessions/{id}/reports/{runId}/content` → `200 text/markdown` (404 until succeeded).
- `GET /api/v1/sessions/{id}/reports/{runId}/download` → same, `Content-Disposition: attachment; filename="session-<id>-v<version>.md"`.
- `POST /api/v1/sessions/{id}/reports/{runId}/cancel` → `202 ReportRun` (409 if not running).

Storage: `%LOCALAPPDATA%\SessionInsights\reports\<sessionId>\v<version>.md` and `v<version>.json` (the ReportRun).

### Continue in CLI
- `SessionDetail.canContinue` (bool): the session's working folder still exists.
- `POST /api/v1/sessions/{id}/continue` body `{ "force": false }` → `204`. Opens a terminal in the session's folder and runs `copilot --resume=<id>` or `claude --resume <id>`. The folder and CLI come from the server, never the client.
  - `404` unknown session; `400` id is not a GUID.
  - `409` a report run is active for the session (never forceable).
  - `409` with ProblemDetails extension `"code": "session_active"` when the session had activity in the last 5 minutes; resend with `force: true` to proceed.
  - `422` the working folder no longer exists, or the terminal cannot be started.
- `GET /api/v1/settings` → `200 { "terminal": "wt-pwsh", "terminals": [{ "id":"", "label":"", "available":true }] }`.
- `PUT /api/v1/settings` body `{ "terminal": "<id>" }` → `200` same shape. `400` for an id outside the fixed set: `wt-pwsh`, `wt-powershell`, `wt-cmd`, `pwsh`, `powershell`, `cmd`. Stored in `%LOCALAPPDATA%\SessionInsights\settings.json`. A stored choice that is unavailable falls back to `wt-pwsh` at launch time.

### Application log
- `GET /api/v1/logs` → `200 { "fileName": "session-insights-20261112.log", "content": "...", "truncated": false }`.
- Returns UTF-8 text from the latest backend log file under the configured data root, oldest-to-newest within that file, capped at its last 256 KiB. When truncated, the partial first line is omitted. No client-supplied paths are accepted.
- No log file returns `{ "fileName": null, "content": "", "truncated": false }`. Read failures return ProblemDetails, not an empty log. Responses are marked `Cache-Control: no-store` and require the same API token as all other API endpoints.
- Diagnostic text can contain local paths, session identifiers and exception details, but no raw CLI output is added for report runs. The UI links to this page only from **Options → Application Log**.

## Ownership
- Backend agent owns: `apps/backend/**`, `Directory.*.props`, solution file, `docs/adr/**`, `scripts/**`, `.editorconfig`.
- Frontend agent owns: `apps/frontend/**`, `pnpm-workspace.yaml`, root `package.json` if needed.
- Neither edits this contract without telling the coordinator.
