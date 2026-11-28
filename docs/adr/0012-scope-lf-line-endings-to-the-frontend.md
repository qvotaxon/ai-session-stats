# ADR-0012 – Enforce LF line endings for the frontend with a scoped .gitattributes

## Status

Accepted (2026-11-28)

## Prologue (Summary)

In the context of the frontend's Prettier format check, which runs in CI and locally on Windows checkouts, facing the fact that Prettier expects LF
line endings while a checkout made with `core.autocrlf=true` has CRLF working copies, we decided for a `.gitattributes` containing
`* text=auto eol=lf` in `apps/frontend` only, to achieve LF working copies for the frontend on every platform without touching how the rest of the
repository is checked out, accepting that existing Windows checkouts need a re-clone or refresh and that the policy covers only `apps/frontend`.

## Discussion (Context)

Prettier's default `endOfLine` is `lf`, and the root `.editorconfig` sets `end_of_line = lf`. The blobs in git are LF. On Windows, `core.autocrlf=true`
turns them into CRLF in the working copy, so a local `pnpm format:check` reports files that are clean in git. #107 (PR #108) added the check to
CI and a README workaround; neither removes the cause.

Forces and non-goals:

- Prettier documents `* text=auto eol=lf` in a `.gitattributes` as the way to keep LF on Windows (https://prettier.io/docs/options#end-of-line).
- The Windows CI jobs (`backend`, `scripts`) and the release `installer` job may check out with `core.autocrlf=true`. A repository-wide rule could change
  what they see.
  > TODO: confirm whether these jobs check out with `core.autocrlf=true`.
- Non-goal: a line-ending policy for the backend, the scripts or the installer.

Options considered:

- **A repository-wide `.gitattributes`.** Rejected: it could change the checkouts of the Windows CI and release jobs, which this issue does not need.
- **Prettier `endOfLine: auto`.** Rejected: it stops the check from enforcing LF at all, so CRLF could be committed unnoticed.
- **Keep the README workaround only.** Rejected: it leaves the cause in place and asks every Windows contributor to remember it.

## Solution (Decision)

- Add `apps/frontend/.gitattributes` with `* text=auto eol=lf`.
- Add no other `.gitattributes`, and do not change Prettier's `endOfLine`.
- Existing Windows checkouts keep their CRLF files until refreshed. The README says how: re-clone, or delete and restore `apps/frontend`.

## Consequences (Results)

- Positive: `format:check` is clean on a fresh Windows checkout, and the CI check and the local check agree.
- Negative: contributors with an existing checkout must refresh it once. Line endings outside `apps/frontend` are unchanged and still follow each
  contributor's `core.autocrlf`.
- Follow-up, trigger: if the Windows CI and release jobs are shown to check out with LF, or CRLF problems appear elsewhere, a new ADR may widen the
  scope and supersede this one.
- This decision is reversed by deleting `apps/frontend/.gitattributes`, for example if Prettier's or the repository's line-ending policy changes.
