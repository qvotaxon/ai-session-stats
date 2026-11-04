# ADR-0002: Report-run forks are never listed as sessions

- Status: Accepted
- Date: 2026-01-10

## Context

A report is produced by resuming the session in its CLI. `claude --fork-session` creates a **new** session file that
copies the history and ends with the report prompt. Left alone it would show up as a session of its own and, worse,
could be reported on again. Copilot resumes in place, so its report turn pollutes the metrics of the original instead.

## Decision

Several independent layers; any one of them hides a fork:

1. The `session_id` returned by a Claude run is added to `excluded-sessions.json` (persisted under the data root) **before** the run is marked finished.
2. Heuristic: a Claude session whose first human message is the report prompt (or whose last human message is the prompt and that line came from the SDK entrypoint) is a fork and is dropped.
3. Race safety: while any report run is active, session files that are new to the index are held back until the run ends, so a fork cannot flash into the list before layer 1 records it.
4. `GET /sessions/{id}` and report start go through the same index, so an excluded id gives 404 and can never be reported.
5. Copilot: the report prompt turn (and everything until the next real user message) is excluded from tokens, tools, hooks, turns and subagents.

## Consequences

- Forks never appear, even after a restart, and exclusion survives deletion of the index (it is a file, not memory).
- A genuine human session that starts with exactly the report prompt is hidden. Accepted: it is indistinguishable from a fork.
- The exclusion file grows by one GUID per Claude report run. Negligible.
