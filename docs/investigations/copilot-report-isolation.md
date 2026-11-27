# Investigation: isolating Copilot report runs from the user's real session

Status: Proposed
Recommended option: (b) no resume, with a digest of the session inside the `-p` argument and a dedicated report working directory. It is conditional on a length test (see "Recommendation").
Accepted option:

Issue: [#53](https://github.com/qvotaxon/ai-session-stats/issues/53). Unblocks #57. Overlaps with #52 (Claude), which is parked and not touched here.
Scope: findings only. No product code, test or ADR file is changed by this document. The ADR-0002 amendment is proposed text below.

## 1. Summary and recommendation

A Copilot report today runs `copilot -p <prompt> --resume=<id> -s` in the session's own working directory
([ReportCoordinator.cs L290-L292](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Reports/ReportCoordinator.cs#L290-L292)).
Measured on a throwaway session: the report prompt and the model's answer are appended to that session's `events.jsonl`, the
session is the user's history afterwards, and the report is in context on the next resume (a follow-up resume answered that
the last thing produced was the report).

Findings that decide the options:

1. The CLI has no fork, copy or no-persist flag for `--resume` (Q1). Option (a) does not exist.
2. Every run without `--resume` creates a new session in the Copilot home. It is visible in Copilot's own resume list (owner check, Q5).
   Isolation (b) therefore moves the pollution from the user's real session to a throwaway session. It does not remove it.
3. `-p` ignores piped stdin (one run). A transcript has to be inside the `-p` argument. A newline in that argument is
   truncated by the `copilot.cmd` shim, so the digest has to be a single line.
4. An empty temp `COPILOT_HOME` is not authenticated, so option (c) cannot run without copying auth material out of the real home (Q3).
5. `--deny-tool '*'` is rejected by this CLI. Evolve's current flags probably fail (observation, section 7).

**Recommended option: (b)**, cost M (a digest builder plus a report working directory), about 1 premium request per report,
the same as today. The condition: #57 must first test that a realistic digest fits through the `-p` argument and the `.cmd`
shim. If it does not fit, the fallback is (d), disable Copilot reports, which is the only option with no history effect.
The owner picks. `Accepted option` is left empty on purpose.

## 2. Environment and version pin

- Copilot CLI: `1.0.114` at the start (`copilot --version` printed `GitHub Copilot CLI 1.0.114.`). It auto-updated to `1.0.116` during the window. `copilot --version` after the experiment printed `GitHub Copilot CLI 1.0.116.`
  The per-run process logs show `1.0.114` for the log of run 3 and `1.0.116` for the logs of runs 4 to 10 (inferred from the log version strings). Runs 1 to 3 used 1.0.114.
  The help text was read at 1.0.114 and re-checked at 1.0.116 for fork and persistence flags: none in either.
- Model pinned on every model run: `claude-sonnet-5.5` (see "Evidence": the model the user's own sessions use most; Sonnet-class mid-tier).
  `claude-sonnet-4.6` is in the CLI's configuration list but was reported "not available" for this account.
- OS: Windows 11. `copilot` resolves to the npm shim `copilot.cmd`.
- Code cited at origin/main `c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2`.
- Spend: 10 counted runs of a cap of 14. Runs 1, 2, 3 and 8 failed before a model call. A model run cost `totalPremiumRequestCost: 1`, about 36k input and 350 to 420 output tokens, and about 5 AI credits.
- #57 must re-run `copilot --help` before implementing: everything here is tied to these versions.

Owner observations are marked "(owner check)": they come from the owner's own look at Copilot's resume picker, not from an agent run.

## 3. Method and safety

- Every call was PowerShell with an argument array, an explicit per-call `COPILOT_HOME` (or none), empty stdin unless piping, a 5-minute timeout, and a pinned model.
- Only recorded throwaway session ids were resumed, and the `workspace.yaml` cwd was asserted to be the scratch directory before each resume. No `--continue`, bare `--resume` or picker was used by an agent.
- No login was completed and no credential file was copied or read. Process checks showed no Copilot client or backend running before the snapshot and before every model run.
- A snapshot of the whole effective home (names, sizes, times, SHA256 under `session-state`) was taken before, mid-way, before the owner's check, and after.
  Result: the `session-state` difference is empty. Details in the evidence appendix.

## 4. Questions

### Q1. Fork, copy or no-persist option for `--resume`

Answer: no.

```powershell
copilot --version
copilot --help
copilot sessions --help
copilot help sessions
```

- Resume-related options: `-r, --resume [<value>]`, `--continue`, `--connect [<sessionId>]`, `--session-id <id>` ("Resume an existing session or task by ID, or set the UUID for a new session").
- `copilot sessions` has only `import`. `/fork` exists only inside an interactive session.
- No flag mentions fork, copy, ephemeral or persistence of a resumed session. Step 4b (testing option (a)) was skipped for that reason.
- `--session-id <id>` does create a new session with the id I passed (checked: the folder name and `workspace.yaml` matched). This lets #57 pre-assign ids.
- `-p` denies tool requests that no allow rule approves, per the help text.
- `--usage-output-file <file>` writes premium requests, AI credits and tokens as JSON even with `-p -s`. It supplied the spend figures below.
- Not done: the online command-line reference was not read. The conclusion rests on `--help` and `help sessions` at two versions.

### Q2. Option (b): no resume, summary on stdin

Answer: stdin does not work, but the summary inside the `-p` argument does.

```powershell
# run 7, scratch cwd "cwd-b", no resume, transcript piped on stdin
& copilot @('-p','Give me a session report in markdown format','--silent','--no-custom-instructions','--disable-builtin-mcps',
  '--deny-tool','shell','--deny-tool','write','--deny-tool','memory','--model','claude-sonnet-5.5','--session-id',$id)  # stdin = transcript file
# run 10, scratch cwd "cwd-e", summary inside the argument (single line)
$prompt='Give me a session report in markdown format. Transcript of the session to report on: USER: list three prime numbers | ASSISTANT: Three primes: 2, 3, 5.'
& copilot @('-p',$prompt,'--silent', ... same flags ..., '--session-id',$id,'--usage-output-file',$file)
```

- **Stdin (run 7):** the report had no trace of the transcript, and the new session's first `user.message` was exactly the prompt. `-p` ignored the piped input in this version (one run).
- **Argument (run 10):** the report used the transcript. The first `user.message` was the prompt plus the digest (151 characters, not an exact match of the prompt).
- **New session on disk:** yes, in both cases. The folder is `session-state/<id>` in the Copilot home, `workspace.yaml` carries the cwd of the report run, and its name is the prompt (plus the digest in run 10).
- **Exclusion in this app:**
  - With stdin, `LooksLikeReportFork` would catch the session (first human message equals the prompt:
    [CopilotSessionReader.cs L345](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/CopilotSessionReader.cs#L345)).
  - With the digest in the argument it does not match any more. The in-app exclusion would have to be a dedicated report working directory, the same pattern as
    [`IsSkillAnalysisSession`](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/SessionIndex.cs#L125) and Evolve's working directory
    ([SkillAnalyzer.cs L82-L83](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/SkillAnalyzer.cs#L82-L83)), or deletion of the session.
- **The `.cmd` shim truncates multi-line arguments (run 9):** a multi-line `-p` value dropped every argument after the first newline (`--silent`, `--model`, `--session-id`, `--usage-output-file`). The run printed stats and created a session with a random id on the default model. The digest must be one line.
  cmd.exe batch files also have a line-length limit (about 8,191 characters). **I did not test the limit**, so it is an expected constraint, not a measurement. Windows' own limit is about 32,767 characters. Real sessions are far larger than a 276-character toy, so the digest has to be a compressed one: user prompts, the compaction recap and the metrics.
- **Quality (indicative only, a trivial session):** the run 10 report was comparable to the baseline report. The claim is weak because the session was two turns long (assumption A6).
- **Tokens and cost:** run 10: `totalPremiumRequestCost: 1`, about 36k input and 347 output tokens. Baseline: 1 premium request, about 36k input and 394 output tokens. Most of the input is the user's plugin context, which loads even with `--no-custom-instructions`.
- **`--attachment`:** rejected a `.txt` file ("must be an image or native document") and still left an empty session folder behind.
- **Code cost in #57:** the issue says the summary is "built by `CopilotSessionReader`". The reader has no such builder: it emits metrics, `SkillPrompts` and a compaction recap
  ([CopilotSessionReader.cs L120-L141](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/CopilotSessionReader.cs#L120-L141)).
  The digest builder is new code (size M: building, escaping to one line, a length budget, tests). The process wiring is not new: `ProcessSpec` already takes an input and a working directory.
  It would not use the input, since stdin is ignored. The report working directory and its cleanup are new as well.

### Q3. Option (c): temp `COPILOT_HOME`

Answer: infeasible without copying auth material from the real home. Feasible only with that copy.

```powershell
# run 2: empty temp home, new session id, pinned model
$env:COPILOT_HOME = '<scratch>\home-empty'   # set for that one child process only
copilot -p 'list three prime numbers' -s --no-custom-instructions --disable-builtin-mcps --deny-tool shell --deny-tool write --deny-tool memory --model claude-sonnet-4.6 --session-id <id>
```

- Output: `Execution failed: Model "claude-sonnet-4.6" is not available.`
- The process log of that run says `server policy fetch skipped: no authenticated GitHub host available`. The same model id was also "not available" in the real home, which was authenticated (`latest_model_list_fetch_succeeded: true` in its log), so the model name was a separate problem. The empty home's lack of authentication is a distinct finding.
- Step 3's isolation test: `COPILOT_HOME` does isolate session state. A session folder was created under the temp home and nothing in the real `session-state` changed. Auth does not carry over: the empty home has none.
- No token environment variables were set in the shell (names checked, values never read), so the real home's auth is a stored login, not an env token. Which stored file or credential-store entry the CLI needs was **not determined**.
  A candidate from the docs is the plain-text credential fallback under the home. I did not copy or read any, because the last-resort credential copy was not needed to answer Q3.
- **Security cost of (c):** copying auth material into a temp folder (and deleting it on every exit path). A login under a temp home creates a new OAuth grant, so it is not an alternative.
- **Cost in #57 if accepted anyway:** `ExecuteAsync` has to copy before the run and delete on every exit path, not only the success one: cancel, timeout and failure
  ([ReportCoordinator.cs L262-L287](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Reports/ReportCoordinator.cs#L262-L287)),
  with the run started at [L220](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Reports/ReportCoordinator.cs#L220) and the env built at
  [L294-L300](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Reports/ReportCoordinator.cs#L294-L300).
  The copy source is the configured `SessionInsightsOptions.CopilotHome` (service-account case). A copy of a live session can catch a half-written last line of `events.jsonl` (noted, not tested). The largest `events.jsonl` in the real home was about 59 MB, so copy time and disk are a real cost.
- Not run: step 6 (resume under a temp home with a copied session). It could not start without auth.

### Q4. Option (d): disable Copilot reports

Answer: the user-visible impact is small and fully in the UI, and there is no refusal path today.

Read at the pinned sha, no CLI:
- `ReportPanel` is rendered for every session in the session detail view. It shows "Generate session report" or "Re-run analysis" and has no per-CLI condition
  ([ReportPanel.tsx](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/frontend/src/features/reports/components/ReportPanel.tsx)).
- Starting a report goes `StartReportCommand` to `ReportCoordinator.StartAsync`, which refuses only an invalid id, an unknown session or a run already active
  ([ReportCoordinator.cs L94-L131](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Reports/ReportCoordinator.cs#L94-L131)).
  There is no CLI-based refusal. A refusal would be new code in `StartAsync` (a validation or unprocessable error for `session.Source == Copilot`).
- The 409 case has a toast ("A report is already running"). Other errors go through `toastApiError(error, "Could not start the report")` in `useGenerateReport`. A refusal would therefore show as a toast, but the button would still look usable.
  A clean version also disables or hides the button for Copilot sessions (size S).
- Existing `v<n>.md` reports stay readable: the list, content and download queries do not depend on the CLI.
- Cost: S. Reversible. The history effect is none.

### Q5. What is left in the user's history and what is in context

See the table in section 5. Evidence for each cell is in the appendix. Owner checks are marked.

## 5. Q5 table

| Option | Left in user history | In context on next resume of the real session | Visible in Copilot's own resume list | Visible in this app | Premium requests per report | #57 code cost | Reversibility |
|---|---|---|---|---|---|---|---|
| Baseline (today): resume in place | The report prompt and answer appended to the real session's `events.jsonl` (measured: 20 to 32 events after the report run) | Yes, the report is in context (probe answered that it was the last thing produced) | The real session itself, with the report in it | Hidden by ADR-0002 layer 5 | 1 | none | None: an appended turn cannot be undone |
| (a) fork/copy/no-persist flag | n/a: no such flag in 1.0.114 or 1.0.116 | n/a | n/a | n/a | n/a | n/a | n/a |
| (b) no resume, digest in `-p`, dedicated cwd | A new throwaway session per report. The real session is untouched | No (the report is in the other session) | Yes: the new sessions are listed (owner check: sessions from no-resume runs appear, named from the prompt) | Excluded by cwd, not by the exact-prompt rule | 1 | M | Delete the throwaway session folder by id (reported, not auto-reverted: index rows and config stay) |
| (b) with stdin summary | As (b) | As (b) | As (b) | Excluded by the exact-prompt rule (first message equals the prompt) | 1 but the model never sees the digest | n/a: does not work | n/a |
| (c) copy to temp `COPILOT_HOME` | A session in the temp home, deleted after. Needs copied auth | No | No, if the home is deleted | not applicable | 1 | L | Delete the temp home. Credential copy must be cleaned on every exit path |
| (d) disable Copilot reports | Nothing | Nothing | Nothing | The button is disabled or refused | 0 | S | Reversible |
| Considered and rejected: resume in place, then truncate `events.jsonl` back | Appended turn removed, with a race against the user's live session | Unknown | Same as baseline | Same as baseline | 1 | M | Edits a file Copilot owns while it may be open. Rejected (do not re-propose) |

Owner checks (the owner opened Copilot's own resume picker in the real home; no agent run did):
- All four named throwaway sessions appeared in Copilot's own resume list: the prompt-named baseline throwaway, the "report with transcript" session, and the two sessions named after the report prompt. Sessions created by no-resume runs are visible in Copilot's history, and so is the resumed throwaway whose history gained the report turn.
- Unrelated real sessions are listed alongside. Their titles and paths are intentionally not recorded here.
- Not observed from the owner's screenshot: whether the empty session from the failed `--attachment` run was listed (help says unnamed sessions with no messages are hidden).
- The picker's Esc started a new blank session in the scratch cwd (a session folder with a `workspace.yaml` and no `events.jsonl`, created at the time of the owner's check). It was deleted at cleanup.

## 6. Recommendation

**Recommended: (b), cost M, 1 premium request per report, conditional on a length test.**

- It is the only option that stops the report landing in the user's real session and still produces a report. (a) does not exist, (c) needs credential copying, and (d) removes the feature.
- It reuses patterns already in the code: a dedicated working directory excluded from the index, as Evolve does.
- It costs a one-line digest builder and leaves a throwaway session per report in Copilot's own list, which the owner has seen is visible. Deleting those by id is safe, and it is #57's decision whether to do so.
- Before implementing, #57 should test, on a realistic large session, that a digest fits through `-p` and the `.cmd` shim. If it cannot, the fallback is (d).

ADR-0002 impact if (b) is accepted: the context line "Copilot resumes in place" no longer holds, and the exact-prompt heuristic no longer catches the report session. See the amendment text.

## 7. Proposed ADR-0002 amendment (pure append, not applied)

Proposed block to append to `docs/adr/0002-report-forks-never-listed.md`. It edits no existing text:

```markdown
## Amendment (Copilot, proposed by #53)

Date: <set when accepted>. Status: Proposed.

- Supersedes the context statement "Copilot resumes in place, so its report turn pollutes the metrics of the original instead":
  Copilot reports no longer resume the user's session. They start a new session with a digest of the session inside `-p`, in a dedicated report working directory.
- Extends layer 2 (the exact-prompt heuristic): for Copilot it no longer identifies the report session, because the first user message is the prompt plus the digest.
  The report session is hidden from the index by its working directory (the same rule as the skill-analysis sessions) instead.
- Layer 5 (excluding the report turn from the original session's metrics) stays for sessions reported before this change.
- The report session is still visible in Copilot's own resume list. This is accepted, and #57 may delete it by id after the run.
- Requires `copilot` 1.0.116 behaviour: stdin is ignored with `-p`, and multi-line `-p` values are truncated by the `copilot.cmd` shim, so the digest is one line.
```

## 8. Observations for the owner (not fixed here)

- **Evolve's Copilot lockdown probably fails on this version.** [SkillAnalyzer.cs L70](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/SkillAnalyzer.cs#L70)
  passes `--deny-tool "*" --deny-url "*"`. Run 1 passed `--deny-tool '*'` to 1.0.114 and exited 1 with `Invalid --deny-tool value. Error: Invalid rule format: *`.
  One run. `--deny-url '*'` was not tested. The orchestrator files a separate issue.
- **Evolve's piped input is probably ignored.** The same code pipes its examples on stdin
  ([L82](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/SkillAnalyzer.cs#L82)). Run 7 showed `-p` ignoring stdin. Not tested on Evolve itself.
- **Evolve sessions are visible in Copilot's own list (owner check).** The owner's picker showed an Evolve-created session (prompt "Find repeated task patterns in the following user requests...", in the Evolve working directory, created six days earlier). Evolve's Copilot runs therefore also leave sessions in the user's history.
- **`copilot.cmd` shim:** a multi-line `-p` value truncates the argument list. This applies to any caller that locates `copilot` through the npm shim, including `ReportCoordinator` if it ever builds a multi-line prompt.
- **The CLI auto-updated mid-experiment** (1.0.114 to 1.0.116). Findings from run 4 onward are on 1.0.116.
- **Real-home runs load the user's plugins and hooks** even with `--no-custom-instructions`, so the reports also contained an unrelated plugin notice.

## 9. Evidence appendix

Commands are shown with flags as run. Paths are redacted; `<scratch>` is a throwaway directory under the local application data folder.

Runs (counter 10 of 14):

| # | Purpose | Home | Result |
|---|---|---|---|
| 1 | step 3 with `--deny-tool '*'` | empty temp | exit 1: `Invalid rule format: *` (no model call) |
| 2 | step 3 retry, `claude-sonnet-4.6` | empty temp | exit 1: model not available. Session folder created in the temp home. No auth |
| 3 | step 3 in the real home, `claude-sonnet-4.6` | real | exit 1: model not available. A session folder was still created |
| 4 | first turn of the throwaway, `claude-sonnet-5.5`, `--resume=<id>` | real | exit 0: `Three primes: 2, 3, 5.`; 1 premium request |
| 5 | baseline report, F3 plus pin plus lockdown | real | exit 0; events 20 to 32; 1 premium request |
| 6 | probe: "What was the last thing you produced...?" | real | exit 0: answered that it was the markdown report |
| 7 | option (b), stdin | real | exit 0; stdin ignored; first message == prompt |
| 8 | option (b), `--attachment` `.txt` | real | exit 1: file type not supported (no model call). Left an empty session folder |
| 9 | option (b), multi-line prompt | real | exit 0 but args truncated by `copilot.cmd`; default model; random session id |
| 10 | option (b), single-line digest in `-p` | real | exit 0; digest used; 1 premium request |

Flags used on runs 4 to 10: `--no-custom-instructions --disable-builtin-mcps --deny-tool shell --deny-tool write --deny-tool memory --model claude-sonnet-5.5`
(deviation from F3 and from Evolve: `'*'` is not a valid deny rule on this CLI, and `-p` auto-denies unapproved tools).

Event types in the throwaway's `events.jsonl` after the report run (counts only, no raw lines):
`assistant.message=2, assistant.turn_end=2, assistant.turn_start=2, hook.end=4, hook.start=5, instruction.update=1, session.error=1, session.model_change=1, session.resume=2, session.shutdown=3, session.start=1, session.tool_baseline=1, session.usage_checkpoint=2, session.usage_record=2, system.message=1, user.message=2`.
After the probe it had 44 event lines and three `user.message` entries: the first turn, the report prompt and the probe.

Model choice: the model in the user's own recent session event logs, by count of `model` fields: `claude-sonnet-5.5` far ahead of the next ones. It is Sonnet-class mid-tier, neither flagship nor small.

Snapshot result (real home, the effective CLI home):
- Before: 4,485 files, 102 directories under `session-state` (101 sessions plus the CLI's lock directory).
- Experiment-created session folders: 5 by agent runs (one per scratch working directory), plus one blank session from the owner's picker check. All 6 were deleted by recorded id after the owner check. After cleanup, 101 session folders remain, matching before.
- `session-state` difference before vs after: empty (no real session added, changed or removed).
- Outside `session-state`, changed or touched and **not reverted** (Copilot-owned, not edited by hand): `config.json` (changed, probably trusted-folder or run bookkeeping; contents not read), `session-store.db` and its `-shm` / `-wal` (the live session index, which holds index rows for the deleted sessions), `open-sessions-state.json` and one new `sidebar-sessions-state` file (written by the owner's picker check), and about 19 `process-*.log` files (new logs from the runs and old ones removed by the CLI's own retention).
- Attribution: every run window is recorded in the scratch log. The `open-sessions-state.json`, sidebar file, blank session and one log fall after the last run and are attributed to the owner's check. `config.json` was changed during both the experiment and the owner's check and is not attributed to one.
- Process checks before the snapshot, before step 3, before each model run and after: 0 Copilot or backend processes.
- The temp home (with a stray session created by run 2) and the scratch working directories were removed and verified gone.
- The credential copy (last resort) was not used.
