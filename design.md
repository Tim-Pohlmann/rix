# Design Document: `rix job`

## Overview

`rix job` is the lowest-level building block of the RIX tool suite. It clones a GitHub
repository into a temporary working directory, starts a local API server, and spawns a
Claude Code instance against the repo with a given prompt. When Claude finishes, `rix job`
reports the URLs of any PRs that were created, cleans up, and exits.

It is the reusable foundation for higher-level subcommands (`rix worker`, `rix factory`,
etc.). Anything that needs to run Claude Code against a repo will go through this layer.

---

## Inputs

| Parameter | Flag | Required | Description |
|---|---|---|---|
| Repository | `--repo` | Yes | Full GitHub repo identifier, e.g. `owner/repo` |
| Prompt | `--prompt` | Yes | The task prompt passed to Claude Code |
| Read token | `--read-token` | Yes | GitHub PAT with read-only repo access |
| Write token | `--write-token` | Yes | GitHub PAT with write access (push + PR creation). Never exposed to Claude. |
| Max tokens | `--max-tokens` | No | Claude Code token budget cap. Default: 50000 |
| Timeout | `--timeout` | No | Wall-clock timeout in minutes. Default: 30 |
| Work dir | `--work-dir` | No | Base directory for the temp clone. Default: system temp |

All parameters can alternatively be provided via environment variables with the prefix
`RIX_` (e.g. `RIX_READ_TOKEN`). Flags take precedence over env vars.

---

## Output

On success, `rix job` writes a JSON result to stdout:

```json
{
  "status": "success",
  "prs": [
    {
      "url": "https://github.com/owner/repo/pull/42",
      "branch": "rix/some-branch-name"
    }
  ],
  "tokensUsed": 12345,
  "durationSeconds": 87
}
```

On failure:

```json
{
  "status": "failure",
  "error": "Claude Code exited with code 1",
  "tokensUsed": 12345,
  "durationSeconds": 87
}
```

