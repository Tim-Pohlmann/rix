---
name: rix-onboard
description: Onboard the repository you are working on to the rix factory this run belongs to, so the factory answers its CI failures. Use when asked to onboard, enroll, or connect a repo to the factory.
---

# Onboard a repo to the rix factory

This run's working directory is a clone of the repo to onboard, and the run itself belongs to a
rix factory. Onboarding adds one file to the repo, `.github/workflows/rix-notify-factory.yml`,
which dispatches the repo's CI failures to the factory. The secrets it needs are a human's job, so
the pull request you open lists them.

## 1. Check where you are

- `$GITHUB_REPOSITORY` is the factory (owner/name): the workflow that started this run belongs to
  it. `$RIX_BIN` is the rix binary running you. If either is unset, stop and report that this skill
  only works inside a factory's rix run.
- `git remote get-url origin` names the repo to onboard. If that is the factory itself, stop: the
  factory answers its own failures and needs no notify workflow.

## 2. Find the CI workflow

Look through `.github/workflows/*.yml` and `*.yaml` for the workflow that builds and tests the
repo on pull requests or pushes. rix answers that workflow's failures, so you need its top-level
`name:`, exactly as written.

- If more than one fits, take the one that gates pull requests, and name the others in the PR
  body.
- If the one you picked has no `name:`, add one (e.g. `name: CI`) in the same PR, and say so in the
  PR body.
- If no workflow builds or tests the repo, stop without opening a PR, and report that.

## 3. Write the notify workflow

From the clone's root:

```sh
"$RIX_BIN" initialize "$GITHUB_REPOSITORY" --ci-workflow "<the name: from step 2>"
```

That writes `.github/workflows/rix-notify-factory.yml` and nothing else; it overwrites an existing
copy. Write the file only this way, never by hand: the command validates both names and fills in
the template that matches this run's rix version.

If the clone already had the file and `git status` now shows no change, the repo is already
onboarded: stop without opening a PR, and report that.

## 4. Open the pull request

Commit on a branch named `rix/onboard-factory` and open the PR through rix's local API, as your
system prompt describes. Change nothing else, except for a `name:` added in step 2.

Title: `Dispatch CI failures to the rix factory`. The body says which workflow's failures go to
the factory and why you picked it, and lists what a human has to do before merging:

1. Add the repo secret `RIX_FACTORY_DISPATCH_TOKEN` (Settings -> Secrets and variables ->
   Actions): a PAT with contents:write on `<the factory>` only. Every repo the factory serves can
   share the same one.
2. Make sure the factory's `RIX_READ_TOKEN` and `RIX_WRITE_TOKEN` include this repo. Until they
   do, the factory can't answer this repo's failures.

If the repo has rix's single-repo workflows (`rix-on-ci-failure.yml`, `rix-agent.yml`, or the
older `rix.yml`), leave them in place, but say in the body that both this repo and the factory
would then answer each failure. Removing them is the repo owner's decision.
