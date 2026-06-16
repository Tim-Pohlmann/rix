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
    secrets:
      read-token: ${{ secrets.RIX_READ_TOKEN }}
      write-token: ${{ secrets.RIX_WRITE_TOKEN }}
      anthropic-api-key: ${{ secrets.ANTHROPIC_API_KEY }}
```

### Required secrets

| Secret | Purpose |
| --- | --- |
| `read-token` | PAT with read access to the target repo; used to clone it during the agent run. |
| `write-token` | PAT with `contents:write` + `pull-requests:write` on the target repo; used to push branches and open PRs. |
| `anthropic-api-key` | Credentials for the `claude` CLI. |

The read/write split keeps the agent run (which executes untrusted, model-generated work)
on a read-only token; only the final, deterministic PR-creation step holds write access.

The workflow downloads the latest published `rix` release binary; pin a specific build
with the optional `rix-version` input.

Both jobs run on `ubuntu-latest` by default. Pass the optional `runner` input to run them
on a different runner (e.g. a self-hosted label):

```yaml
    with:
      repo: ${{ github.repository }}
      prompt: ${{ inputs.prompt }}
      runner: self-hosted
```