Logs (Claude's thought process, tool calls, API events) are written to stderr as
newline-delimited JSON, separate from the structured result on stdout. This allows callers
to pipe stdout for machine consumption while redirecting stderr to a log file or GH Actions
log stream.

Exit codes: `0` = success, `1` = job failure, `2` = configuration/setup error.

---

## Architecture

```
rix job (C# process)
│
├── Startup
│   ├── Validate inputs
│   ├── Check/install Claude Code (npm)
│   ├── Clone repo (git, read token)
│   └── Start local API server (ASP.NET minimal API, 127.0.0.1 only)
│
├── Job execution
│   ├── Spawn Claude Code subprocess
│   │   ├── Working directory: cloned repo root
│   │   ├── Environment: allowlisted vars only (see Security section)
│   │   └── stdout piped → NDJSON log stream → stderr of rix job
│   └── Monitor: enforce token budget + wall-clock timeout
│
└── Teardown (always runs, even on failure)
    ├── Stop API server
    ├── Delete temp clone directory
    └── Write JSON result to stdout
```

---

## Security

### Token isolation

The write token must never be accessible to Claude Code or any process it spawns.

The Claude Code subprocess is started with an explicitly constructed environment — not
inherited from the parent process. The allowlist is:

```
PATH
HOME
ANTHROPIC_API_KEY
CLAUDE_CODE_MAX_OUTPUT_TOKENS   (set by rix job to enforce budget)
LANG / LC_ALL                   (for locale-correct git output)
```

All other environment variables, including `RIX_WRITE_TOKEN` or any variable containing
the write token, are excluded. The write token lives only in the C# process memory and is
used exclusively by the local API server.

### Local API

The API binds to `127.0.0.1` only, on a randomly selected free port. The port is passed to
Claude via the system prompt. No authentication is used — the assumption is an ephemeral,
single-user environment (GH Actions runner). On a shared machine, standard OS-level process
isolation applies.

### Branch protection

PRs can only be created for branches matching the pattern `rix/*`. The API enforces this
server-side and rejects other branch names. This provides a defense-in-depth layer: even if
Claude calls the API unexpectedly, it cannot create PRs from arbitrary branches.

---

## Local API

A minimal ASP.NET Core web API. Claude is told the base URL and available endpoints in its
system prompt.

### Endpoints

#### `POST /pr`

Pushes a local branch to the remote and opens a pull request.

Request:
```json
{
  "branch": "rix/fix-null-check",
  "title": "Fix null reference in UserService",
  "body": "Fixes the null reference exception described in the prompt."
}
```

Response (success):
```json
{
  "url": "https://github.com/owner/repo/pull/42"
}
```

Response (failure — branch already exists on remote):
```json
{
  "error": "Branch rix/fix-null-check already exists on the remote."
}
```

**Server-side behavior:**
1. Validate branch matches `rix/*` pattern
2. Check remote for branch existence via GitHub REST API — reject if already present
3. Run `git push origin <branch>` using the write token (embedded in URL, not written to disk)
4. Call GitHub REST API (`POST /repos/{owner}/{repo}/pulls`) using the write token
5. Record the PR URL in the job result
6. Return the PR URL to Claude

The write token is used only in steps 3 and 4. It never touches the filesystem and is never
passed to any subprocess.

#### `GET /health`

Returns `200 OK`. Useful for Claude to verify the API is reachable before starting work.

### Future endpoints (not in v1)

- `GET /prs` — list open PRs (read token, for worker context)
- `GET /issues` — list issues
- `POST /comment` — post a PR comment

These are noted here to inform the API's structural design but are not part of the initial
implementation.

---

## Claude Code Integration

### Installation

At startup, `rix job` resolves Claude Code as follows:

1. Check if `claude` is on `PATH` via `claude --version`
2. If present and version meets the minimum required (configured constant in code), use it
3. If absent or too old, run `npm install -g @anthropic-ai/claude-code@<pinned-version>`
4. If npm is unavailable, exit with error code `2` and a clear message

The pinned version is a constant in the source code. It should be updated deliberately, not
automatically.

### Invocation

```
claude -p "<prompt>" \
  --output-format stream-json \
  --max-tokens <budget>
```

- `-p` runs in non-interactive (headless) mode
- `--output-format stream-json` emits NDJSON on stdout — every tool call, decision, and
  error as a structured line, not just a final summary
- `--max-tokens` enforces the token budget
- `--bare` is **not used** — Claude must be able to read `CLAUDE.md` and pick up
  project-specific skills and tools from the cloned repo

### System prompt

The system prompt (passed via `--append-system-prompt` or a `CLAUDE.md` injected at the
repo root — TBD) tells Claude:

- The local API base URL and available endpoints
- That it should create a branch named `rix/<short-description>` for its changes
- That it should call `POST /pr` when it is satisfied with its work
- That `GET /health` is available to verify connectivity

### Observability

Claude Code's NDJSON stdout is read line-by-line by the C# subprocess wrapper and written
to `rix job`'s stderr. This surfaces Claude's full thought process in the GH Actions log
without requiring any third-party observability service.

`CLAUDE_CODE_DEBUG=1` can optionally be added to Claude's environment to increase verbosity
for debugging.

---

## Process lifecycle

### Subprocess wrapper

The C# `ProcessWrapper` class handles:

- Constructing the sanitized environment (allowlist only)
- Capturing stdout line-by-line for NDJSON log forwarding
- Propagating stderr
- Enforcing wall-clock timeout via `CancellationTokenSource`
- Graceful termination: send SIGTERM (or Windows equivalent), wait 5s, then SIGKILL
- Returning exit code to the caller

### Cancellation and timeout

Two independent cancellation conditions:

1. **Token budget**: `--max-tokens` is passed directly to Claude Code. Claude Code enforces
   this internally and exits when the budget is exhausted.
2. **Wall-clock timeout**: The `ProcessWrapper` has a `CancellationToken` tied to a
   `TimeoutCancellationTokenSource`. If Claude Code is still running when the timeout
   fires, it is terminated gracefully then forcefully.

Both conditions result in job status `failure` with an appropriate error message in the
output JSON.

### Cleanup

Cleanup runs in a `finally` block and is guaranteed to execute regardless of success,
failure, timeout, or unhandled exception:

- Stop and dispose the API server
- Delete the temp clone directory (`Directory.Delete(path, recursive: true)`)
- If cleanup itself fails (e.g. locked files on Windows), log the error but do not rethrow
  — the job result has already been determined

---

## Repository abstraction

All GitHub-specific operations are grouped in a `GitHubRepositoryHost` class. This is not
behind an interface in v1, but the class boundary is kept clean to make introducing
`IRepositoryHost` straightforward later if GitLab or another host needs to be supported.

Operations owned by this class:
- `CloneAsync(repoIdentifier, readToken, targetDirectory)` — shells out to `git clone`
- `BranchExistsOnRemoteAsync(branch)` — GitHub REST API call
- `PushBranchAsync(branch)` — shells out to `git push` with write token in URL
- `CreatePullRequestAsync(branch, title, body)` — GitHub REST API call, returns PR URL

The write token is a constructor parameter of `GitHubRepositoryHost` and is never exposed
outside the class.

---

## Testing considerations

- `GitHubRepositoryHost` is the main unit requiring a seam for testing. Extract
  `IRepositoryHost` early to allow mocking in unit tests without real HTTP or git.
- The local API server can be tested independently from the subprocess lifecycle.
- Integration tests should use a dedicated test GitHub org/repo with short-lived tokens.
- The `ProcessWrapper` timeout and cancellation behavior should be unit-tested with a
  trivial subprocess (e.g. `sleep 999`).

---

## Non-goals for v1

- Concurrency: multiple simultaneous jobs are handled at the GH Actions layer (parallel
  workflow jobs), not by `rix job` itself
- Memory / context persistence: owned by `rix worker` and above
- GitLab or other host support
- MCP server interface (HTTP API chosen for v1; MCP remains an option for future
  consideration if context-window efficiency becomes a concern with a larger API surface)
  
