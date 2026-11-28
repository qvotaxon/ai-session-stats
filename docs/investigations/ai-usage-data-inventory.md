# Investigation: inventory of local AI-usage data and its retention

- Status: Complete
- Date: 2026-11-25
- Issue: qvotaxon/ai-session-stats#66
- Epic: qvotaxon/ai-session-stats#41
- Scope: read-only inventory. No capture is implemented here.

## Goal

To show usage over time we need to know, per CLI, exactly which data exists on
the local machine, how long it survives, whether it is available offline, and
what it costs (configuration burden and privacy) to capture. This document is
the inventory and a recommendation on which sources should feed a future
snapshot store.

## CLI versions and environment tested

| Thing | Version / value | How verified |
| --- | --- | --- |
| Claude Code transcript format | `version` field `2.0.77` | Real transcript line in `~/.claude/projects/-home-runner-work-ai-session-stats-ai-session-stats/*.jsonl` on the agent host (2026-11-25). |
| Copilot CLI session state | could not determine | No `~/.copilot/session-state` directory existed on the host, and the Copilot CLI could not be run (`copilot --version` returned nothing). Copilot facts below are taken from the reader source only and are marked accordingly. |
| Session Insights | 1.3.0 | `CHANGELOG.md`, `README.md`. |

The Claude Code transcript format is documented by Anthropic as **internal and
subject to change**, so the field list below is pinned to the tested CLI version
and should be re-verified when the CLI updates.

## Sources read today by Session Insights

