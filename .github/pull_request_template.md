Refs #
<!--
Line 1 links the issue: `Refs #<n>` until the next line allows `Fixes #<n>`.
Change `Refs #<n>` to `Fixes #<n>` only when the review gate has passed and CI is green on the latest commit. Any commit after the review goes back to review.
The owner marks the pull request ready and merges it.
Agents: the "Repository conventions" section of AGENTS.md governs this pull request.
Title: a plain Conventional Commit with no prefix, for example `feat: show delivered points per sprint`. CI's pr-title check fails any other title.
With this repo's squash setting the PR title becomes the commit subject that release-please reads.
Keep every section and every gate line. See CONTRIBUTING.md.
-->

## Plan

<!-- One checkbox per plan step; tick each as it is done. Keep one status line. -->

- [ ] <plan step>

Status: <one line>

## Summary

## Gates run

<!-- Tick only commands that ran green on this branch's HEAD. Keep unticked lines. -->

- [ ] `dotnet build SessionInsights.slnx -c Release`
- [ ] `dotnet test SessionInsights.slnx -c Release --no-build`
- [ ] `pnpm --dir apps/frontend install --frozen-lockfile`
- [ ] `pnpm --dir apps/frontend run lint`
- [ ] `pnpm --dir apps/frontend run format:check`
- [ ] `pnpm --dir apps/frontend run test`
- [ ] `pnpm --dir apps/frontend run build`
- [ ] `powershell -NoProfile -File scripts/tests/release-readiness.tests.ps1`
- [ ] `powershell -NoProfile -File scripts/tests/update-lock.tests.ps1`
- [ ] `powershell -NoProfile -File scripts/tests/claude-statusline.tests.ps1`
- [ ] `node --test scripts/tests/startup-page.test.mjs`
- [ ] `node --test scripts/tests/check-adr-headings.test.mjs`
- [ ] `node scripts/check-adr-headings.mjs docs/adr`
- [ ] Not applicable (docs only; a change under `docs/adr/` runs the two ADR checks): <why>

## Scope notes

Owner follow-up: <none, or the action only the owner can take>
