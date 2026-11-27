# ADR-0003 – Continue in CLI launches a terminal from a fixed, server-built command

## Status

Accepted (2026-01-12)

## Prologue (Summary)

In the context of the detail page reopening a session in its CLI, which makes a web request start a process on the user's desktop, facing the rule that the loopback-only, token-protected API must never become a way to run arbitrary commands, we decided for a launch the server builds from a fixed command, where the client sends only a GUID session id and a `force` flag and the terminal comes from a fixed list, to achieve that no client input other than the GUID reaches a shell string, accepting that a session idle for more than 5 minutes but still open is not detected and that the dashboard cannot see whether the CLI started.

_Prologue added when this record was converted to the Alexandrian template (#102). It restates the record below and changes no decision._

## Discussion (Context)

The detail page can reopen a session in its CLI (`copilot --resume=<id>` / `claude --resume <id>`) in a terminal at the
session's folder. That means a web request makes the backend start a process on the user's desktop. The API is
loopback-only and token-protected (ADR-0001), but it must still never become a way to run arbitrary commands.

## Solution (Decision)

1. The client sends only a session id and a `force` flag. The id must be a GUID, and the folder and CLI come from the
   server's own session index and configuration.
2. The terminal is chosen from a fixed enum (`wt-pwsh`, `wt-powershell`, `wt-cmd`, `pwsh`, `powershell`, `cmd`).
   Unknown values are rejected with 400; options not installed are disabled and fall back to the default.
3. Windows Terminal receives the folder and shell as separate `ArgumentList` entries. Plain windows use
   `ShellExecute` with the working directory set. The shell command line is the only composed string. It holds the
   configured CLI plus fixed flags and the GUID, and each part is single-quoted for PowerShell.
4. A CLI path with shell metacharacters (`" & | < > ^ %`) or a folder containing `;` (a Windows Terminal separator) is refused.
5. Resuming a session that is open elsewhere can corrupt its history, so: a running report for the session blocks the
   launch outright, and activity within 5 minutes needs an explicit confirmation (`force`).

## Consequences (Results)

- No client input reaches a shell string other than the GUID.
- A session idle for more than 5 minutes but still open in a terminal is not detected.
- The launched window is detached: the dashboard cannot see whether the CLI started successfully.
