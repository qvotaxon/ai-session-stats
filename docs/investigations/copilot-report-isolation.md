# Investigation: isolating Copilot report runs from the user's real session

Status: Proposed
Recommended option: (b) no resume, a digest of the session sent to a fresh Copilot run in a dedicated report working directory. Conditional: session text must not pass through `cmd.exe`, and #57's design needs a security review first (see section 6).
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
3. Stdin together with `-p` was ignored in one run (run 7). Stdin as the only prompt source (no `-p`) was **not tested**.
   Putting the digest in the `-p` argument worked on a single-line toy (run 10), but that path has a command-injection risk through the `copilot.cmd` shim that the backend uses (Q2 and section 6), and a multi-line value lost every later argument in run 9 (mechanism not determined).
4. Option (c) is **inferred** infeasible without copying auth material: one failed run under an empty temp `COPILOT_HOME` plus one log line, not confirmed by a run with the working model (Q3). A confirming run would either make (c) a candidate without credential copying, pending step 6 (resuming a copied session under a temp home, not run); feasibility and final cost stay open until step 6 passes, or confirm it needs auth material; it would not make (c) the fallback.
5. `--deny-tool '*'` was rejected in one run on 1.0.114 (not re-run on 1.0.116). Evolve's current flags may therefore fail (observation, section 8).

**Recommended option: (b)**, cost M (a digest builder plus a report working directory), about 1 premium request per report
on the toy session, **conditional on all of these**:

- Session text (user prompts, the compaction recap) must **not** be passed as an argument through `cmd.exe`. The backend resolves `copilot` to `copilot.cmd` and `cmd.exe` re-parses its arguments (the BatBadBut class). Either start the node loader or the native binary directly, or send the prompt on stdin without `-p`. Neither was tested here.
- #57 tests "stdin as the only prompt source" first (documented, not proven), because it avoids the length limit, the newline truncation and the injection risk, and reuses `ProcessSpec`'s input.
- #57's design gets a security review (a threat model of the argv path) before implementation.
- Quality: a digest without assistant actions or outcomes cannot describe what was done in a real session. Real-session quality was not measured.

If these cannot be met, the fallback is (d), disable Copilot reports, which is the only option with no history effect.
The owner picks. `Accepted option` is left empty on purpose.

## 2. Environment and version pin

- Copilot CLI: `1.0.114` at the start (`copilot --version` printed `GitHub Copilot CLI 1.0.114.`). It auto-updated to `1.0.116` during the window. `copilot --version` after the experiment printed `GitHub Copilot CLI 1.0.116.`
  The per-run process logs show `1.0.114` for the log of run 3 and `1.0.116` for the logs of runs 4 to 10 (inferred from the log version strings). Runs 1 to 3 used 1.0.114.
  The help text was read at 1.0.114. Neither version's help output was saved, so 1.0.116 was **not re-checked for the record**.
