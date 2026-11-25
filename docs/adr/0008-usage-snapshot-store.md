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

`snapshots/YYYY-MM.jsonl`, one JSON object per line, one line per `(day, source, project hash,
model, harness version, CLI version)` tuple. A day's rows are rewritten by reading the current
month file, replacing that day's lines, and writing the file once.

- Crash safety: write to `YYYY-MM.jsonl.tmp` and atomically replace, as the existing stores do.
  A torn line is isolated to one month and one day; the reader skips malformed lines and logs.
- Size: a dense upper bound is roughly 365 days x a handful of sources/projects/models x ~400
  bytes, i.e. low single-digit megabytes a year, far less in practice because a solo user hits few
  combinations a day. Monthly sharding keeps any single rewrite small.
- Query: read the relevant month files, parse lines, group in memory. Trivial for a year of data.
- Backup/uninstall: plain text files under the data root; copy to back up, removed on uninstall
  with everything else. Human-readable and diffable.
- Package: none. Uses `System.Text.Json` and `IFileSystem`, matching the existing stores.

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

- The store follows the existing persistence pattern: an `IFileSystem` abstraction, a `Lock` gate
  for concurrent writes, and `System.Text.Json`. Writes go to a `.tmp` sibling and atomically
  replace the target, so a kill mid-write leaves the previous file intact.
- The unit of storage is a **daily aggregate row**, not a per-message or per-session record. Each
  row is the rolled-up total for one day for one combination of the key fields. Re-running indexing
  for a day recomputes and replaces that day's rows idempotently, keyed by the tuple below, so
  repeated runs never double-count.
- Reads load the month files in the requested range and group in memory. This covers #70, #72 and
  #77 without a query engine.
- Rows are grouped by month file only; a day's rows are confined to one file, bounding every
  rewrite.

### Record schema

Each JSON-lines row is one aggregate for one day and one key combination.

Key fields (identity of the row):

- `day` — local date, `YYYY-MM-DD`.
- `source` — `claude` or `copilot`.
- `projectHash` — a salted, non-reversible hash of the project path (never the path itself).
- `model` — model identifier as reported by the CLI.
- `harnessVersion` — detected harness/CLI harness version, or `unknown`.
- `cliVersion` — the CLI version the data was read from (the transcript format is internal, so the
  version is recorded for provenance).

Measure fields (sums over that day and key):

- Tokens by type: `inputTokens`, `outputTokens`, `cacheReadTokens`, `cacheWriteTokens`.
- Cost: `costUsd` and `costLabel` (`reported`, `estimated`, or `tokens-only`), so a consumer can
  honour "cost never shown alone" and the comparability rule in `docs/metrics.md`. `costUsd` is null
  when no cost source exists.
- `toolCalls`, `toolFailures`.
- `skillInvocations`, `agentInvocations`, `subagentInvocations`.
- `hookFailures` (for #72's hook failure rate).
- `sessions` — distinct sessions contributing to this row that day.
- Delivered-work fields: `deliveredPoints` and its `deliveredLabel`, populated only when a delivery
  signal exists; otherwise null so #77 shows "no delivered-points data" rather than a bare cost.

A `schemaVersion` integer accompanies each row so the shape can evolve without reinterpreting old
files.

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
it indexes the Claude and Copilot sessions it can still read, aggregates them into daily rows, and
writes the month files. Back-fill covers only the retained transcripts, so history before the CLIs'
retention window cannot be recovered and is simply absent. Back-fill is idempotent (keyed by the
tuple), so re-indexing a day replaces rather than appends, and a crash mid-back-fill can be safely
re-run. After back-fill, the store is the durable source for trends even once the underlying
transcripts are deleted.

## Consequences

- No new package and no owner approval needed; the store reuses the existing JSON + `IFileSystem`
  pattern.
- Files are human-readable and diffable, easy to back up and inspect, and a corruption is contained
  to one month with malformed lines skipped.
- In-memory grouping is adequate at single-developer scale; if data volume ever outgrows this (for
  example a shared or multi-user deployment), this ADR is revisited and SQLite reconsidered.
- Keeping data forever means the store only ever grows until the user clears it, but the yearly size
  is small and sharded by month.
- Back-fill cannot reconstruct history older than the CLIs' own retention, so the earliest trends
  begin at the oldest transcript present on first run.
