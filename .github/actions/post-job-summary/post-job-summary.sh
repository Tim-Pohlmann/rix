#!/usr/bin/env bash
# Appends a markdown summary of a rix job run to the GitHub Actions step summary file named by
# $GITHUB_STEP_SUMMARY, so the workflow run page shows the job outcome (status, cost, duration)
# and the pull requests that were actually opened. Used by the run-rix-job and submit-rix-job
# composite actions.
#
# Usage: post-job-summary.sh <result.json> [submit-result.json] [submit-error.log]
#
#   result.json        the result of `rix job` (always present in a completed run)
#   submit-result.json the stdout of `rix submit` (absent when the submit step wrote nothing)
#   submit-error.log   the stderr of `rix submit` (used to explain a submit failure)
#
# Best-effort by design: the summary is informational only, so a missing file or a malformed JSON
# renders whatever it can (or nothing) and the script always exits 0 - it must never fail the
# workflow step it is called from.

# Formats a whole number of seconds as "Xh Ym Zs", "Xm Zs", or "Zs".
duration_label() {
  local seconds="$1" h m s
  h=$((seconds / 3600))
  m=$((seconds % 3600 / 60))
  s=$((seconds % 60))
  if ((h > 0)); then
    printf '%dh %dm %ds' "$h" "$m" "$s"
  elif ((m > 0)); then
    printf '%dm %ds' "$m" "$s"
  else
    printf '%ds' "$s"
  fi
  return 0
}

# Appends a "### Submit" / status / error block to stdout. Shared by both render_submit branches
# below - the only difference between them is where the error text comes from.
render_submit_failure() {
  local status="$1" error="$2"
  echo "### Submit"
  echo ""
  echo "**Status:** ${status:-failure}"
  if [[ -n "$error" ]]; then
    echo ""
    echo "**Error:** $error" # NOSONAR: step-summary markdown content, not a diagnostic - must stay on stdout to reach $summary_file
  fi
  echo ""
  return 0
}

# Appends a "### $heading" section listing a JSON array's entries, or nothing if it's empty/absent.
# Shared by createdPrs and pushedBranches in render_submit - the only difference between them is
# the heading, the array's field name, and the jq filter that renders each entry as a line.
render_list_section() {
  local file="$1" heading="$2" field="$3" render_filter="$4" count
  count="$(jq "(.$field // []) | length" "$file" 2>/dev/null || echo 0)"
  if ((count > 0)); then
    echo "### $heading"
    echo ""
    jq -r "$render_filter" "$file" 2>/dev/null || true
    echo ""
  fi
  return 0
}

# Appends the submit outcome - the opened pull requests, or the submit failure - to stdout.
render_submit() {
  local submit_file="$1" submit_log="$2" status=""
  if [[ -f "$submit_file" ]]; then
    status="$(jq -r '.status // empty' "$submit_file" 2>/dev/null || true)"
  fi

  # submit_file is missing, empty, or malformed (e.g. `rix submit` crashed before writing valid
  # JSON to it) - fall back to the raw stderr log instead of silently rendering nothing.
  if [[ -z "$status" ]]; then
    if [[ -n "$submit_log" && -s "$submit_log" ]]; then
      render_submit_failure "failure" "$(head -n1 "$submit_log")"
    fi
    return 0
  fi

  if [[ "$status" == "success" ]]; then
    render_list_section "$submit_file" "Pull requests opened" "createdPrs" \
      '.createdPrs[] | "- [" + .branch + "](" + .url + ")"'
    # shellcheck disable=SC2016 # backticks are jq's markdown code-fence output, not command substitution
    render_list_section "$submit_file" "Branches pushed" "pushedBranches" \
      '.pushedBranches[] | "- `" + . + "`"'
    render_list_section "$submit_file" "Tasks updated" "updatedPrs" \
      '.updatedPrs[] | "- [" + .branch + "](" + .url + ")"'
    render_list_section "$submit_file" "Tasks reverted" "closedPrs" \
      '.closedPrs[] | "- [" + .branch + "](" + .url + ")"'
  else
    local error
    error="$(jq -r '.error // empty' "$submit_file" 2>/dev/null || true)"
    render_submit_failure "$status" "$error"
  fi
}

rix_post_job_summary() {
  local result_file="$1" submit_file="${2:-}" submit_log="${3:-}"
  local summary_file="${GITHUB_STEP_SUMMARY:-}"

  if [[ -z "$summary_file" ]]; then
    echo "GITHUB_STEP_SUMMARY is not set; skipping rix job summary." >&2
    return 0
  fi
  [[ -f "$summary_file" ]] || : > "$summary_file"

  {
    echo "## Rix job summary"
    echo ""
    if [[ ! -f "$result_file" ]]; then
      echo "The rix job produced no result.json - it never completed."
      echo ""
      return 0
    fi

    local status
    status="$(jq -r '.status // empty' "$result_file" 2>/dev/null || true)"
    if [[ -z "$status" ]]; then
      echo "result.json did not contain a status."
      echo ""
      return 0
    fi

    if [[ "$status" == "success" ]]; then
      local cost duration
      cost="$(jq -r '.costUsd // 0' "$result_file" 2>/dev/null || echo 0)"
      duration="$(jq -r '.durationSeconds // 0' "$result_file" 2>/dev/null || echo 0)"
      echo "**Status:** success"
      echo "**Cost:** \$$cost"
      echo "**Duration:** $(duration_label "$duration")"
    else
      echo "**Status:** $status"
      local error
      error="$(jq -r '.error // empty' "$result_file" 2>/dev/null || true)"
      if [[ -n "$error" ]]; then
        echo ""
        echo "**Error:** $error"
      fi
    fi
    echo ""
    render_submit "$submit_file" "$submit_log"
  } >> "$summary_file"
  return 0
}

if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
  set -euo pipefail
  rix_post_job_summary "$@"
fi
