# ADR-0011 – Evolve deletes the Copilot session it creates

## Status

Proposed (2026-11-27). Becomes Accepted when the owner accepts it. The behaviour shipped in pull request #105, which fixed issue #99.

## Prologue (Summary)

In the context of the Evolve skill analysis, which runs the local Copilot CLI over a digest of the user's own requests and so leaves a session
(including that digest) in the user's real Copilot history, facing the fact that the CLI has no delete or do-not-save option, we decided for
the backend deleting exactly the one session folder its own run created, found by a session id it assigned itself, under rules that all fail
closed, to achieve no digest transcript left behind after a completed analysis and a worst case limited to one GUID-named folder created by that
run, accepting that Copilot-owned residue stays (the session index rows, config changes, process logs, and the sessions of cancelled or timed-out
runs) and that the product now contains code that deletes under the user's profile.

## Discussion (Context)

Evolve builds one prompt from up to 60 of the user's own requests and sends it to the selected CLI. For Copilot, the CLI stores every run as a
session folder under `<COPILOT_HOME>\session-state\<session id>`, with an `events.jsonl` transcript and a `workspace.yaml` that records the
session's working folder (`cwd`). Evolve always runs in `%TEMP%\ai-session-stats-evolve`, so its sessions are found by that `cwd`, and the app
already hides them from its own session list. They still show in the user's own Copilot history: the owner saw one in Copilot's resume picker.
The folder's `events.jsonl` holds the full digest.

Forces:

- The owner decided that Evolve must be safe to ship in the pull request that fixes #99 (#105), which includes not leaving digests in the user's history.
- The CLI reference documents no flag that skips persistence and no non-interactive session delete. Deleting is only possible in the
  interactive picker.
- `--session-id <id>` is resume-or-create: an existing session with that id would be resumed and appended to. So the id has to be fresh, and
  the run must be refused if it already exists.
- A bug in a delete is not recoverable by reverting the commit. The code has to be cheap to review and every rule has to be shown to matter.

Non-goals: cleaning sessions created by earlier Evolve runs, cleaning Copilot's own index and logs, and anything for Claude (its Evolve run
passes `--no-session-persistence`). Report runs (#52, #57) make their own decision and may reuse this one as precedent.

Options considered:

- **A throwaway `COPILOT_HOME` per analysis.** Rejected: the CLI's sign-in does not follow a different home (observed in a zero-model run,
  "no authenticated GitHub host available"), so it would need credentials copied into a temp folder. That is a credential-handling cost larger
  than the problem.
