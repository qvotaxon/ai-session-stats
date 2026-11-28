# ADR-0001 – Loopback-only access with a local token instead of Entra ID / MSAL

## Status

Accepted (2026-01-10)

## Prologue (Summary)

In the context of Session Insights, a single-user tool that runs as a Scheduled Task on the developer's own machine and reads files in that user's profile, facing MSDC doctrine's default of Microsoft Entra ID for every web application while the tool has no tenant, no remote users and must work offline, we decided for loopback-only access on `127.0.0.1:5180` with Host and Origin checks and a 256-bit local token required on `/api/**`, to achieve an API with no MSAL or Entra dependency that works offline and exposes metrics only, accepting that another process running as the same Windows user can read the token file, and that hosting the tool for more than one person supersedes this ADR.

_Prologue added when this record was converted to the Alexandrian template (#102). It restates the record below and changes no decision._

## Discussion (Context)

MSDC doctrine defaults to Microsoft Entra ID (MSAL on the client, JWT bearer on the API) for every web application.
Session Insights is different in kind: a single-user tool that runs as a Scheduled Task on the developer's own machine,
reads files in that user's profile (`~/.copilot`, `~/.claude`) and has no tenant, no shared data and no remote users.
There is no app registration to point at, the tool must work offline, and the data it exposes is the user's own metrics.

## Solution (Decision)

- Kestrel binds to `127.0.0.1:5180` only. No other interface is ever opened.
- `Host` and `Origin` are checked against a loopback allow-list (403 otherwise) to defeat DNS rebinding and cross-site calls from a browser.
- `/api/**` requires the header `X-Session-Insights-Token`. The token is 256 random bits, generated on first run into
  `%LOCALAPPDATA%\SessionInsights\token` with an ACL that grants only the current user, compared in constant time.
- The token is injected into the served `index.html` as `<meta name="si-token">`; the SPA sends it on every call.
  A foreign web page cannot read it (same-origin policy plus the Host/Origin checks).
- `/health` is anonymous and returns no data.
- The API exposes metrics only: never prompt or response text, tool arguments or tool output. The single, bounded exception is
  the session `recap` on the detail endpoint: a CLI-written summary (max 2000 chars) that the user's own tools already stored.

## Consequences (Results)

- No MSAL, no Entra dependency, works offline.
- Another process running as the same Windows user can read the token file. That is the same trust boundary as the session files themselves.
- If the tool is ever hosted for more than one person, this ADR is superseded and Entra ID authentication is required before that happens.

## Amendment 2026-11-28: Endpoints that return non-metric content, and the anonymous endpoints (#131)

This amendment changes no decision above. It records which endpoints on `main` return content other than metrics, and which endpoints are anonymous, because the Solution names only the session `recap` as an exception and only `/health` as anonymous.

Endpoints under `/api/**` that return non-metric text:

- Report content and download: `GET /api/v1/sessions/{id}/reports/{runId}/content` and `GET /api/v1/sessions/{id}/reports/{runId}/download` return the Markdown of a report that a CLI wrote (`SessionsController`). ADR-0002 covers how report runs are kept out of the session list and that a report run resumes the session in its CLI. No ADR records returning the report text through the API.
- Session title: `GET /api/v1/sessions` and `GET /api/v1/sessions/{id}` return `Title`. When the session has no stored title, it is the text of the user's first message (`CopilotSessionReader`, `ClaudeSessionReader`), and the Evolve analysis repeats it in `Evidence[]`. No ADR records this.
- Evolve analysis: `POST /api/v1/evolve/analysis` returns, per suggested skill, a model-written `Outline` and `Instructions` (`EvolveController`). ADR-0011 covers how the analysis runs.
- Application log: `GET /api/v1/logs` returns the application log text in `Content` (`ApplicationLogController`). No ADR records it.

Endpoints outside `/api/**`, served without authentication:

- `/health`, as in the Solution above.
- `/startup` returns a readiness status value only (`Program.cs`). No ADR records it.
- `/version` returns the application version only. ADR-0005 covers it.
