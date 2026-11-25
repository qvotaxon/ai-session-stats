<!--
Title: a plain Conventional Commit, for example `feat: show delivered points per sprint`.
No [ai-session-stats] prefix, although the create-pr skill adds one by default.
With this repo's squash setting the PR title becomes the commit subject that release-please reads.
Keep every section and every gate line. See CONTRIBUTING.md.
-->

## Closes

Closes #

## Summary

## Gates run

<!-- Tick only commands that ran green on this branch's HEAD. Keep unticked lines. -->

- [ ] `dotnet build SessionInsights.slnx -c Release`
- [ ] `dotnet test SessionInsights.slnx -c Release --no-build`
- [ ] `pnpm --dir apps/frontend install --frozen-lockfile`
- [ ] `pnpm --dir apps/frontend run lint`
- [ ] `pnpm --dir apps/frontend run test`
- [ ] `pnpm --dir apps/frontend run build`
- [ ] `powershell -NoProfile -File scripts/tests/release-readiness.tests.ps1`
- [ ] `powershell -NoProfile -File scripts/tests/update-lock.tests.ps1`
- [ ] `powershell -NoProfile -File scripts/tests/claude-statusline.tests.ps1`
- [ ] `node --test scripts/tests/startup-page.test.mjs`
- [ ] Not applicable (docs only): <why>

## Scope notes

Owner follow-up: <none, or the action only the owner can take>
