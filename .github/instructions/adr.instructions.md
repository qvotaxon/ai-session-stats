---
applyTo: "docs/adr/**"
---

# Architecture Decision Records

These rules apply to every file in `docs/adr/`, for people and for every AI agent, and need no plugin. Where a plugin skill says something else (for example the msdc-harness `adr` skill's `ADR-NNNN-` file names or its README index), these rules win.

## Template

- Every ADR follows `docs/adr/template.md`: an H1 title, then `## Status`, `## Prologue (Summary)`, `## Discussion (Context)`, `## Solution (Decision)` and `## Consequences (Results)`, each exactly once and in that order. Do not add, remove or rename sections.
- The one exception is an amendment that an issue explicitly asks for. Append it after `## Consequences (Results)`, and after any earlier amendment, as `## Amendment YYYY-MM-DD: <summary> (#<issue>)`, with the real date and never a placeholder.
- The H1 is `# ADR-NNNN – <title>`, with an en dash (–), where NNNN is the number in the file name. The title of a new ADR states the decision, not the topic. For an existing ADR, only the H1's format may be normalised (the number and the separator). Never change its title words, except where an issue says so.
- `## Status` holds one line: `Proposed`, `Accepted`, `Deprecated` or `Superseded by ADR-NNNN`, then the date of that status in brackets, for example `Accepted (2026-01-10)`. One short sentence may follow, for example `Proposed (2026-11-27). Becomes Accepted when the #99 pull request merges.` Leave out the date only where none was ever recorded; never invent one.
- The Prologue is one paragraph with five clauses: in the context of <use case>, facing <concern>, we decided for <option>, to achieve <quality>, accepting <downside>.
- Discussion gives the forces, the non-goals and the options considered, with the reason each rejected option lost. Solution says what we will do and what we will not do. Consequences gives the positive and negative results and follow-ups, each with an owner or a trigger.
- Write in English. Do not invent forces, options, dates or evidence; mark an unknown with `> TODO: confirm`. One decision per ADR.

## File name and number

- The file name is `NNNN-kebab-case-title.md` with a four-digit number, for example `0012-store-reports-as-json.md`. Never use `ADR-NNNN-...`. Do not create an index file.
- The number is one higher than the highest number that is in use or reserved:
  - in use: a file in `docs/adr/` on `origin/main`, or a file under `docs/adr/` in any open pull request (check each open PR's files, for example with `gh pr view <n> --json files`);
  - reserved: 0006, 0007, 0009, 0010.
- The pull request that uses a reserved number for its purpose removes it from the list above. Only the owner adds a reservation.
- Never renumber an existing ADR unless an issue says so (#51 renumbers the duplicate 0003 to 0005). A renumbering changes the file name and the H1 number in the same commit.

## Changing an existing ADR

- Never rewrite a decision. Once an ADR is `Accepted`, its Prologue, Discussion, Solution and Consequences stay as they are. Two exceptions exist, and each needs an issue that asks for it:
  - an appended amendment, as described above;
  - a conversion to this template. A conversion changes no decision, status, date, number or title wording. It only renames sections, normalises the H1's format, moves the Status and Date into `## Status`, and adds a Prologue that restates the record, followed by a one-line provenance note and any `> TODO: confirm` line; leave both in place.
- A changed decision gets a new ADR that supersedes the old one. The old ADR changes only its Status line, to `Superseded by ADR-NNNN (YYYY-MM-DD)`, and the two link to each other.
- A `Proposed` ADR may still be edited in the pull request that adds it, or where an issue asks for it.
- Change a status (for example `Proposed` to `Accepted`) only where the owner or an issue says so.
- CI's `adr-headings` check fails a pull request in which an ADR breaks a rule it checks: a missing, duplicated or out-of-order section, an H1 in the wrong format or with a number that differs from the file name, an invalid status line, a malformed amendment heading, or a wrong file name.
