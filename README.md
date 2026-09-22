# RIX

AI software factory.

`rix job` clones a target repo, runs a coding agent against it, and emits one git
bundle per proposed change. It is read-only — it never pushes or opens PRs itself, so the
agent run needs only a read token. A separate, trusted step turns the bundles into PRs.

Before the agent starts, rix configures the clone's git identity as
`rix <rix@noreply.invalid>`, so commits the agent creates carry a consistent author instead
of guessed metadata.

## Run via GitHub Actions

`rix` ships a reusable workflow (`.github/workflows/job.yml`) that runs the job and opens
the resulting PRs. Each repo drives it through a small caller workflow.

### Quick start: `rix initialize`

Run `rix initialize` inside a checkout of the target repo to write both caller workflows
(`.github/workflows/rix.yml` and `.github/workflows/rix-on-ci-failure.yml`; existing files are
overwritten). The templates are baked into the `rix` binary, so this needs no network access.
Their `uses:` lines point at the floating major-version tag of the release this `rix` comes
from (`@v0` for any 0.x build), so the caller workflows call the reusable workflows they were
released with. Then:

1. Add repo secrets `RIX_READ_TOKEN` and `RIX_WRITE_TOKEN` (see [Secrets](#secrets)).
2. In `rix-on-ci-failure.yml`, change `workflows: ["CI"]` to the `name:` of the workflow rix
   should react to.
3. Commit and push the two files.

Pass `--dir <path>` to target a repo other than the current directory, and `--ref <git-ref>` to
pin the written workflows to a different tag, branch, or commit SHA of this repo. The sections
below describe the files it writes and how to customize them further.

### The `rix` caller workflow

```yaml
name: rix
on:
  workflow_dispatch:
    inputs:
      prompt:
        description: Task for the agent
        required: true
jobs:
  rix:
    uses: Tim-Pohlmann/rix/.github/workflows/job.yml@v0
    with:
      repo: ${{ github.repository }}
      prompt: ${{ inputs.prompt }}
      # agent: claude   # optional; defaults to 'opencode'
    secrets:
      read-token: ${{ secrets.RIX_READ_TOKEN }}
      write-token: ${{ secrets.RIX_WRITE_TOKEN }}
```

Pick the coding agent with the optional `agent` input — `opencode` (default), `claude`, or
`pi`. All three install their CLI via npm at run time. With no `model` set, `opencode` picks
its own free default model, so the example above needs no `agent-api-key` at all.

The workflow posts a job summary to the run page after `rix submit`: the job's status, cost and
duration, plus the pull requests that were actually opened (with links). The `run` job posts an
early summary too, so a failed job still reports its outcome even when the PR-creation job is
skipped.

### Allowing the agent to push (resuming a run)

`rix job` exposes a local API to the agent. Besides opening PRs (`/pr`), the agent can push new
commits onto a branch that already exists on the remote (`/push`, e.g. resuming a previous run).
`/push` accepts nothing by default — an untrusted agent cannot touch any existing branch unless
you opt in with the `allowed-push-branches` input, a comma-separated list of branches the `/push`
endpoint accepts:

```yaml
    with:
      repo: ${{ github.repository }}
      prompt: ${{ inputs.prompt }}
      allowed-push-branches: rix/continue-foo,rix/continue-bar
```

A push to any branch outside that list (or any push at all, if the input is omitted) is rejected
with a 403, and the agent is told the allow-list in its system prompt. The input forwards verbatim
as `--allowed-push-branches` (env `RIX_ALLOWED_PUSH_BRANCHES`); an entry can be any branch name
that already exists on the remote, not just `rix/*` — e.g. a human's own branch you want the agent
to resume.

### Using a different provider or model

opencode supports many model providers beyond the free default. Pick a model with the
`model` input, forwarded verbatim as `--model` (rix does not interpret it), and set
`agent-api-key`/`agent-api-key-env` to the credential that provider expects:

```yaml
    with:
      repo: ${{ github.repository }}
      prompt: ${{ inputs.prompt }}
      agent: opencode
      model: openai/gpt-4o
      agent-api-key-env: OPENAI_API_KEY
    secrets:
      read-token: ${{ secrets.RIX_READ_TOKEN }}
      write-token: ${{ secrets.RIX_WRITE_TOKEN }}
      agent-api-key: ${{ secrets.OPENAI_API_KEY }}
```

`claude` (Anthropic's Claude Code CLI) only talks to Anthropic's own models; for that agent,
`model` (if set) just picks among Claude's models, and `agent-api-key-env` defaults to
`ANTHROPIC_API_KEY` automatically (no need to set it unless using a non-default env var name).

`pi` (the open-source Pi coding agent CLI) is multi-provider like opencode — pick a model
with `model`, forwarded verbatim as `--model` (e.g. `openai/gpt-4o`). Unlike opencode, pi has
no free default provider, so `agent-api-key-env` must always be set explicitly when
`agent-api-key` is provided; there is no per-agent default to fall back on.

Local/self-hosted backends (e.g. Ollama, LM Studio) aren't supported yet — opencode and pi
reach those through a generated config file rather than a model string + API key, which is a
separate mechanism this workflow doesn't build today.

`@v0` is the floating major-version tag: it moves to each new 0.x release, so callers pick up
fixes without re-pinning, and the workflow keeps fetching the binary belonging to that release.
Pin an exact tag (e.g. `...job.yml@v0.5.0`) or a commit SHA for byte-for-byte reproducible,
supply-chain-safe runs. `@main` is not recommended: between a version bump and the release it
names, it asks for a binary that isn't published yet.

### Secrets

| Secret | Purpose |
| --- | --- |
| `read-token` | Required. PAT with read access to the target repo; used to clone it during the agent run. |
| `write-token` | Required. PAT with `contents:write` + `pull-requests:write` on the target repo; used to push branches and open PRs. |
| `agent-api-key` | Optional. API key for the selected agent's model provider, exported as the env var named by `agent-api-key-env` (default `OPENCODE_API_KEY` for opencode, `ANTHROPIC_API_KEY` for claude; for pi, `agent-api-key-env` must be set explicitly whenever `agent-api-key` is provided, since pi has no per-agent default). Not needed when leaving `model` unset, since opencode then picks its own free default model. |

The read/write split keeps the agent run (which executes untrusted, model-generated work)
on a read-only token; only the final, deterministic PR-creation step holds write access.

The workflow downloads the `rix` release binary belonging to the workflow ref you pinned in
`uses:` — not whatever was published most recently — and verifies it against that release's
published SHA-256 checksum before running it, so the workflow and the binary it drives always
come from the same release. Override with the optional `rix-version` input (an exact tag, or
`latest`) only if you specifically want them to differ.

Both jobs run on `ubuntu-latest` by default. Pass the optional `runner` input to run them
on a different runner (e.g. a self-hosted label). The workflow detects the runner's OS and
architecture and downloads the matching release binary: Linux and macOS on x64 or arm64,
and Windows on x64.

```yaml
    with:
      repo: ${{ github.repository }}
      prompt: ${{ inputs.prompt }}
      runner: self-hosted
```

## React to CI failures

`rix` also ships `.github/workflows/on-ci-failure.yml`, a reusable workflow that takes a
specific run, checks whether it actually failed, builds a prompt from the failure (PR number,
run URL, failing step logs), and runs the agent against it via the `run-ci-failure` and
`submit-rix-job` composite actions. It's the building block for both patterns below — write the
"turn a failure into a prompt" logic once, reuse it either way.

### Simple: directly in a project repo

A caller triggered by `workflow_run` instead of `workflow_dispatch` — this is the
`rix-on-ci-failure.yml` that [`rix initialize`](#quick-start-rix-initialize) writes; edit the
watched workflow name after generating it:

```yaml
name: rix (on CI failure)
on:
  workflow_run:
    workflows: ["CI"] # must match the `name:` of the workflow to watch
    types: [completed]
jobs:
  rix:
    # Cheap short-circuit; on-ci-failure.yml re-checks both conditions via the API regardless.
    if: >-
      github.event.workflow_run.conclusion == 'failure' &&
      github.event.workflow_run.head_repository.full_name == github.repository
    # One rix run per branch at a time: when CI fails again while rix is still working on the
    # previous failure, the second run waits instead of starting a second agent from the same tip.
    # On the job rather than the workflow, so only runs that get past the `if` above contend:
    # GitHub keeps a single pending entry per group and cancels whatever a newcomer displaces
    # (`cancel-in-progress` governs the running entry, not the queued one), so at workflow scope
    # every completion on the branch - a later successful rerun included - would join the group
    # and could drop a queued answer to a real failure. The branch names the group on its own
    # because the head_repository check above has already pinned which repo's branch it is.
    concurrency:
      group: rix-on-ci-failure-${{ github.event.workflow_run.head_branch }}
      cancel-in-progress: false
    uses: Tim-Pohlmann/rix/.github/workflows/on-ci-failure.yml@v0
    with:
      # repo defaults to the calling repo — no need to set it here.
      run-id: ${{ github.event.workflow_run.id }}
    secrets:
      read-token: ${{ secrets.RIX_READ_TOKEN }}
      write-token: ${{ secrets.RIX_WRITE_TOKEN }}
```

`read-token` needs `Actions:read` in addition to read access to contents, since
`on-ci-failure.yml` uses it to fetch the failing run's logs, not just to clone.

**Fork PRs:** `workflow_run` always executes with the base repo's secrets, even when the CI run
it's reacting to came from a fork PR. Since `on-ci-failure.yml` feeds that run's log output
straight into the agent's prompt, an untrusted fork PR could otherwise smuggle prompt-injection
text into a failing test's output and have it interpreted as instructions by an agent holding
`write-token` — and pushing to a fork takes no permission on your repo at all.

Rix therefore refuses these runs itself: `rix ci-failure` compares the run's head repo against the
target repo and stops with an `untrustedRun` result — reported as a notice, not a failure — before
fetching any logs. Getting a branch into the repo requires write access to it, so that one
comparison is the permission check; a maintainer's own fork PR is turned away by it too, but rix
could not have pushed a fix onto that branch anyway. The `head_repository` condition in the `if:`
above is the same rule applied a step earlier, so a fork's failure costs no runner minutes.

### Advanced: a central factory repo

Useful when several project repos should share one set of `read-token`/`write-token`/
`agent-api-key` secrets instead of each holding its own. `workflow_run` can't cross repos, so
each project repo still needs a small local trigger — but it only *notifies* the factory repo
instead of running rix itself, using a PAT scoped just to dispatch to that one repo:

```yaml
# In each project repo: .github/workflows/notify-rix-factory.yml
name: notify rix factory
on:
  workflow_run:
    workflows: ["CI"]
    types: [completed]
jobs:
  notify:
    # Same two conditions as the simple pattern: rix re-checks both against the API, so this only
    # saves the round trip through the factory.
    if: >-
      github.event.workflow_run.conclusion == 'failure' &&
      github.event.workflow_run.head_repository.full_name == github.repository
    runs-on: ubuntu-latest
    steps:
      - name: Dispatch to factory repo
        # Every value reaches the script through env, never through ${{ }} inside `run:` - an
        # expression there is pasted into the script before bash sees it. A fork PR's branch name
        # is chosen by its author and git allows `$(...)` and backticks in one, so interpolating
        # it would run the author's command in this step, which holds the dispatch token.
        env:
          GH_TOKEN: ${{ secrets.RIX_FACTORY_DISPATCH_TOKEN }}
          FACTORY_REPO: ${{ vars.RIX_FACTORY_REPO }}
          PROJECT_REPO: ${{ github.repository }}
          RUN_ID: ${{ github.event.workflow_run.id }}
          HEAD_BRANCH: ${{ github.event.workflow_run.head_branch }}
        run: |
          gh api "repos/$FACTORY_REPO/dispatches" \
            -f event_type=rix-ci-failure \
            -f "client_payload[repo]=$PROJECT_REPO" \
            -f "client_payload[run_id]=$RUN_ID" \
            -f "client_payload[branch]=$HEAD_BRANCH"
```

`RIX_FACTORY_DISPATCH_TOKEN` needs `contents:write` on the factory repo (required by the
`dispatches` API) and nothing else — it never touches RIX's own credentials.

```yaml
# In the factory repo: .github/workflows/rix-on-failure.yml
name: rix (dispatched CI failure)
on:
  repository_dispatch:
    types: [rix-ci-failure]

jobs:
  rix:
    # Bounds which repos the factory will act on, but does not say who asked — see the
    # trust caveat below.
    if: contains(fromJSON(vars.RIX_FACTORY_ALLOWED_REPOS), github.event.client_payload.repo)
    # The factory's equivalent of the simple pattern's group, on the job for the same reason and
    # below the allowlist so a payload naming a repo this factory does not serve never reaches it.
    # `repository_dispatch` carries neither repo nor branch of its own, so the key comes from the
    # payload. Those fields are unauthenticated: an onboarded repo can name another one's pair and
    # cancel its queued response. That is a real denial rather than mere serialization, and it is
    # the same mutual trust the allowlist already asks for - not something the allowlist bounds.
    # See the caveat below.
    concurrency:
      group: rix-on-ci-failure-${{ github.event.client_payload.repo }}-${{ github.event.client_payload.branch }}
      cancel-in-progress: false
    uses: Tim-Pohlmann/rix/.github/workflows/on-ci-failure.yml@v0
    with:
      repo: ${{ github.event.client_payload.repo }}
      run-id: ${{ github.event.client_payload.run_id }}
    secrets:
      read-token: ${{ secrets.RIX_READ_TOKEN }}
      write-token: ${{ secrets.RIX_WRITE_TOKEN }}
```

The factory repo's `read-token`/`write-token` need access across every project repo it serves
(e.g. a GitHub App installation token), which is the trade-off of centralizing: one PAT with a
much bigger blast radius than the per-repo tokens in the simple pattern.

**Validate the dispatch payload:** `RIX_FACTORY_DISPATCH_TOKEN` can only dispatch to the
factory repo, but the `repo`/`run-id` values inside `client_payload` are unauthenticated free
text — any repo holding that token can ask the factory to act against *any* `repo`/`run-id`,
not just its own. Since the factory's `read-token`/`write-token` span every project repo it
serves, an unvalidated payload lets one onboarded repo trigger rix runs (and PR writes) against
another. Gate the factory job on an explicit allowlist of onboarded repos (as shown above with
`RIX_FACTORY_ALLOWED_REPOS`) rather than trusting `client_payload.repo` directly.

**Onboarding a repo is mutual trust:** the allowlist bounds *which* repos the factory will act
on, but nothing in a `repository_dispatch` says *who asked*. All project repos share one
`RIX_FACTORY_DISPATCH_TOKEN`, so an onboarded repo can name any other onboarded repo in
`client_payload[repo]` along with a real failing run ID from it, and every check downstream
passes — the run exists, it failed, and its head repo matches. rix then spends the factory's
cross-repo write token on that other repo. What an attacker gets is the trigger, not the
content: rix still answers a genuine CI failure and still opens an ordinary `rix/*` PR. But the
timing and the target are theirs to pick, and the reachable set is exactly the repos worth
reaching. The same holds in the other direction: `client_payload` also supplies the concurrency
key, so an onboarded repo can name another's `repo`/`branch` and cancel a response that repo
had queued. Read the allowlist as a blast-radius bound, not as authentication.

Two things that don't close this, despite looking like they should: `github.event.sender`
names the account or App that called the dispatch API, not the repo it was called from, so with
one shared token every project looks identical; and a per-project `event_type` is just more
payload — the same token can send any of them.

So either **run one factory per trust domain**, keeping the onboarded set small enough that its
members may as well trust each other (usually what an org reaching for a factory wants anyway),
or, when the set spans trust boundaries, **make each project prove who it is**: have it send an
HMAC of `repo`/`run_id` under a per-project secret, and have the factory recompute it using the
secret it holds *for the repo the payload claims to be*. That lookup is what the allowlist is
missing — claiming to be another repo then requires that repo's secret. The cost is that the
factory now stores one secret per project, which is most of the per-repo key management
centralizing was meant to avoid; that trade is the reason to prefer the first option when the
trust domain allows it.

### Keeping rix from answering its own failures

rix pushes commits, those commits run CI, and a failing one triggers this workflow again — so
left alone, rix answering its own failures is a loop with nothing to stop it. Two things bound
it, and both apply to either pattern above:

- **The loop guard.** Before doing any work, `on-ci-failure.yml` counts how many commits at the
  tip of the failing branch rix authored itself, and leaves the failure alone once that reaches
  `max-rix-commits` (default 5, max 100). The count stops at the first commit rix didn't write,
  so anyone pushing to the branch re-enables rix on it; a guarded run logs a notice, succeeds,
  and creates nothing. The default is 5 rather than 1 because rix fixing up its own previous
  attempt is the normal case — the first attempt failing is exactly why there is a second.
  Note it bounds *commits*, not attempts: one agent run can produce more than one commit, so the
  effective number of attempts is at most this.
- **The `concurrency` group**, keyed on the failing branch and the repository that branch lives
  in, so a branch that fails twice in quick succession queues the second run rather than
  starting a second agent from the same tip. `cancel-in-progress: false` because a run already
  talking to the agent has work worth finishing. It sits on the job rather than on the workflow,
  which matters more than it looks: GitHub keeps one *pending* entry per group and cancels
  whatever a newcomer displaces, and `cancel-in-progress` governs the running entry, not the
  queued one. At workflow scope the group is taken before any `if` is looked at, so an event
  that does no work — a later successful rerun of the same branch — could quietly drop a queued
  answer to a real failure. The branch alone is enough to name the group because the `if:` above
  it has already pinned the repository. Both callers above carry a group; the factory keys its
  on the dispatch payload's `repo` and `branch` rather than on `workflow_run`, since a
  `repository_dispatch` knows neither on its own.

```yaml
    with:
      run-id: ${{ github.event.workflow_run.id }}
      max-rix-commits: 3
```
