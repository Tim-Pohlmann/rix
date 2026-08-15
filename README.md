# RIX

AI software factory.

`rix job` clones a target repo, runs a coding agent against it, and emits one git
bundle per proposed change. It is read-only — it never pushes or opens PRs itself, so the
agent run needs only a read token. A separate, trusted step turns the bundles into PRs.

Before the agent starts, rix configures the clone's git identity as
`rix <rix@noreply.invalid>`, so commits the agent creates carry a consistent author instead
of guessed metadata.

## Run via GitHub Actions

`rix` ships two reusable workflows: `.github/workflows/job.yml` runs a job against any repo
and opens the resulting PRs, and `.github/workflows/fix-failed-ci.yml` runs a job automatically
whenever one of your CI workflows fails, turning the failure into a fix-PR. Add a small caller
workflow to any repo:

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

### Fixing a failed CI run

Watch one of your CI workflows and run `rix job` on it whenever it fails, with a small
`workflow_run`-triggered caller workflow:

```yaml
name: Fix failed CI
on:
  workflow_run:
    workflows: ["CI"]        # the workflow(s) whose failures should be fixed
    types: [completed]
permissions:
  contents: read             # needed to check out rix's own scripts
  actions: read              # needed to query the failed run's jobs
jobs:
  fix:
    uses: Tim-Pohlmann/rix/.github/workflows/fix-failed-ci.yml@main
    with:
      repo: ${{ github.repository }}
    secrets:
      read-token: ${{ secrets.RIX_READ_TOKEN }}
      write-token: ${{ secrets.RIX_WRITE_TOKEN }}
```

When a watched workflow fails, the workflow gathers the failed run's details (workflow name,
branch, commit, and the failing jobs) and bakes them into the prompt it hands the agent, so the
agent knows exactly which CI run to reproduce and fix. The agent works against a clone of the
default branch and opens a PR with the fix; nothing runs when the watched workflow succeeds.

Optional inputs beyond `repo`:

- `prompt` — extra task instructions appended to the auto-generated prompt (default: none).
- `ci-workflows` — comma-separated workflow display names to react to; runs from other workflows
  are skipped. Usually the caller's `workflow_run.workflows` filter suffices; use this when one
  caller watches several workflows.
- `ignore-branches` — comma-separated glob patterns of branch names whose runs are skipped
  (default: `rix/*`). Prevents the workflow from re-triggering itself when CI fails on the
  `rix/*` PR branches it opens; set to `''` to react to every failed run.
- plus the same `agent`, `model`, `agent-api-key-env`, `max-tokens`, `timeout`,
  `allowed-push-branches`, `rix-version` and `runner` inputs as `job.yml`.

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
