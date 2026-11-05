# Session Insights API contract (v1)

Single source of truth shared by `apps/backend` and `apps/frontend`. JSON is camelCase. Timestamps are ISO-8601 UTC.
Errors are RFC 9457 ProblemDetails. The service listens on `http://127.0.0.1:5180`.

## Security

- Bind to loopback only. Reject (403) any request whose `Host` is not `localhost`/`127.0.0.1`/`[::1]` (with the port), or whose `Origin` (if present) is not one of those hosts.
- Every `/api/**` request needs header `X-Session-Insights-Token: <token>`; missing or wrong is 401. `/health` is the only anonymous endpoint (and returns no session data).
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

## Ownership
- Backend agent owns: `apps/backend/**`, `Directory.*.props`, solution file, `docs/adr/**`, `scripts/**`, `.editorconfig`.
- Frontend agent owns: `apps/frontend/**`, `pnpm-workspace.yaml`, root `package.json` if needed.
- Neither edits this contract without telling the coordinator.
