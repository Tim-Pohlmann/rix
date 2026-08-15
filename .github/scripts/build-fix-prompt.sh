#!/usr/bin/env bash
# Builds the task prompt rix hands to the coding agent when a caller's CI workflow run has
# failed. Used by .github/workflows/fix-failed-ci.yml to turn a failed workflow_run event into a
# `rix job` prompt: the workflow gathers the run context (failing jobs, run URL, commit) with the
# gh CLI and passes it here via the environment. Kept a pure function of its inputs (no network,
# no side effects) so tests/scripts/build-fix-prompt.bats can exercise it directly.
#
# Env:
#   RIX_REPO       owner/name of the repo whose CI failed (required)
#   RUN_URL        HTML URL of the failed workflow run (required)
#   RUN_NAME       display name of the failed workflow (required)
#   HEAD_BRANCH    branch the failed run executed on (default: "unknown")
#   HEAD_SHA       commit SHA the failed run executed on (default: "unknown")
#   DEFAULT_BRANCH default branch of RIX_REPO - what the rix job clone is checked out on
#   FAILED_JOBS    comma-separated names of the run's failed jobs (default: "unknown")
#   CALLER_PROMPT  caller-provided task text appended to the generated prompt (optional)

build_fix_prompt() {
  local repo="${RIX_REPO:-}" url="${RUN_URL:-}" name="${RUN_NAME:-}"
  if [[ -z "$repo" || -z "$url" || -z "$name" ]]; then
    echo "error: RIX_REPO, RUN_URL and RUN_NAME are required" >&2
    return 1
  fi

  local branch="${HEAD_BRANCH:-unknown}" sha="${HEAD_SHA:-unknown}"
  local default="${DEFAULT_BRANCH:-unknown}" failed="${FAILED_JOBS:-unknown}"
  local caller="${CALLER_PROMPT:-}"

  {
    echo "A CI workflow run failed in $repo, and your job is to fix it and open a pull request with the fix."
    echo ""
    echo "The failed run: $url"
    echo ""
    echo "Run details:"
    echo "- Workflow: $name"
    echo "- Branch: $branch"
    echo "- Commit: $sha"
    echo "- Failing job(s): $failed"
    echo ""
    echo "The repository is already checked out for you on its default branch ($default). Reproduce the"
    echo "failure by reading .github/workflows/ in the checkout to find the failing workflow and its"
    echo "commands, then run the failing job(s) locally. Fix the underlying cause, re-run the failing"
    echo "job(s) to confirm the fix, and open a pull request."
    echo ""
    echo "If the failing commit is on a branch other than $default, make the fix against the checked-out"
    echo "default branch anyway, so the change applies cleanly to $default."
    if [[ -n "$caller" ]]; then
      echo ""
      echo "$caller"
    fi
  }
  return 0
}

# Only run when executed directly, so the bats tests can source this file and exercise
# build_fix_prompt in isolation.
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
  set -euo pipefail
  build_fix_prompt
fi
