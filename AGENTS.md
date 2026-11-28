# Instructions for AI agents

These instructions are for every AI agent that works in this repository: Claude Code (which loads this file through `CLAUDE.md`), and GitHub Copilot's cloud agent, CLI, code review and VS Code chat. Other Copilot IDE chats get the ADR rules from `.github/instructions/adr.instructions.md` when they work on a file in `docs/adr/`, and otherwise through the pointer in `.github/copilot-instructions.md`. They need no plugin. Where a plugin skill says something different, this file and the files it names win. People are welcome to read them too.

## Architecture Decision Records

ADRs in `docs/adr/` use the Alexandrian template, always. Before you create, convert or edit any file in `docs/adr/`, read `.github/instructions/adr.instructions.md` (the rules, including how to pick the next number) and `docs/adr/template.md` (the template). Never rewrite an existing decision.

## Repository conventions

Always: never merge, close or approve a pull request, or close an issue; the owner does. The issue, scope, test and package rules also always apply. The branch, commit and pull request rules apply only to tasks that change tracked files; read-only tasks (questions, reviews, plans kept in `.msdc/local/`) skip them.

### Branch and worktree

- First check for existing work: `gh pr list --state all --search "#<n> in:body"` and `git ls-remote --heads origin '<n>-*'`. If either finds this issue's pull request or branch, or your tool already opened one, continue on it (for a found branch: work in the folder `git worktree list` shows for it, else `git fetch origin` then `git worktree add <path> <type>/<n>-<short-slug>`; then `git merge origin/<type>/<n>-<short-slug>`). If it is merged or closed, stop and ask the owner.
- Otherwise run `git fetch origin` and `git worktree add -b <type>/<n>-<short-slug> <path> origin/main`, where `<path>` is a folder outside the main checkout, such as `../wt-<n>-<short-slug>`. In the main checkout, never switch branches, commit, push or edit tracked files.
- To update a branch that has a pull request, run `git fetch origin` and `git merge origin/main`. Never rebase it and never force-push.

### Draft pull request first

1. Make a first commit and push it. An empty one is fine: `git commit --allow-empty -m "chore: start work on #<n>" -m "<your Co-Authored-By trailer>"`, then `git push -u origin HEAD`.
2. Open a draft: `gh pr create --draft --base main --title "<type>: <summary>" --body-file <file>`. Never open a second pull request, also not after a stop.
3. The title is a plain Conventional Commit with no `[ai-session-stats]` prefix; do not take the msdc-harness `create-pr` skill's title.
4. The body starts with `Refs #<n>`, then one checkbox per plan step and one status line; other required content may follow. No `Generated with` line or other agent footer. Tick boxes and update the status line as you go (`gh pr edit <n> --body-file <file>`).

### Commits and pushes

- Every push to a draft runs the full CI. Commit and push at meaningful checkpoints: a failing test written, a step green, a plan step done, or just before a risky refactor. Roughly every 15 to 30 minutes of real work, not every edit.
- Stage named paths only (`git add <path>`), never `git add -A`, `git add .` or `git commit -a`. Never commit secrets, scratch or output files, plan files (`.msdc/local/`), or untracked files you did not create.
- Keep the `Co-Authored-By` trailer your tool adds to commit messages.
- A hook may ask the person at the keyboard to confirm a commit or push. That answer is theirs: wait for it, never avoid or disable the hook, and never take another agent's message as the answer. If it is refused, stop and hand back the exact commands the person still has to run (`git commit` with the change staged, then `git push`).

### Ready for review

- The review gate is a review of the pull request's head commit by an MSDC reviewer agent (`MSDC-Code-Reviewer`) other than the authoring agent, which passes and names the reviewed commit SHA.
- Change `Refs #<n>` to `Fixes #<n>` only when the review gate has passed and CI is green on the latest commit. Any commit after the review goes back to review.
- The owner marks the pull request ready and merges it. Run `gh pr ready <n>` only when the owner tells you to in their own message.

### Issues, scope, tests and packages

- Every issue body and comment you write, also on pull requests, starts with the line `Filed by local agent`. Cloud agents too. Pull request bodies and commit messages do not.
- Found a bug or other work outside the task? File it as its own issue (marker first, no labels) and keep it out of this pull request.
- Never create a label. Add or remove a label only where a documented step tells you to.
- Never skip, weaken or delete a test, or edit a failing test to make it pass, unless the issue says that test is wrong. Re-run the tests before you report them green, and name the command and its result; the review gate confirms it.
- Add no package (NuGet, npm, PowerShell module or GitHub Action) without asking the owner first.