- Model pinned on runs 4 to 8 and 10: `claude-sonnet-5.5` (the model the user's own sessions use most; Sonnet-class mid-tier). Run 9 lost its `--model` argument and ran on the default model (which reported itself as Sonnet 5.5). Runs 1 to 3 asked for `claude-sonnet-4.6`.
  `claude-sonnet-4.6` is in the CLI's configuration list but was reported "not available" for this account.
- OS: Windows 11. In PowerShell `Get-Command copilot -All` lists `copilot.ps1` (ExternalScript), then `copilot.cmd`, then an extensionless `copilot`, all in the npm directory. The experiment helper started the first *Application* result, which is `copilot.cmd`, through `ProcessStartInfo` (from the helper script; the resolved file was not logged per run). The backend's `ExecutableLocator` also picks `copilot.cmd`
  ([ExecutableLocator.cs L11](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Reports/ExecutableLocator.cs#L11)).
  The npm `copilot.cmd` ends in `node ... npm-loader.js %*`, so `cmd.exe` re-parses the arguments.
- Code cited at origin/main `c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2`.
- Spend: 10 counted runs of a cap of 14. Runs 1, 2, 3 and 8 failed before a model call. A model run cost `totalPremiumRequestCost: 1`, about 36k input and 350 to 420 output tokens, and about 5 AI credits.
- #57 must re-run `copilot --help` before implementing: everything here is tied to these versions.

Owner observations are marked "(owner check)": they come from the owner's own look at Copilot's resume picker, not from an agent run.

## 3. Method and safety

- Launch path: the version and help commands were typed in PowerShell. Every `copilot` run that sent a prompt (runs 1 to 10) was started by one PowerShell helper script that used .NET `ProcessStartInfo` with an argument list, the first Application result of `Get-Command copilot` (`copilot.cmd`), an explicit per-call `COPILOT_HOME` (or none), empty stdin unless piping, a 5-minute timeout, and a pinned model on the model runs (except run 9). The helper script is not committed. Per run, the helper recorded the arguments in the scratch log. The helper also added `--usage-output-file <file>` to every run.
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
- **Version scope:** no fork, copy or no-persist flag was seen in `copilot --help` at 1.0.114. That help output was observed in the session, but no capture was saved (only a one-line note in the local scratch log). The conclusion about option (a) therefore rests on the official reference keyword search cited below. 1.0.116 was **not re-checked for the record** (see section 2).
- **Official reference:** the GitHub Copilot CLI command reference (<https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-command-reference>) was fetched and searched by the implementer on 2026-11-27, and read earlier in a review round. Searching for `fork`, `ephemeral` and `no-session-persistence`, I found `/fork [NAME]` only as an interactive slash command, and no option that forks, copies or avoids persisting a resumed session. The searches were keyword searches, not a full read of the page.
  The same fetch confirmed the stdin wording used in Q2 (`--fleet`: "Combine with `-i`, `-p`, or piped standard input"; `--trust-folder`: "one new non-interactive session (`-p` or piped standard input)"; and under `copilot workflow run`: "Piped standard input is not used as a prompt or as workflow arguments").

### Q2. Option (b): no resume, summary on stdin

Answer: stdin together with `-p` was ignored (run 7, one run). Stdin alone (without `-p`) was **not tested**. A one-line digest inside `-p` worked (run 10), but that path depends on the section 6 conditions (the `cmd.exe` argument-injection risk).

```powershell
# Both runs were started by the helper script (ProcessStartInfo, argument list, copilot.cmd), not typed as a command line.
# The argument lists, as recorded in the scratch log:

# run 7, scratch cwd "cwd-b", no resume; the transcript file was written to the child's stdin
$args7 = @('-p','Give me a session report in markdown format','--silent','--no-custom-instructions','--disable-builtin-mcps',
  '--deny-tool','shell','--deny-tool','write','--deny-tool','memory','--model','claude-sonnet-5.5',
  '--session-id',$id,'--usage-output-file',$usageFile)

# run 10, scratch cwd "cwd-e", summary inside the argument (single line); no stdin
$prompt = 'Give me a session report in markdown format. Transcript of the session to report on: USER: list three prime numbers | ASSISTANT: Three primes: 2, 3, 5.'
$args10 = @('-p',$prompt,'--silent','--no-custom-instructions','--disable-builtin-mcps',
  '--deny-tool','shell','--deny-tool','write','--deny-tool','memory','--model','claude-sonnet-5.5',
  '--session-id',$id,'--usage-output-file',$usageFile)
```

- **Stdin together with `-p` (run 7, one run):** the report had no trace of the transcript, and the new session's first `user.message` was exactly the prompt. So `-p` ignored piped input when both were given.
  **Not tested: stdin as the only prompt source (no `-p`).** The CLI reference documents piped standard input as a prompt source (for example in the `--fleet` and `--trust-folder` options), and another line says workflows do not use it as a prompt, so this is documented, not shown to work.
  Sending the prompt and digest on stdin without `-p` would avoid the argv length limit, the newline truncation and the injection risk below, and it reuses `ProcessSpec`'s input ([SkillAnalyzer.cs L82](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/SkillAnalyzer.cs#L82)). It is the first thing #57 must test.
- **Argument (run 10):** the report used the transcript. The first `user.message` was the prompt plus the digest (151 characters, not an exact match of the prompt).
- **New session on disk:** yes, in both cases. The folder is `session-state/<id>` in the Copilot home, `workspace.yaml` carries the cwd of the report run, and its name is the prompt (plus the digest in run 10).
- **Exclusion in this app:**
  - With stdin together with `-p` (run 7), `LooksLikeReportFork` would catch the session (first human message equals the prompt:
    [CopilotSessionReader.cs L345](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/CopilotSessionReader.cs#L345)).
  - With the digest in the argument it does not match any more. The in-app exclusion would have to be a dedicated report working directory, the same pattern as
    [`IsSkillAnalysisSession`](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/SessionIndex.cs#L125) and Evolve's working directory
    ([SkillAnalyzer.cs L82-L83](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/SkillAnalyzer.cs#L82-L83)), or deletion of the session.
- **Security risk of the argument path (from code, not from a run):** option (b) puts session-derived text (user prompts, the compaction recap) into `-p`. The backend's `ExecutableLocator` tries `.exe`, `.com`, `.cmd`, `.bat` and picks the npm `copilot.cmd`
  ([ExecutableLocator.cs L11](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Reports/ExecutableLocator.cs#L11)).
  `ProcessRunner` passes an `ArgumentList` ([ProcessRunner.cs L29-L32](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Reports/ProcessRunner.cs#L29-L32)), but a `.cmd` runs under `cmd.exe`, which parses the command line again through `%*`.
  That is the BatBadBut class: `& | ^ < > "` and `%VAR%` in a prompt can run commands as the user, and `%VAR%` expands environment variables of the backend into the prompt.
  The prompt text also persists as the new session's name in Copilot's own list (run 10's session was named after its prompt). User prompts routinely contain these characters. I did not exploit or test any of this.
  The #52 plan records the same mechanism (threat T3) and the invariant "no variable argument through the shim". Option (b) via `-p` breaks that invariant, so it needs the conditions in section 6.
  I did **not** test any mitigation (starting node and the loader directly, the native binary, or stdin), so none is claimed.
- **Multi-line `-p` lost later arguments (run 9, mechanism not determined):** a multi-line `-p` value dropped every argument after the first newline (`--silent`, `--model`, `--session-id`, `--usage-output-file`). The run printed stats and created a session with a random id on the default model.
  What is known: the experiment helper started `copilot.cmd` through `ProcessStartInfo` (see section 2), and `cmd.exe` ends a batch command line at a newline. What is **not** known: where the truncation happened, because I did not isolate it (for example by running the node loader directly). I also did not run it through the backend's own `ExecutableLocator` and `ProcessRunner`. A one-line digest worked in run 10.
  #57 must test multi-line and long prompts through the backend's real path (`ExecutableLocator` plus `ProcessRunner`). My helper used a similar launch (`ProcessStartInfo` on `copilot.cmd`), but it was not that code, so it does not replace the test.
  The `cmd.exe` batch line limit (about 8,191 characters) was also **not tested**. It is an expected constraint, not a measurement.
- **Quality (indicative only, a trivial two-turn session):** the run 10 report was comparable to the baseline report.
  **Structural loss, not measured:** a resumed report sees the whole conversation (assistant turns, tool calls and results, edits, errors). The proposed digest (user prompts, the compaction recap and the metrics) carries no assistant actions, tool results or outcomes, so a report on a real session built from it cannot say what was done. Real-session quality was not measured.
- **Tokens and cost:** run 10: `totalPremiumRequestCost: 1`, about 36k input and 347 output tokens. Baseline: 1 premium request, about 36k input and 394 output tokens. Most of the input is the user's plugin context, which loads even with `--no-custom-instructions`.
  On a large real session the baseline's input would include the resumed context, so (b) is probably cheaper per report. This is an estimate: only the two-turn toy was measured.
- **`--attachment`:** rejected a `.txt` file ("must be an image or native document") and still left an empty session folder behind.
- **Code cost in #57:** the issue says the summary is "built by `CopilotSessionReader`". The reader has no such builder: it emits metrics, `SkillPrompts` and a compaction recap
  ([CopilotSessionReader.cs L120-L141](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/CopilotSessionReader.cs#L120-L141)).
  The digest builder is new code (size M: building, a length budget, tests; the process start itself must not go through `cmd.exe`, see section 6). The process wiring is not new: `ProcessSpec` already takes an input and a working directory.
  If stdin without `-p` works, it would use that input. The report working directory and its cleanup are new as well. #57's design needs a security review (see section 6).

### Q3. Option (c): temp `COPILOT_HOME`

Answer: **INFERRED, not confirmed**: infeasible without copying auth material from the real home.

What was observed: one run under an empty temp home (run 2) failed with `Model "claude-sonnet-4.6" is not available`, and its process log had the line `no authenticated GitHub host available`. The process log is in the real Copilot home and is not part of this document's evidence.
What was not observed: any run under an empty home with the model that works (`claude-sonnet-5.5`), so the authentication failure was never seen as the cause of a failed model call. The same model id was also "not available" in the authenticated real home (run 3), so "model not available" alone does not show an auth failure.
What one confirming run needs: the working model under an empty temp home, with the owner's go (it may start a login prompt, which must be aborted). That is an open item for the owner, not a finding. Because this verdict is what rules out (c), the only producing option that leaves nothing in Copilot's list, (c) is not dismissed on this evidence alone. If the confirming run and step 6 both succeed, the owner revisits the recommendation in section 6.
What the confirming run would change: if the pinned working model runs under the empty home, (c) becomes a candidate without copying credentials, pending step 6 (resuming a copied session under a temp home, not run); feasibility and final cost stay open until step 6 passes. If it fails with an authentication error, (c) is infeasible without copying auth material. Either way this does not make (c) the fallback: (d) stays the fallback in section 6.

```powershell
# run 2, started by the helper script (ProcessStartInfo, argument list, copilot.cmd): empty temp home, new session id.
# The helper set COPILOT_HOME for that one child process only (psi.Environment), not in the shell.
# Argument list as recorded in the scratch log:
$args2 = @('-p','list three prime numbers','-s','--no-custom-instructions','--disable-builtin-mcps',
  '--deny-tool','shell','--deny-tool','write','--deny-tool','memory','--model','claude-sonnet-4.6',
  '--session-id',$id,'--usage-output-file',$usageFile)
# child environment: COPILOT_HOME = '<scratch>\home-empty'
```

- Output: `Execution failed: Model "claude-sonnet-4.6" is not available.`
- The process log of that run says `server policy fetch skipped: no authenticated GitHub host available`. That wording is from a managed-settings component, so it is a hint about authentication, not proof. The real home's log reported `latest_model_list_fetch_succeeded: true`.
- Step 3's isolation test (observed): `COPILOT_HOME` does isolate session state. A session folder was created under the temp home and nothing in the real `session-state` changed.
- No token environment variables were set in the shell (names checked, values never read), so the real home's auth is a stored login, not an env token. Which stored file or credential-store entry the CLI needs was **not determined**.
  A candidate from the docs is the plain-text credential fallback under the home. I did not copy or read any, because the last-resort credential copy was not needed to answer Q3.
- **Security cost of (c):** copying auth material into a temp folder (and deleting it on every exit path). A login under a temp home creates a new OAuth grant, so it is not an alternative.
- **Cost in #57 if accepted anyway:** `ExecuteAsync` has to copy before the run and delete on every exit path, not only the success one: cancel, timeout and failure
  ([ReportCoordinator.cs L262-L287](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Reports/ReportCoordinator.cs#L262-L287)),
  with the run started at [L220](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Reports/ReportCoordinator.cs#L220) and the env built at
  [L294-L300](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Reports/ReportCoordinator.cs#L294-L300).
  The copy source is the configured `SessionInsightsOptions.CopilotHome` (service-account case). A copy of a live session can catch a half-written last line of `events.jsonl` (noted, not tested). The largest `events.jsonl` in the real home was about 59 MB, so copy time and disk are a real cost.
- Not run: step 6 (resume under a temp home with a copied session), because the inferred missing auth made it unrunnable without a credential copy. No other run under a temp home was made, although 4 counted runs were left under the cap.

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
| Baseline (today): resume in place | The report prompt and answer appended to the real session's `events.jsonl` (measured: 20 to 32 events after the report run) | Yes, the report is in context (probe answered that it was the last thing produced) | The real session itself, with the report in it | The session stays listed. ADR-0002 layer 5 only removes the report turn from the metrics (tokens, tools, hooks, turns, subagents); cost, API duration and API request count come from shutdown totals and include the report turn once a shutdown follows it ([CopilotSessionReader.cs L294-L297](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/CopilotSessionReader.cs#L294-L297), [L299-L300](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/CopilotSessionReader.cs#L299-L300), [L338](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/CopilotSessionReader.cs#L338)); last activity, end time, duration and the timeline extent include its timestamps once a shutdown follows it, because `acc.Seen` runs on every event ([L112](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/CopilotSessionReader.cs#L112), [L285-L287](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/CopilotSessionReader.cs#L285-L287), [L319-L321](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/CopilotSessionReader.cs#L319-L321)). It is not dropped as a fork, because its first human message is the real one (`SessionIndex` drops only excluded ids, report forks and skill-analysis cwds, [L37](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/SessionIndex.cs#L37)) | 1 | none | None: an appended turn cannot be undone |
| (a) fork/copy/no-persist flag | n/a: no such flag seen in `--help` at 1.0.114 (help output observed in the session, not saved; 1.0.116 not re-checked for the record) | n/a | n/a | n/a | n/a | n/a | n/a |
| (b) no resume, digest in `-p`, dedicated cwd | A new throwaway session per report. The real session is untouched. The new session's name and first message carry the digest, which is copied from the user's prompts, as a titled entry in Copilot's own list (owner-observed for the run 10 style session). Per the official CLI reference cited in Q1, `--continue` is documented as "Resume the most recent session in the current working directory, falling back to the globally most recent session". The fallback is documented, and after a (b) report the report session would be the most recent one | No (the report is in the other session) | Yes: the new sessions are listed (owner check: sessions from no-resume runs appear, named from the prompt) | Excluded by cwd, not by the exact-prompt rule | 1 on the toy session. Probably less on a real session (estimate, not measured) | M, plus a security review (injection through `cmd.exe`, section 6) | Delete the throwaway session folder by id (the real-home index rows and config stay, as the cleanup of this investigation showed) |
| (b) with stdin and `-p` together | As (b) | As (b) | As (b) | Excluded by the exact-prompt rule (first message equals the prompt) | 1, but the model did not see the digest (one run) | n/a: not usable as tested | n/a |
| (b) with stdin only (no `-p`) | Not tested | Not tested | Not tested | Not tested | Not tested | Probably M, smaller than the argument path | Not tested. First thing #57 tests |
| (c) copy to temp `COPILOT_HOME` | A session in the temp home, deleted after. Probably needs copied auth (inferred, Q3) | No (estimated, pending step 6) | No, if the home is deleted | not applicable | 1 (estimated, pending step 6) | L (estimated, pending step 6) | Delete the temp home. Credential copy must be cleaned on every exit path |
| (d) disable Copilot reports | Nothing | Nothing | Nothing | The button is disabled or refused | 0 | S | Reversible |
| Considered and rejected: resume in place, then truncate `events.jsonl` back | Appended turn removed, with a race against the user's live session | Unknown | Same as baseline | Same as baseline | 1 | M | Edits a file Copilot owns while it may be open. Rejected (do not re-propose) |

Owner checks (the owner opened Copilot's own resume picker in the real home; no agent run did):
- All four named throwaway sessions appeared in Copilot's own resume list: the prompt-named baseline throwaway, the "report with transcript" session, and the two sessions named after the report prompt. Sessions created by no-resume runs are visible in Copilot's history, and so is the resumed throwaway whose history gained the report turn.
- Unrelated real sessions are listed alongside. Their titles and paths are intentionally not recorded here.
- Not observed from the owner's screenshot: whether the empty session from the failed `--attachment` run was listed (help says unnamed sessions with no messages are hidden).
- The picker's Esc started a new blank session in the scratch cwd (a session folder with a `workspace.yaml` and no `events.jsonl`, created at the time of the owner's check). It was deleted at cleanup.

## 6. Recommendation

**Recommended: (b), cost M, about 1 premium request per report on the toy session. Conditional, see below.**

- It is the only option tested to stop the report landing in the user's real session while still producing a report. (a) does not exist, (c) is inferred to need credential copying (not confirmed, Q3), and (d) removes the feature. If the confirming Q3 run and step 6 both succeed, the owner revisits this recommendation.
- It reuses patterns already in the code: a dedicated working directory excluded from the index, as Evolve does.
- It costs a digest builder, a loss of report quality on real sessions (the digest has no assistant actions or outcomes, not measured), and it leaves a throwaway session per report in Copilot's own list, which the owner has seen is visible. Deleting those by id worked in this investigation, but the index rows and config stay, and no picker check was made after deleting. Whether #57 deletes them is its own decision.

**Conditions (all required):**

1. **Session text must not pass through `cmd.exe`.** The backend resolves `copilot` to `copilot.cmd` ([ExecutableLocator.cs L11](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Reports/ExecutableLocator.cs#L11)) and `cmd.exe` re-parses `%*` (BatBadBut class). User prompts routinely contain `& | ^ < > "` and `%VAR%`, so passing them as an argument through the shim risks command execution as the user and environment expansion into a prompt that persists as a session name. This breaks the invariant recorded in #52's plan (threat T3: no variable argument through the shim).
   Either start the node loader or the native binary directly, or send the prompt on stdin without `-p`. **No mitigation was tested here.**
2. **Test stdin as the only prompt source first** (no `-p`). It is documented, not proven. It would avoid the length limit, the newline truncation and the injection risk.
3. **#57's design needs a security review** (a threat model of the argv path) before implementation. This is an open item for the owner.
4. **Test through the backend's real path:** multi-line and long prompts via `ExecutableLocator` plus `ProcessRunner`. My helper launched `copilot.cmd` through `ProcessStartInfo` but was not that code.
5. **Measure real-session quality** of a digest-based report against a resumed one before relying on it.

If these cannot be met, the fallback is (d).

Open items for the owner:
- Approve a security review for #57's design (condition 3).
- Decide whether one confirming run under an empty temp home with the working model is wanted for Q3 (it may start a login prompt, which must be aborted).

What a confirming Q3 run would change (open item above): if the pinned working model runs under an empty temp home, (c) becomes a candidate without copying credentials, pending step 6 (resuming a copied session under a temp home, not run); feasibility and final cost stay open until step 6 passes. If it fails with an auth error, (c) stays infeasible without copying auth material. It does not make (c) the fallback.

ADR-0002 impact if (b) is accepted: the context line "Copilot resumes in place" no longer holds, and for Copilot the report session is not caught by the exact-prompt check, although the code already applies that check to Copilot ([CopilotSessionReader.cs L345](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/CopilotSessionReader.cs#L345)): the one-line digest in the argument defeats it, so the new layer 6 (a report working directory excluded from the index) is needed. The amendment below proposes that layer.

## 7. Proposed ADR-0002 amendment (pure append, not applied)

Proposed block to append to `docs/adr/0002-report-forks-never-listed.md`. It edits no existing text:

```markdown
## Amendment (Copilot, proposed by #53)

Date: <set when accepted>. Status: Proposed.

- Supersedes the context statement "Copilot resumes in place, so its report turn pollutes the metrics of the original instead":
  Copilot reports no longer resume the user's session. They start a new session with a digest of the session, in a dedicated report working directory.
- Adds a new layer 6 (Copilot): the report session is started in a dedicated report working directory, and the index drops sessions whose cwd equals it (the same rule as the skill-analysis sessions). Layer 2 is not changed. If the digest is part of the first user message, the exact-prompt check (`LooksLikeReportFork`) no longer identifies the report session, so layer 6 is what hides it.
- Security constraint: session text (user prompts, the compaction recap) must not be passed as an argument through the `copilot.cmd` shim, because `cmd.exe` re-parses the arguments (BatBadBut class). The report run starts the node loader or the native binary directly, or sends the prompt on stdin without `-p`. The design needs a security review before implementation.
- Layer 5 (excluding the report turn from the original session's metrics) stays for sessions reported before this change.
- The report session is still visible in Copilot's own resume list. This is accepted, and #57 may delete it by id after the run.
- Open until tested by the implementing issue: whether stdin alone works as a prompt source, how multi-line and long prompts behave through the backend's own process path, and the quality of a digest-based report on a real session. This amendment does not depend on stdin being ignored.
```

## 8. Observations for the owner (not fixed here)

- **Evolve's Copilot lockdown may fail on this version (one run on 1.0.114).** [SkillAnalyzer.cs L70](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/SkillAnalyzer.cs#L70)
  passes `--deny-tool "*" --deny-url "*"`. Run 1 passed `--deny-tool '*'` to 1.0.114 and exited 1 with `Invalid --deny-tool value. Error: Invalid rule format: *`.
  One run, 1.0.114 only (not re-run on 1.0.116). `--deny-url '*'` was not tested. The orchestrator files a separate issue.
- **Evolve's piped input may be ignored.** The same code pipes its examples on stdin together with `-p`
  ([L82](https://github.com/qvotaxon/ai-session-stats/blob/c6cb4e8d0f72305fa3391b690bc7ce8d38abd9e2/apps/backend/src/Infrastructure/Sessions/SkillAnalyzer.cs#L82)). Run 7 showed one case of `-p` ignoring stdin when both were given. Not tested on Evolve itself.
- **Evolve sessions are visible in Copilot's own list (owner check).** The owner's picker showed an Evolve-created session (prompt "Find repeated task patterns in the following user requests...", in the Evolve working directory, created six days earlier). Evolve's Copilot runs therefore also leave sessions in the user's history.
- **Multi-line `-p` (run 9, mechanism not determined):** a multi-line `-p` value lost the arguments after the first newline when the experiment helper started `copilot.cmd`. It was not tested through the backend's `ExecutableLocator` and `ProcessRunner`, so whether `ReportCoordinator` would be affected is open.
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
| 7 | option (b), stdin | real | exit 0; stdin ignored together with `-p`; first message == prompt |
| 8 | option (b), `--attachment` `.txt` | real | exit 1: file type not supported (no model call). Left an empty session folder |
| 9 | option (b), multi-line prompt | real | exit 0 but later args were lost (started via `copilot.cmd`, truncation mechanism not determined); default model; random session id |
| 10 | option (b), single-line digest in `-p` | real | exit 0; digest used; 1 premium request |

Flags used on runs 4 to 10: `--no-custom-instructions --disable-builtin-mcps --deny-tool shell --deny-tool write --deny-tool memory --model claude-sonnet-5.5`
(deviation from F3 and from Evolve: `'*'` was rejected as a deny rule in one run on 1.0.114, and `-p` auto-denies unapproved tools per the help text). Run 9 lost its flags, see the table.

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
