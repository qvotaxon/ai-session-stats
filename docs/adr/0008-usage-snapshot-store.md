# ADR-0008: Local persisted snapshot store for usage trends

- Status: Proposed
- Date: 2026-11-25

## Context

Trends need data that outlives CLI transcript retention (#66). Claude Code deletes old
transcripts after `cleanupPeriodDays` and Copilot CLI session state is likewise transient, so a
dashboard that reads only the live transcripts cannot show usage over weeks or months once those
files are gone. Session Insights already keeps small JSON files under
`%LOCALAPPDATA%\SessionInsights` (`token`, `excluded-sessions.json`, `settings.json`,
`claude-allowance.json`, and the `reports/` tree), resolved through
`SessionInsightsOptions.ResolveDataRoot()` and used by `ExcludedSessionsStore.cs:27`,
`TerminalSettingsStore.cs:28` and `ReportCoordinator.cs:345`. None of these is a time-series store.

The consumers of this store are:

- **#70** (overview trends): weekly series of tokens by type, sessions, tool calls and tool
  failure rate, skill, agent and subagent usage, harness-in-effect share, and cost only when
  paired with delivered work. Filters by source, project and date range. Claude and Copilot are
  shown side by side and never summed when not comparable.
- **#72** (harness page): per-harness-version aggregation of sessions, attributed tool calls, tool
  and hook failure rate, skill and agent invocations, first and last seen date, and sample size,
  with Claude and Copilot kept separate.
- **#77** (cost vs delivered work): cost next to delivered story points per team per sprint, with
  cost never shown without delivered-work data, and Claude and Copilot never summed unless
  `docs/metrics.md` declares them comparable.

All three read, not write. The queries are small: group a bounded number of daily rows by day,
week, source, project and harness version. There is no need for ad-hoc SQL, joins across large
tables, or sub-second query latency over millions of rows. A single developer machine produces at
most a few sessions a day.

Any store must weigh crash safety (the server is a Scheduled Task that can be killed mid-write by
logoff, update, or `Quit Session Insights`), size after one year, the query needs above, backup and
uninstall behaviour (the uninstaller deletes the whole data root, per `README.md:62`), and package
approval (new NuGet dependencies require owner approval).

## Options

### 1. Daily aggregate JSON-lines files, one file per month

`snapshots/YYYY-MM.jsonl`, one JSON object per line, one line per `(session, day, source, project
hash, model, harness evidence, harness version, CLI version)` tuple. A row is merged by reading the
current month file, upserting the affected lines under the never-shrink rule below, and writing the
file once.

- Crash safety: write to `YYYY-MM.jsonl.tmp` and atomically replace. This is new code: the
  existing stores (`ExcludedSessionsStore.cs:49`, `TerminalSettingsStore.cs:54`,
  `ReportCoordinator.cs:347`) write in place with `WriteAllText`, and only `claude-statusline.ps1`
  (lines 55-61) uses temp-and-replace. A torn
  line is isolated to one month and one day; the reader skips malformed lines and logs.
- Size: a dense upper bound is roughly 365 days x a handful of sources/projects/models x ~400
  bytes, i.e. low single-digit megabytes a year, far less in practice because a solo user hits few
  combinations a day. Monthly sharding keeps any single rewrite small.
- Query: read the relevant month files, parse lines, group in memory. Trivial for a year of data.
- Backup/uninstall: plain text files under the data root; copy to back up, removed on uninstall
  with everything else. Human-readable and diffable.
- Package: none. Uses `System.Text.Json` and `IFileSystem`, matching the existing stores' JSON
  and file-system abstraction (not their in-place write).

### 2. SQLite through `Microsoft.Data.Sqlite`

A single `snapshots.db` with one table keyed by the tuple above.

- Crash safety: WAL mode and transactions give strong guarantees, but a native SQLite binary is
  shipped per runtime and a corrupt database is opaque to inspect or repair by hand.
- Size: compact, with indexes; comparable to or smaller than option 1 at this scale.
- Query: SQL grouping is convenient, but the required queries are simple grouped sums that in-memory
  LINQ handles without a query engine.
- Backup/uninstall: a single binary file; backup must copy the WAL/SHM siblings too. Removed on
  uninstall. Not human-readable.
- Package: adds `Microsoft.Data.Sqlite` (and its native asset), which needs owner approval and
  enlarges the self-contained `win-x64` publish. Disproportionate to the query needs.

### 3. One JSON file rewritten on every change

`snapshots.json` holding the full array, rewritten whenever a day changes.

- Crash safety: the same atomic temp-and-replace pattern works, but every write rewrites the entire
  history.
- Size: the whole year lives in one file; rewrite cost grows with total history, not with the day
  being changed.
- Query: load all, group in memory. Fine to read, wasteful to write.
- Backup/uninstall: one text file; same as option 1 but coarser.
- Package: none.

## Decision

Adopt **option 1: daily aggregate JSON-lines files, one file per month**, under
`snapshots/YYYY-MM.jsonl` in the data root resolved by `SessionInsightsOptions.ResolveDataRoot()`.

- The store reuses the existing `IFileSystem` abstraction and `System.Text.Json`, and adds a `Lock`
  gate for concurrent writes. Writes go to a `.tmp` sibling and atomically replace the target, so a
  kill mid-write leaves the previous file intact. The existing stores do not do this (they write in
  place), so the temp-and-replace write is new code in this store, not a reused pattern.
- The unit of storage is a **per-session daily aggregate row**: the rolled-up total for one session
  on one day for one model, not a per-message record. Session identity is part of the key so that
  totals are sums over rows and a pruned transcript affects only its own session's rows.
- **Never-shrink rule.** Re-indexing upserts rows by the key below and never replaces a stored value
  with a smaller recomputed one. For each measure the stored value is kept when the recomputed one
  is lower (a transcript partly pruned, or a session only partly readable), and rows for sessions
  no longer readable are left untouched. The key makes repeated runs idempotent, so they never
  double-count, and the rule means pruning never shrinks stored history (#69).
- Reads load the month files in the requested range and group in memory. This covers #70, #72 and
  #77 without a query engine.
- Rows are grouped by month file only; a day's rows are confined to one file, bounding every
  rewrite.

### Record schema

Each JSON-lines row is one aggregate for one session, one day and one key combination.

Key fields (identity of the row):

- `sessionId` — the CLI's opaque session identifier (not a path or name). It drives the
  never-shrink upsert and gives #69's rows keyed by session id and day.
- `day` — local date, `YYYY-MM-DD`.
- `source` — `claude` or `copilot`.
- `projectHash` — a salted, non-reversible hash of the project path (never the path itself).
- `model` — model identifier as reported by the CLI.
- `harnessEvidence` — how harness use was observed: `activation` (Claude hook-path activation,
  `HarnessEvidence.cs:36-37`), `invocation` (the only kind Copilot produces, lines 40-43), or
  `none` (no harness observed). Stored separately from the version so "no harness" and "active,
  version unknown" stay distinct.
- `harnessVersion` — the detected harness version, or null when unknown. Null with `activation` or
  `invocation` means active with unknown version; `none` always carries null.
- `cliVersion` — the CLI version the data was read from (the transcript format is internal, so the
  version is recorded for provenance).

Measure fields (sums over that day and key):

- Tokens by type: `inputTokens`, `outputTokens`, `cacheReadTokens`, `cacheWriteTokens`.
- Cost, one nullable field per unit, because Copilot reports premium requests and nano-AIU
  (`CostInfo(PremiumRequests, NanoAiu)`, `SessionMetrics.cs:63`), not dollars: `costUsd`,
  `costPremiumRequests` and `costNanoAiu`. Each is null when that unit has no source, so no unit is
  converted into another or lost, and units are never summed across each other. `costLabel`
  (`reported`, `estimated`, or `tokens-only`) lets a consumer honour "cost never shown alone" and the
  comparability rule in `docs/metrics.md`.
- `toolCalls`, `toolFailures`.
- `skillInvocations`, `agentInvocations`, `subagentInvocations`.
- `hooks` and `hookFailures`: the total and the failed count, so #72 computes the failure rate as
  `hookFailures / hooks`, as `HarnessPage.tsx:85` does (`HarnessActivity` carries both).

A session count is derived by counting distinct `sessionId` over the rows in range; it is not
stored, since a per-row count cannot be summed.

A `schemaVersion` integer accompanies each row so the shape can evolve without reinterpreting old
files.

### Delivered work

Delivered story points are measured per team per sprint (#77), not per session, day or model, so
summing them from these rows would double count. They are not a field here. They belong in a
separate small store keyed by `(team, sprint)` holding `deliveredPoints` and `deliveredLabel`,
written by whichever feature supplies the delivery signal and joined to cost at read time. Until
that signal exists nothing is stored, so #77 shows "no delivered-points data" rather than a bare
cost. The exact shape of that store is left to #77.

### Project hash salt

`projectHash` uses a salt so a path cannot be recovered by hashing guesses. The salt is a random
value generated once and kept in the data root, outside the month files (for example
`snapshots/salt`). If it is lost or regenerated the same project hashes differently and its history
splits into a new project. Whether the salt must be recoverable, and how that stability is
guaranteed, is an **open question** for implementation.

### Never stored

The store records only aggregated counts, costs and the keys above. It **never** stores prompt or
response text, tool arguments or tool output, file contents, file paths or project names (only the
salted `projectHash`), session recaps, per-message detail, usernames, machine identifiers, or any
Claude or Copilot credentials or API-credit balances. This keeps ADR-0001's metrics-only boundary
intact for the persisted copy.

### Retention policy

Default: **keep forever.** The store is append-and-update only and is never auto-pruned, because the
point of the store is to outlive transcript retention. The user can clear the store from Settings,
which deletes the `snapshots/` directory; the uninstaller removes it with the rest of the data root
(`README.md:62`). No time-based auto-deletion is added, so trends remain complete unless the user
chooses to clear them.

### Back-fill on first run

On first run after this store ships, Session Insights back-fills from the transcripts still present:
it indexes the Claude and Copilot sessions it can still read, aggregates them into per-session
daily rows, and writes the month files. Back-fill covers only the retained transcripts, so history
before the CLIs' retention window cannot be recovered and is simply absent. Back-fill follows the
same never-shrink rule and is idempotent (keyed by the tuple), so re-indexing upserts rather than
appends or replaces, and a crash mid-back-fill can be safely re-run. After back-fill, the store is the durable source for trends even once the
underlying
transcripts are deleted.

## Consequences

- No new package and no owner approval needed; the store reuses the existing JSON + `IFileSystem`
  approach. The atomic temp-and-replace write is new code here, not a reused pattern.
- Keying by session makes sums valid and keeps history intact after pruning, at the cost of more
  rows (one per session per day per model) than a pure daily rollup; still small at this scale.
- Files are human-readable and diffable, easy to back up and inspect, and a corruption is contained
  to one month with malformed lines skipped.
- In-memory grouping is adequate at single-developer scale; if data volume ever outgrows this (for
  example a shared or multi-user deployment), this ADR is revisited and SQLite reconsidered.
- Keeping data forever means the store only ever grows until the user clears it, but the yearly size
  is small and sharded by month.
- Back-fill cannot reconstruct history older than the CLIs' own retention, so the earliest trends
  begin at the oldest transcript present on first run.