- **A no-persist flag.** Rejected: none exists in the CLI command reference, and an earlier investigation (#53) found none on 1.0.114.
- **Leave the sessions (status quo).** Rejected by the owner: the digest stays in the user's history.
- **Sweep every session whose `cwd` is Evolve's folder.** Rejected: enumeration-based deletion widens the blast radius from "the folder this
  run created" to "any folder that matches a value in a file". Older leftovers stay for the user to delete in the picker.
- **Delete the `session-store.db` row too, or drive the picker's `x` key.** Rejected: the first is a Copilot-owned, undocumented SQLite schema
  with corruption risk, and the second is interactive only.

Evidence (experiments of 2026-11-27, run against the real Copilot CLI; the session counts and ids stay out of this record):

- **Z1, argument loss.** Through the npm `copilot.cmd` shim, today's launch (`-p` plus a multi-line prompt) sent 9 arguments and the CLI received 2:
  everything after the first newline was lost. The target launch (instruction and data on stdin, constant arguments) sent 21 arguments and
  received 21, identical, with the stdin bytes matching by hash.
- **Z2, flags.** All the new flags parse. `--deny-tool "*"` is rejected ("Invalid rule format"), `--deny-url "*"` is accepted, so the lockdown
  uses named deny rules instead.
- **M1 and M3, lockdown.** Stdin input without `-p` works. The tool baseline lists only `view`. In M3 the model attempted three `view` calls on
  planted canary files and all three were denied: the read deny rule was proven on a file inside the working folder, and files outside it were
  stopped by the path restriction plus the unattended fallback (no interactive user to approve). No canary content leaked.
- **Z3, cleanup against the real layout.** The product cleanup, run through the real CLI's folder layout in a throwaway home, removed the session
  (`Removed` logged, only the CLI's own lock folder left).
- **J1, links.** A session folder that is itself a junction is refused (`ReparsePoint`) and the target stays intact. When the junction is
  inside the session folder, `Directory.Delete(recursive)` throws `UnauthorizedAccessException` on this OS (reproduced in plain PowerShell
  without the product). The product logs `Failed (UnauthorizedAccessException)`, the junction is not followed and the target is intact, the
  session's own files (`workspace.yaml`, `events.jsonl`) are deleted, and an empty folder is left. This is accepted: it fails closed.
- **M2, end to end.** The product ran against the real Copilot home: the analysis returned its result, `Removed` was logged, the session folder
  was absent afterwards, the number of session folders was equal before and after, a recheck 30 seconds later still found it absent, and
  Evolve's working folder stayed empty.
- **P1, picker check (by the owner).** An older session from before the fix, used as a control, was listed in Copilot's own resume picker. No
  session from the M2 run was listed. The limit: only the visible rows (in descending last-updated order) were checked, and the picker may list
  more.

Not verified:

- That `--available-tools` hides the tools of a connected MCP server. The servers that did start all failed sign-in, so none connected.
- The `@` attachment. It is escaped in the prompt and unit-tested, but not observable end to end, because the cleanup deletes `events.jsonl`. The
  canary's access time is confounded by the experiment's own read, and the process log showed nothing.
- The usage figures of the M2 run.
- Whether the CLI's workspace discovery walks up through ancestor folders of the working folder.

## Solution (Decision)

What we will do. After the CLI process of a Copilot analysis returned normally, the backend deletes one folder:

1. The id is a fresh GUID generated by the app and passed as `--session-id`. It never comes from session text, and it is re-checked with the
   canonical GUID format.
2. The folder is a direct child of `<the child's own COPILOT_HOME>\session-state`. The root is resolved the way the child resolves it (the
   configured `CopilotHome`, else the inherited `COPILOT_HOME`, else the profile's `.copilot`), not from `CopilotRoot`, which is only the
   reader's override and is never passed to the child. A root that is not fully qualified is refused.
3. The folder did not exist before the launch. If it does, the analysis refuses to launch at all, because the CLI would resume it.
4. The folder is not a link or junction.
5. `workspace.yaml` exists, parses as a mapping, has an `id` that matches if it has one, and has a `cwd` equal to Evolve's working folder
   (case-insensitive on Windows, trailing separators ignored).
6. The delete runs inside the analysis lease, which keeps the app's own session index away from the folder, and before the exit-code check, so a
   failed or unparseable run is cleaned as well. It never runs on cancel, timeout or I/O failure of the launch, because the process tree may
   still be writing.
7. A refusal or a failed delete is logged with the session id, an outcome name and, for a failure, the exception type. It never fails the
   analysis. No path, workspace value or exception message is logged.

The same change pins `--sync-session=off` on the child, because a synced cloud copy would survive the local delete.

Maximum blast radius, by construction: one GUID-named folder under the child's session-state, created by this run, whose `workspace.yaml` names
Evolve's folder. No wildcard and no enumeration is used.

What we will not do. We will not sweep by `cwd`, delete Copilot's index rows, relax any rule to make a run pass, or extend this to Claude. If
a CLI change makes a rule fail (the `cwd` or `id` keys, a changed home), the code keeps the folder and logs a warning; the rule is then
re-assessed, not loosened.

## Consequences (Results)

Positive:

- A completed Evolve analysis leaves no `events.jsonl` with the digest in the user's session history.
- Every safety-critical rule (the `cwd` comparison, the presence of the `cwd` key, the pre-existence check, the id format, the link refusal and
  the fully qualified root) has a unit test on an in-memory file system that turns red when the rule is removed; a review mutated each of
  them (and others) and every one turned a test red. One extra layer cannot be reached by a test, because a GUID id is always a direct
  child: the direct-child check. The id check inside `Exists` has its own test.

Negative:

- The product now contains code that deletes in the user's profile. A bug there is not recoverable by a revert, so the code is reviewed as
  security-relevant and rule changes need a new decision.
- Residue remains that the product cannot remove: Copilot's `session-store.db` rows (the session was not listed in the picker in the P1 check,
  within the limits stated under Evidence), `config.json` changes, `process-*.log` files, and the full session of a cancelled or timed-out analysis, the most likely
  failure path being the 2-minute timeout.
- Copies made by user code remain. The user's own hooks run in the child and receive the prompt and a `transcriptPath`, so a hook can copy the
  transcript. If `--sync-session=off` is rejected by an older CLI and dropped, a synced copy may also remain.
- Accepted residual (J1b): when a session folder contains an inner junction, the recursive delete throws `UnauthorizedAccessException`. The
  product logs a warning and leaves an empty folder; the junction is not followed and its target is intact. It fails closed.
- Also remaining, from the Evidence section: MCP servers and plugin hooks of the user's own configuration still start (a user hook receives the
  session digest and a transcript path); the working folder can be trusted by the CLI on a machine whose profile folder is trusted (it is empty
  by construction and stayed empty in the runs); the bring-your-own-key variables are kept on purpose; the items under "Not verified".
- A same-user process can swap a link in between the check and the delete. That is outside the model: such a process can already delete the
  data itself.
- A backend that resolves a different home than the child (an unusual `HOME` or `USERPROFILE`) deletes nothing and logs a warning, so the
  digest stays.

Follow-ups:

- Ask GitHub for a delete or do-not-save option for non-interactive runs. Trigger: when it exists, remove this code (the owner of #99).
- #57 (report runs) may reuse this precedent but must make its own decision. #59 (the two-phase Evolve split) must carry the session id and the
  cleanup into both of its phases.
- Re-assess if the `workspace.yaml` `cwd` or `id` keys change, or if evidence shows that deleting a session folder harms Copilot's index.
