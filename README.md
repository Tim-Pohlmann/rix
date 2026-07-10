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
      # agent: claude   # optional; defaults to 'opencode'
    secrets:
      read-token: ${{ secrets.RIX_READ_TOKEN }}
      write-token: ${{ secrets.RIX_WRITE_TOKEN }}
```

Pick the coding agent with the optional `agent` input — `opencode` (default), `claude`, or
`pi`. All three install their CLI via npm at run time. With no `model` set, `opencode` picks
its own free default model, so the example above needs no `agent-api-key` at all.

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
| `agent-api-key` | Optional. API key for the selected agent's model provider, exported as the env var named by `agent-api-key-env` (default `OPENCODE_API_KEY` for opencode, `ANTHROPIC_API_KEY` for claude; `agent-api-key-env` is required for pi). Not needed when leaving `model` unset, since opencode then picks its own free default model. |

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
