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
the resulting PRs. Add a small caller workflow to any repo:

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
    uses: Tim-Pohlmann/rix/.github/workflows/job.yml@main
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
as `--allowed-push-branches` (env `RIX_ALLOWED_PUSH_BRANCHES`); each entry must be a well-formed
`rix/*` branch name.

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

`@main` tracks the latest workflow; once a release is tagged, pin to that tag or a commit
SHA (e.g. `...job.yml@v1.0.0`) for reproducible, supply-chain-safe runs.

### Secrets

| Secret | Purpose |
| --- | --- |
| `read-token` | Required. PAT with read access to the target repo; used to clone it during the agent run. |
| `write-token` | Required. PAT with `contents:write` + `pull-requests:write` on the target repo; used to push branches and open PRs. |
| `agent-api-key` | Optional. API key for the selected agent's model provider, exported as the env var named by `agent-api-key-env` (default `OPENCODE_API_KEY` for opencode, `ANTHROPIC_API_KEY` for claude; for pi, `agent-api-key-env` must be set explicitly whenever `agent-api-key` is provided, since pi has no per-agent default). Not needed when leaving `model` unset, since opencode then picks its own free default model. |

The read/write split keeps the agent run (which executes untrusted, model-generated work)
on a read-only token; only the final, deterministic PR-creation step holds write access.

The workflow downloads the latest published `rix` release binary and verifies it against the
release's published SHA-256 checksum before running it; pin a specific build with the optional
`rix-version` input.

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
run URL, failing step logs), and calls `job.yml`. It's the building block for both patterns
below — write the "turn a failure into a prompt" logic once, reuse it either way.

### Simple: directly in a project repo

Add a caller triggered by `workflow_run` instead of `workflow_dispatch`:

```yaml
name: rix (on CI failure)
on:
  workflow_run:
    workflows: ["CI"] # must match the `name:` of the workflow to watch
    types: [completed]
jobs:
  rix:
    # Cheap short-circuit; on-ci-failure.yml re-checks the conclusion via the API regardless.
    if: github.event.workflow_run.conclusion == 'failure'
    uses: Tim-Pohlmann/rix/.github/workflows/on-ci-failure.yml@main
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
straight into the agent's prompt, an untrusted fork PR could smuggle prompt-injection text into
a failing test's output and have it interpreted as instructions by an agent holding
`write-token`. If CI runs on fork PRs, add an explicit trust gate before calling the reusable
workflow, e.g.:

```yaml
if: >-
  github.event.workflow_run.conclusion == 'failure' &&
  github.event.workflow_run.head_repository.full_name == github.repository
```

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
    if: github.event.workflow_run.conclusion == 'failure'
    runs-on: ubuntu-latest
    steps:
      - name: Dispatch to factory repo
        env:
          GH_TOKEN: ${{ secrets.RIX_FACTORY_DISPATCH_TOKEN }}
        run: |
          gh api repos/${{ vars.RIX_FACTORY_REPO }}/dispatches \
            -f event_type=rix-ci-failure \
            -f "client_payload[repo]=${{ github.repository }}" \
            -f "client_payload[run_id]=${{ github.event.workflow_run.id }}"
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
    # Validate before trusting client_payload — see caveat below.
    if: contains(fromJSON(vars.RIX_FACTORY_ALLOWED_REPOS), github.event.client_payload.repo)
    uses: Tim-Pohlmann/rix/.github/workflows/on-ci-failure.yml@main
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