| Source | Path (default) | Reader |
| --- | --- | --- |
| Claude transcripts (JSONL) | `%USERPROFILE%\.claude\projects\{project}\{sessionId}.jsonl` (+ `.../{sessionId}/subagents/agent-*.jsonl`) | `apps/backend/src/Infrastructure/Sessions/ClaudeSessionReader.cs` |
| Copilot session state | `%USERPROFILE%\.copilot\session-state\{sessionId}\events.jsonl` (+ optional `workspace.yaml`) | `apps/backend/src/Infrastructure/Sessions/CopilotSessionReader.cs` |
| Claude status-line allowance file | `%LOCALAPPDATA%\SessionInsights\claude-allowance.json` | `apps/backend/src/Infrastructure/Sessions/ClaudeAllowanceReader.cs`, written by `apps/backend/src/Web/claude-statusline.ps1` |
| msdc-harness plugin evidence (derived) | Not a separate file: derived from the `msdc-harness/{version}/` hook path and plugin names in Claude/Copilot session data. `~/.msdc-harness` is **not** read today. | `apps/backend/src/Infrastructure/Sessions/HarnessEvidence.cs` (see qvotaxon/ai-session-stats#71) |

Path resolution lives in `SessionInsightsOptions` (`ResolveClaudeRoot`,
`ResolveCopilotRoot`, `ResolveDataRoot`), honouring `CLAUDE_CONFIG_DIR`,
`COPILOT_HOME`, and `SessionInsights__*` overrides.

---

## Metric × source inventory

Legend for **Scope**: *session* = value is per session; *cumulative* = value
accumulates (e.g. maxed across resumes); *window* = rolling time window.
**Survives restart** = present on disk after the CLI/host restarts.
**Offline** = readable with no network.

### Claude Code transcript JSONL (v2.0.77)

| Metric | Field(s) in transcript | Scope | Survives restart | Offline | Privacy risk | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| Input tokens | `message.usage.input_tokens` | session (per message) | yes | yes | low | per assistant message |
| Output tokens | `message.usage.output_tokens` | session | yes | yes | low | |
| Cache read tokens | `message.usage.cache_read_input_tokens` | session | yes | yes | low | |
| Cache write tokens | `message.usage.cache_creation_input_tokens` (plus `cache_creation.ephemeral_1h_input_tokens` / `ephemeral_5m_input_tokens`) | session | yes | yes | low | ephemeral sub-fields seen in v2.0.77 |
| Reasoning / thinking tokens | `message.usage.output_tokens_details.thinking_tokens` | session | yes | yes | low | read by `ClaudeSessionReader` |
| Model | `message.model` | session | yes | yes | low | `<synthetic>` appears for synthetic/error turns and is excluded from usage |
| Requests | derived: distinct `message.id` values (`ApiRequestCount`) | session | yes | yes | low | no explicit request field. The reader keeps one entry per `message.id` (the one with the largest token total, because streamed lines repeat the id) and skips `<synthetic>` models and `isApiErrorMessage` lines |
| Cost ($) | **none** | — | — | — | — | no cost field in the transcript; see status line |
| Premium requests / nano-AIU | **none** | — | — | — | — | not exposed by Claude Code; `ClaudeSessionReader` states this explicitly |
| Lines added/removed | **none** | — | — | — | — | not exposed by Claude Code |
| `gitBranch` | `gitBranch` (top level) | session | yes | yes | medium | branch name can be sensitive; confirmed present |
| `cwd` | `cwd` (top level) | session | yes | yes | medium | absolute path; confirmed present |
| Session id / version | `sessionId`, `version` | session | yes | yes | low | confirmed present |
| Tool use | content block `type:"tool_use"` with `id`, `name`, `input` | session | yes | yes | medium–high | `input` can contain file contents/commands |
| Tool result | content block `type:"tool_result"` with `tool_use_id` (matches `tool_use.id`) and `is_error` (failure = `is_error == true`) | session | yes | yes | medium–high | output text can be sensitive. Hook outcomes are not on the tool result: they are separate `attachment` rows (next rows) |
| Permission denial | user line with a non-accept `permissionDecision` or a `toolDenialKind` | session | yes | yes | low | no dedicated boolean; inferred |
| Hook outcome | `attachment` lines (`hook_success`, `hook_blocking_error`, `hook_stopped_continuation`, …) with `hookEvent`, `hookName`, `durationMs`, `exitCode` | session | yes | yes | low | |

Confirmed from a real v2.0.77 line (abridged):

```json
{"cwd":"/home/.../ai-session-stats","sessionId":"c54a...","version":"2.0.77",
 "gitBranch":"claude/investigation-inventory-ai-usage-data","type":"assistant",
 "message":{"model":"claude-haiku-4-5-20251001",
   "usage":{"input_tokens":3,"output_tokens":154,
            "cache_creation_input_tokens":1200,"cache_read_input_tokens":9700,
            "cache_creation":{"ephemeral_1h_input_tokens":0,"ephemeral_5m_input_tokens":1200}}}}
```

**Official docs:** data location and settings —
<https://docs.claude.com/en/docs/claude-code/data-usage>,
<https://docs.claude.com/en/docs/claude-code/settings>. The docs describe the
transcript location but state that the on-disk JSONL schema is internal; hence
the version pin above.

### Claude retention (`cleanupPeriodDays`)

| Question | Answer | Evidence |
| --- | --- | --- |
| Setting name | `cleanupPeriodDays` in `~/.claude/settings.json` | Official settings docs. |
| Default | 30 days | Official settings docs. |
| Are old transcripts deleted? | Yes — Claude Code deletes chat transcripts older than `cleanupPeriodDays`. | Official settings docs. |
| Real folder check | `~/.claude/projects/-home-...-ai-session-stats/*.jsonl` present on the host; no cleanup config was set (default applies). | Host inspection 2026-11-25. |

**Official docs:** <https://docs.claude.com/en/docs/claude-code/settings>
(search `cleanupPeriodDays`).

**Implication for a snapshot store:** transcripts are *ephemeral* (default 30-day
window). Any "usage over time" longer than that window must be captured into our
own store before the CLI prunes it.

### Claude status-line allowance file

The status line hook receives a JSON object on stdin. `claude-statusline.ps1`
reads **only** `rate_limits.five_hour` and `rate_limits.seven_day`
(`used_percentage`, `resets_at`) and writes `claude-allowance.json`.

| Metric | Field in status-line stdin | Captured today? | Scope | Survives restart | Offline | Privacy risk |
| --- | --- | --- | --- | --- | --- | --- |
| 5-hour allowance used % | `rate_limits.five_hour.used_percentage` | yes | window | yes (file) | yes | low |
| 7-day allowance used % | `rate_limits.seven_day.used_percentage` | yes | window | yes (file) | yes | low |
| Window reset time | `rate_limits.*.resets_at` | yes | window | yes (file) | yes | low |
| Estimated session cost ($) | `cost.total_cost_usd` | **no** | session (resets on `/clear`) | no (not persisted) | yes | low |
| Total duration (ms) | `cost.total_duration_ms` | **no** | session | no | yes | low |
| API duration (ms) | `cost.total_api_duration_ms` | **no** | session | no | yes | low |
| Lines added/removed | `cost.total_lines_added` / `cost.total_lines_removed` | **no** | session | no | yes | low |
| Model | `model.id` / `model.display_name` | **no** | session | no | yes | low |
| Session id | `session_id` | **no** | session | no | yes | low |

**Can `claude-statusline.ps1` safely persist per-session cost snapshots?** Yes,
mechanically. The script already does an atomic write (temp file + `File.Replace`/
`File.Move`, UTF-8 no BOM) and only persists numeric/metadata fields — the same
low-risk pattern would apply to a `cost.*` snapshot keyed by `session_id`. The
important caveat is that **`cost.total_cost_usd` resets to 0 after `/clear`**, so
a persist step must snapshot per `session_id` and treat a decrease as a reset
rather than overwriting a running total. The official status-line docs describe
`cost.total_cost_usd` as an "Estimated session cost in USD, computed client-side
at list price" that "may differ from your actual bill", so #76 should label it
`estimated`, not `reported`. This is a design note for #76, not a change here.

**Official docs:** status-line input fields —
<https://docs.claude.com/en/docs/claude-code/statusline>.

### Claude OpenTelemetry

Claude Code can emit OTel metrics and events when
`CLAUDE_CODE_ENABLE_TELEMETRY=1` and standard OTLP exporter env vars are set.

| Signal | Name | Type | Notes |
| --- | --- | --- | --- |
| Cost | `claude_code.cost.usage` | metric | USD per model |
| Tokens | `claude_code.token.usage` | metric | by type (input/output/cache) |
| Commits | `claude_code.commit.count` | metric | |
| PRs | `claude_code.pull_request.count` | metric | |
| Lines of code | `claude_code.lines_of_code.count` | metric | added/removed |
| Active time | `claude_code.active_time.total` | metric | |
| Tool decision | `claude_code.tool_decision` | event | accept/reject per tool |
| User prompt | `claude_code.user_prompt` | event | `prompt` / `prompt_text`, redacted unless `OTEL_LOG_USER_PROMPTS=1` |
| Assistant response | `claude_code.assistant_response` | event | `response`, redacted by default (`OTEL_LOG_ASSISTANT_RESPONSES`, falls back to `OTEL_LOG_USER_PROMPTS`) |
| API request | `claude_code.api_request` | event | model, cost, duration, token counts; no prompt text (join on `prompt.id`) |

| Question | Answer |
| --- | --- |
| Could Session Insights be a loopback OTLP receiver? | Technically yes — run an OTLP/HTTP endpoint on `127.0.0.1` and point `OTEL_EXPORTER_OTLP_ENDPOINT` at it. This aligns with the loopback-only posture in ADR-0001. |
| Configuration burden | High and intrusive: the user must set several environment variables (`CLAUDE_CODE_ENABLE_TELEMETRY`, OTLP endpoint/protocol/headers) for *every* shell that runs Claude. Easy to get wrong and easy to leave off. |
| Privacy of event attributes | Higher than the files: the `user_prompt` event carries the prompt and `assistant_response` the response text. Both are redacted by default and are included only when `OTEL_LOG_USER_PROMPTS=1` (or `OTEL_LOG_ASSISTANT_RESPONSES=1` for responses). `api_request` carries no prompt text. Event attributes may also include account/org/session identifiers. |
| Verdict | Not worth it now. |

**Official docs:** monitoring/telemetry —
<https://docs.claude.com/en/docs/claude-code/monitoring-usage>.

### Copilot CLI session state (reader-derived; CLI not run)

> Could not run the Copilot CLI or inspect a real `~/.copilot/session-state`
> directory on this host. The following is derived from
> `CopilotSessionReader.cs` only and must be re-verified against a real session.

| Metric | Event / field | Scope | Survives restart | Offline | Privacy risk |
| --- | --- | --- | --- | --- | --- |
| Input/output/cache/reasoning tokens | `session.usage_record.usage.*` (`inputTokens`, `outputTokens`, `cacheReadTokens`, `cacheWriteTokens`, `reasoningTokens`) | session | yes | yes | low |
| Model | `session.usage_record.usage.model`; per-model under `session.shutdown.modelMetrics` | session | yes | yes | low |
| Premium requests (per record) | `session.usage_record.usage.cost` (counted as premium requests, `CostInfo.PremiumRequests`; not a monetary amount) | session | yes | yes | low |
| Premium requests (total) | `session.shutdown.totalPremiumRequests` | cumulative (maxed on resume) | yes | yes | low |
| Nano-AIU | `session.shutdown.totalNanoAiu`, `session.usage_checkpoint.totalNanoAiu` | cumulative | yes | yes | low |
| API duration | `session.shutdown.totalApiDurationMs` | cumulative | yes | yes | low |
| Lines added/removed, files modified | `session.shutdown.codeChanges.{linesAdded,linesRemoved,filesModified}` | cumulative | yes (only at shutdown) | yes | low |
| Tool execution | `tool.execution_start` / `tool.execution_complete` (`toolCallId`, `toolName`; failure comes from `data.success`) | session | yes | yes | medium |
| Permission denial | `tool.execution_complete` with `error.code == "denied"` (marks permission denials only, not failures in general) | session | yes | yes | low |
| Hook outcome | `hook.start` / `hook.end` (`hookInvocationId`, `hookType`, success, duration) | session | yes | yes | low |
| Skill/plugin | `skill.invoked` / `skill.invoked_ref` (`name`, `pluginName`, `pluginVersion`, `agentName`) | session | yes | yes | low |
| Workspace | `workspace.yaml` (`cwd`, `name`, `summary`, `created_at`) | session | yes | yes | medium |

| Question | Answer |
| --- | --- |
| Retention | Could not determine — no documented retention/cleanup was found for Copilot CLI session state. |
| OTel / export option | Could not determine — none found in the reader or confirmed in docs. |

**Note on completeness:** `codeChanges`, `totalPremiumRequests`, and
`totalNanoAiu` are only guaranteed at `session.shutdown`; a live/abandoned
session may lack them. The reader adds the per-record `usage.cost` values
seen since the last shutdown (pending usage) to the total, so premium requests
are still estimated before shutdown.

**Official docs:** GitHub Copilot CLI —
<https://docs.github.com/en/copilot/concepts/agents/about-copilot-cli>. The
session-state file layout is not documented publicly, hence the "could not
determine" rows.

### msdc-harness heartbeat and `rule-usage.json` (see qvotaxon/ai-session-stats#71)

| Metric | Source | Scope | Survives restart | Offline | Privacy risk |
| --- | --- | --- | --- | --- | --- |
| Harness activation/version | hook path regex `msdc-harness/{version}/`; plugin names `msdc-harness` / `msdc-harness@msdc-plugins` | session/activation | yes | yes | low |
| Harness tool calls / failures | attributed from Claude tool/hook events within an activation window | per version | yes | yes | low |
| Harness hooks / failures | same | per version | yes | yes | low |
| CLI version at activation | captured from startup hook | session | yes | yes | low |

| Question | Answer |
| --- | --- |
| `~/.msdc-harness` heartbeat | Not present on this host and not read by current code; could not determine its on-disk schema. |
| `rule-usage.json` | **Could not determine** — no reference to `rule-usage` exists anywhere in the repo or on the host. Reason: file/feature not present in this codebase; it belongs to the harness tracked in #71. |
| What can be reused | The existing `HarnessEvidence` attribution (version detection + per-version tool/hook tallies) is reusable as-is; any heartbeat/`rule-usage` ingestion depends on #71 defining those files. |

---

## Summary: per-source properties (question 7)

| Source | Per session / day / cumulative | Survives restart | Offline | Privacy risk |
| --- | --- | --- | --- | --- |
| Claude transcript JSONL | per session (pruned after `cleanupPeriodDays`, default 30d) | yes, within window | yes | medium (cwd, branch, tool I/O) |
| Claude status-line allowance file | rolling window (5h / 7d) | yes | yes | low |
| Claude status-line `cost.*` (estimate, not yet persisted) | per session, resets on `/clear` | no (not persisted today) | yes | low |
| Claude OTel | per metric/event, exporter-dependent | only if exported to a store | needs local receiver | higher (prompt attrs, off by default) |
| Copilot session state | per session; some totals cumulative at shutdown | yes | yes | medium (cwd, tool I/O) |
| msdc-harness evidence | per activation / per version | yes | yes | low |

---

## Recommendation

**Sources that should feed the snapshot store (now):**

1. **Claude transcript JSONL** and **Copilot session state** — these are the
   authoritative per-session usage records and are already parsed. Because
   Claude prunes transcripts after `cleanupPeriodDays` (default 30 days),
   snapshotting these into our own store is the only way to get usage *over
   time* beyond that window. This directly unblocks the persistence ADR
   (qvotaxon/ai-session-stats#67).
2. **Claude status-line allowance file** — already persisted; keep it. It is the
   only window/quota signal and is cheap and low-risk.
3. **msdc-harness evidence** — keep the existing attribution; extend only when
   #71 defines the heartbeat / `rule-usage.json` contract.

**Add later (not now):**

- **Claude status-line `cost.*` snapshots.** Worthwhile for the cost model
  (qvotaxon/ai-session-stats#76) because the transcript has no cost field, but it
  needs per-`session_id` handling of the `/clear` reset. Defer until #76 defines
  the cost model.

**OTel capture — defer.** A loopback OTLP receiver is feasible and consistent
with ADR-0001, but the per-shell environment-variable burden on the user is
high, the data overlaps with what the files already give us (cost, tokens,
lines), and the `user_prompt` and `assistant_response` events can carry prompt and
response text (redacted by default, but a larger privacy surface than the files). It is not worth it now; revisit only if
a metric we need (e.g. commit/PR counts, active time) proves unavailable from
the files.

## Open items / could-not-determine

- Copilot CLI session-state **retention** and any **OTel/export** option — not
  documented publicly and the CLI could not be run here. Re-verify on a machine
  with Copilot CLI installed and real session data.
- msdc-harness `~/.msdc-harness` heartbeat and `rule-usage.json` schema — owned
  by qvotaxon/ai-session-stats#71; not present in this repo/host.
- Claude transcript schema is internal; re-verify field names on each CLI update
  (pinned here to v2.0.77).

## Acceptance criteria

- [x] Every question is answered or marked "could not determine" with the reason.
- [x] Official doc URLs are cited for every CLI claim.
- [x] No product code changed (documentation only).
