# ADR-0001: Loopback-only access with a local token instead of Entra ID / MSAL

- Status: Accepted
- Date: 2026-01-10

## Context

MSDC doctrine defaults to Microsoft Entra ID (MSAL on the client, JWT bearer on the API) for every web application.
Session Insights is different in kind: a single-user tool that runs as a Windows Service on the developer's own machine,
reads files in that user's profile (`~/.copilot`, `~/.claude`) and has no tenant, no shared data and no remote users.
There is no app registration to point at, the tool must work offline, and the data it exposes is the user's own metrics.

## Decision

- Kestrel binds to `127.0.0.1:5180` only. No other interface is ever opened.
- `Host` and `Origin` are checked against a loopback allow-list (403 otherwise) to defeat DNS rebinding and cross-site calls from a browser.
- `/api/**` requires the header `X-Session-Insights-Token`. The token is 256 random bits, generated on first run into
  `%LOCALAPPDATA%\SessionInsights\token` with an ACL that grants only the current user, compared in constant time.
- The token is injected into the served `index.html` as `<meta name="si-token">`; the SPA sends it on every call.
  A foreign web page cannot read it (same-origin policy plus the Host/Origin checks).
- `/health` is anonymous and returns no data.
- The API exposes metrics only: never prompt or response text, tool arguments or tool output.

## Consequences

- No MSAL, no Entra dependency, works offline.
- Another process running as the same Windows user can read the token file. That is the same trust boundary as the session files themselves.
- If the tool is ever hosted for more than one person, this ADR is superseded and Entra ID authentication is required before that happens.
