# ADR-0013 – Run build, test and release on GitHub Actions as a constrained deviation from the MSDC pipeline templates

## Status

Proposed (2026-11-28). Becomes Accepted when the #132 pull request merges.

## Prologue (Summary)

In the context of building, testing and releasing a public GitHub project whose pipelines are hand-written workflows in `.github/workflows/`, facing the MSDC doctrine default of Azure DevOps pipelines built from the shared MSDC pipeline templates, which a public GitHub repository cannot consume, we decided for GitHub Actions as a recorded, constrained deviation that keeps the template gates where an equivalent exists and names the ones it lacks, to achieve a visible build, test and release path with pinned actions, least-privilege tokens and an owner-approved release, accepting that there is no static analysis gate and no dependency scan gate today and that the workflows can drift from the templates because nothing compares them.

## Discussion (Context)

The repository is a public GitHub project (`qvotaxon/ai-session-stats`). Its build, test and release stages are hand-rolled in three workflow files: `ci.yml` (pull request and `main` checks), `package-release.yml` (the reusable workflow that builds the archive and installer and publishes the GitHub Release) and `release.yml` (release-please, release authorization, release validation and the preview release). Two more small workflows, `adr-headings.yml` and `pr-title.yml`, check documentation and pull request titles. No earlier ADR records the choice of GitHub Actions over the MSDC Azure DevOps pipeline templates.

Background and constraints:

- The MSDC pipeline templates are private. A public GitHub repository cannot reference them, and copying them into a public repository would publish them.
- Releases are GitHub Releases, consumed by the tray updater (ADR-0005). The installer (ADR-0004) is not a consumer: the release workflow builds it and publishes it as a release asset next to the archive. The release process already lives on GitHub (release-please, GitHub Releases, the `release` environment).
- The gate names the templates use are listed here as build, test, static analysis, dependency scan and release approval.

> TODO: confirm with the template owners that a public GitHub repository cannot consume or reference the templates, and that no public or mirrored form of them exists.

> TODO: confirm the gate list against the templates, which are not readable from this repository.

What the workflows cover today, checked against the files on `main`:

| Gate | Where it exists | State |
| --- | --- | --- |
| Build | `ci.yml` jobs `backend` (`dotnet build`, Release) and `frontend` (`pnpm run build`); `package-release.yml` publishes the Windows application | Present |
| Test | `ci.yml`: `dotnet test`, `pnpm run test`, the PowerShell and Node script tests; `release.yml` `release-validation`; `adr-headings.yml` | Present |
| Static analysis | The frontend runs ESLint and a Prettier format check (`ci.yml` `frontend`). The backend has no analyzer, warnings-as-errors or code scanning step in the workflows, and `Directory.Build.props` sets none | Partial (frontend lint only); no static analysis gate for the backend |
| Dependency scan | No `dependabot.yml`, no dependency-review step, no vulnerability audit step in any workflow. `pnpm install --frozen-lockfile` pins the frontend lockfile, which is not a scan | Absent (see the TODO below the table) |
| Release approval | The `release-please` and `publish` jobs run in the `release` environment, which has one required reviewer (the owner). `release-authorization` fails the run when that setup is missing | Present |

> TODO: confirm whether GitHub-side alerting or code scanning is switched on in the repository settings, which the files cannot show.

What is already compliant:

- Every third-party action is pinned to a full commit SHA with the version in a comment. The only unpinned reference is the repository's own reusable workflow, which is referenced by path.
- Every workflow sets `permissions: contents: read` at the top except `pr-title.yml`, which sets `permissions: {}`, an explicit empty set that grants the token no permissions. Write access is granted only per job: `contents: write` and `pull-requests: write` to `release-please`, `contents: write` to `publish`, and `contents: write` to the two jobs that call the reusable workflow so that `publish` can use it.
- Every `actions/checkout` step sets `persist-credentials: false`. The `publish` job checks out nothing.
- Build jobs (`build`, `installer`) receive no write permission. Release-write credentials go only to `publish` (ADR-0004) and to `release-please`, which hold `contents: write`; both run in the `release` environment, so after owner approval. `release-please` may use the `RELEASE_PLEASE_TOKEN` secret instead of the workflow token (`release.yml`), and this repository cannot show what scope that secret has.

> TODO: confirm the scope of `RELEASE_PLEASE_TOKEN` in the repository settings.

- The Inno Setup installer download in `package-release.yml` is verified against a fixed SHA-256 before it runs.
- The ruleset on the default branch requires the checks `frontend`, `backend`, `scripts`, `adr-headings` and `pr-title`, and allows squash merges only. It requires no approving review (`required_approving_review_count` is 0). The review gate is a repository convention (AGENTS.md), not a ruleset rule.

Options considered:

- **Consume the MSDC Azure DevOps templates.** Rejected: the templates are private and the project is a public GitHub repository, so it cannot reference them. Mirroring the source into Azure DevOps only to run them would give two homes for one project and split the release path away from the GitHub Releases the updater reads.
- **Copy the template content into this repository.** Rejected: it publishes private material and creates a copy that drifts from the source.
- **Hand-rolled GitHub Actions, with the gates named.** Chosen.

> TODO: confirm that mirroring the source into Azure DevOps was not otherwise ruled out.

Non-goals: this ADR does not add a gate. It does not change any workflow or repository setting.

## Solution (Decision)

- Build, test and release stay on GitHub Actions, as the three workflows above.
- This is a deviation from the MSDC pipeline templates, recorded here so it is deliberate and not accidental. It is constrained: new workflows keep the compliant properties listed above (SHA-pinned actions, `contents: read` by default, `persist-credentials: false`, release publishing only after approval in the `release` environment, required checks in the ruleset).
- The gaps are named and stay open: there is no backend static analysis gate and no dependency scan gate. They are not accepted as permanent; the follow-ups below track them.
- We will not copy private template content into the repository, and we will not weaken a compliant property to make a workflow pass.

## Consequences (Results)

Positive:

- The deviation is written down, with the exact gates it replaces, so a reviewer can tell a deliberate difference from an oversight.
- The release path stays next to the code and the GitHub Releases that the updater reads.

Negative:

- No static analysis gate for the backend and no dependency scan gate. A vulnerable dependency or a class of code defect that a scanner finds is not blocked by CI today.
- Nothing compares the workflows with the templates, so they can drift as the templates change.

Follow-ups:

- Decide whether to add a dependency scan gate and a backend static analysis gate (for example GitHub's dependency review and code scanning, or analyzers with warnings as errors). Adding either needs an owner decision, and a GitHub Action needs asking first (AGENTS.md). Owner of the decision: the repository owner. Trigger: the next release preparation, or any dependency change.
- Confirm the repository settings for dependency alerts and code scanning, and record the answer in an amendment to this ADR. Owner: the repository owner. Trigger: before the follow-up above is decided, since it changes what that decision has to add.

> TODO: confirm those settings.
- Reverse this decision when: the MSDC pipeline templates become usable from this repository (published publicly, or the project moves to a host that can consume them), when the project is taken over as an MSDC-delivered product that must use them, or when the release path moves away from GitHub Releases. Then supersede this ADR with a new one.
