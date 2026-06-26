# RIX

AI software factory.

`rix job` clones a target repo, runs a coding agent against it, and emits one git
bundle per proposed change. It is read-only — it never pushes or opens PRs itself, so the
agent run needs only a read token. A separate, trusted step turns the bundles into PRs.

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
      # agent: opencode   # optional; defaults to 'claude'
    secrets:
      read-token: ${{ secrets.RIX_READ_TOKEN }}
      write-token: ${{ secrets.RIX_WRITE_TOKEN }}
      agent-api-key: ${{ secrets.ANTHROPIC_API_KEY }}
```

Pick the coding agent with the optional `agent` input — `claude` (default) or `opencode`.
Both install their CLI via npm at run time.

### Required secrets

| Secret | Purpose |
| --- | --- |
| `read-token` | PAT with read access to the target repo; used to clone it during the agent run. |
| `write-token` | PAT with `contents:write` + `pull-requests:write` on the target repo; used to push branches and open PRs. |
| `agent-api-key` | API key for the selected agent's model provider. Mapped to the provider env var the agent reads (`ANTHROPIC_API_KEY` for both `claude` and `opencode`). |

The read/write split keeps the agent run (which executes untrusted, model-generated work)
on a read-only token; only the final, deterministic PR-creation step holds write access.

The workflow downloads the latest published `rix` release binary; pin a specific build
with the optional `rix-version` input.

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
