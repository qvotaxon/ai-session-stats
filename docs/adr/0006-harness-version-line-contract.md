# ADR-0006 – The msdc-harness SessionStart hook prints a fixed version line that Session Insights reads before falling back to the install path

## Status

Proposed (2026-11-28). Becomes Accepted when the owner and the msdc-harness owner agree the format; the status changes only on their word (#62).

## Prologue (Summary)

In the context of detecting which msdc-harness version a Claude or Copilot session ran, facing a path-regex detection that silently turns into "unknown" when the plugin's install layout changes, we decided for a one-line contract `msdc-harness-version: <name>@<semver>` printed first by the plugin's SessionStart hook and read by Session Insights before the existing path regex, to achieve a version signal that does not depend on the install layout and also works for Copilot, accepting a cross-repository dependency on the msdc-harness hook (#63) and an "unknown" result for sessions started by plugin versions that do not print the line yet.

## Discussion (Context)

**Background**

- Session Insights infers the plugin version from the SessionStart hook's recorded stdout by matching `msdc-harness/<semver>/` (`apps/backend/src/Infrastructure/Sessions/HarnessEvidence.cs`, `ClaudePath` and `Claude`). If the plugin or its install layout changes, the result silently becomes `unknown`.
- For Copilot, the version comes only from `skill.invoked` and `skill.invoked_ref` events (`HarnessEvidence.Copilot`, called from `CopilotSessionReader.cs`). A Copilot session that never invokes a skill has no version evidence.
- The hook is `scripts/session-start-doctrine.js` in the **msdc-harness** repository, wired from `hooks/hooks.json` (Claude) and `hooks/copilot-hooks.json` (Copilot). It writes one JSON object to stdout that carries the doctrine twice: flat `additionalContext` (read by Copilot) and `hookSpecificOutput.additionalContext` (read by Claude). The msdc-harness owner offered to add a fixed version line.

**What the transcripts record (checked, not assumed)**

- Claude: the reader takes the SessionStart hook attachment's `stdout` (`HarnessEvidence.Claude`). The stdout is the raw JSON document, so a line break inside `additionalContext` is recorded as the two characters `\n`, and a path separator as `\\`. The existing tests already cover these JSON-escaped forms.
- Copilot: `CopilotSessionReader.cs` reads `hook.start` and `hook.end` events but uses them only for hook counts, durations and failures. It does not read the hook's output. A real Copilot session-state file does record it: in `events.jsonl`, the `hook.end` event with `data.hookType` equal to `sessionStart` has `data.output.additionalContext`, already parsed, with real line breaks. Of 126 such events in the local session-state folder, 124 carried an `additionalContext` of up to about 16,000 characters and 2 carried no output. The recorded `output` object holds only the key `additionalContext`.
- Neither transcript records the plugin name or version for a session the hook did not run in, for instance when the plugin is disabled. For those sessions the result stays `unknown`.

**Requirements**

- A signal that is independent of where the plugin is installed.
- The same line on both surfaces, so the parser has one rule.
- Robust to the JSON escaping that Claude's stdout recording introduces.
- Evolvable without breaking older Session Insights builds.

**Non-goals**

- Implementing the emitter (#63, other repository) or the parser (#65).
- Detecting the version of a harness that is not msdc-harness.
- Proving that a version caused a failure. The line says which version a session started with, nothing more.

**Options considered**

- **Keep the path regex only.** Rejected: it is the status quo that silently breaks on a layout change, and Copilot's recorded output contains no install path, so it cannot work there.
- **Read the version from the installed `plugin.json` at index time.** Rejected: it reports the version installed now, not the one the session ran. An update between the session and the index run attributes the session to the wrong version.
- **Add a structured key to the hook's JSON output, such as `harnessVersion`.** Not chosen: text inside `additionalContext` is already recorded on both surfaces (checked above), so a second channel adds nothing. Whether Copilot keeps unknown keys in its recorded `output` object was not investigated, because the decision does not depend on it.
- **Rely on Copilot `skill.invoked` events for Copilot.** Kept as the existing evidence, not enough alone: a session without a skill invocation has no version.

## Solution (Decision)

**The line**

- The SessionStart hook prints one line `msdc-harness-version: <name>@<semver>`, for example `msdc-harness-version: msdc-harness@0.29.0`. `<name>` is the plugin name as a marketplace knows it. `<semver>` is the plugin's own version, with an optional pre-release and build part, the same grammar as `VersionPattern` in `HarnessEvidence.cs`.
- It is ASCII, printed first in the `additionalContext` text, once per session start, on both Claude and Copilot, followed by a line break. The first position keeps it ahead of any truncation of the doctrine text that follows.
- The prefix `msdc-harness-version:` is fixed and does not follow the plugin name, so a renamed plugin still prints a line that a consumer can read and compare with its configured name (`HarnessPluginName`, #64).

**How a consumer reads it**

- The consumer reads the SessionStart text the transcript recorded: `stdout` for Claude, `output.additionalContext` of the `sessionStart` `hook.end` event for Copilot.
- It finds the pattern `msdc-harness-version: <name>@<semver>` anywhere in that text, not only at a line start, because Claude records it inside a JSON string. The `<name>` and `<semver>` character classes contain no backslash, quote or whitespace, so the escaped `\n` that follows the line ends the match.
- A match whose `<name>` differs from the configured plugin name is ignored, because another plugin may print its own version lines.
- Fallback order: the contract line, then the install-path regex (Claude only), then `unknown`.
- Results: one distinct version from the contract is `contract`; a contract version and a path version that disagree, or two distinct contract versions in one SessionStart output, is `conflicting`; a contract version and a path version that agree is `contract` (the contract wins); no match is `unknown`. The evidence value `contract` is added next to `hookPath`, `invocation`, `unknown` and `conflicting`.
- For Copilot, the `contract` line and the existing `skill.invoked` version are separate observations. A session start that carries the line yields a `contract` observation; the skill-invocation observations stay as they are.

**Versioning the line**

- The line is append-only. A new datum is a new key on its own line, `msdc-harness-<key>: <value>`. An existing key never changes its meaning, format or name.
- A consumer ignores any line and any key it does not know.
- A breaking change to the version line is a new key, with the old one printed alongside it for at least one minor release of the plugin, and a superseding ADR.

**What we will not do**

- We will not read the plugin's install directory or `plugin.json` to decide a session's version.
- We will not change the existing evidence values or remove the path regex while older plugin versions are in use.
- We will not guess a version for a session whose SessionStart output was not recorded.

## Consequences (Results)

- Positive: the version signal survives a change of install layout; Copilot sessions get a version without a skill invocation; both surfaces share one rule.
- Positive: `hookPath` and `contract` can be compared on sessions that have both, which shows when the path regex is no longer needed.
- Negative: sessions started by msdc-harness versions that do not print the line stay on the path regex or `unknown`. Copilot sessions started by such versions keep skill-invocation evidence only.
- Negative: the contract is shared across two repositories. A change to the hook's output shape, such as moving the version out of `additionalContext`, breaks the parser unseen unless the line's format is kept.
- Follow-up: the owner and the msdc-harness owner accept or change the format, and the status becomes Accepted (trigger: this pull request is reviewed).
- Follow-up: #63 (msdc-harness repository) emits the line. Trigger: this ADR is Accepted.
- Follow-up: #65 (this repository) implements the parser, adds the `contract` evidence value to `docs/api-contract.md` and the Harness page, and starts reading `hook.end` output for Copilot. Trigger: this ADR is Accepted and #64 is merged (done).
- Follow-up: remove the path regex once the oldest plugin version in use prints the line. Owner: the repository owner; trigger: no session in the index has `hookPath` evidence only.
