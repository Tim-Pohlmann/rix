#!/usr/bin/env bats
# Unit tests for .github/actions/post-job-summary/post-job-summary.sh. They source the script
# (which guards its main entry point), point GITHUB_STEP_SUMMARY at a temp file, and assert on the
# rendered markdown for the success, failure, submit-failure, and missing-input cases - no network.

setup() {
  source "${BATS_TEST_DIRNAME}/../../.github/actions/post-job-summary/post-job-summary.sh"
  export GITHUB_STEP_SUMMARY="$BATS_TEST_TMPDIR/step-summary.md"
  RESULT="$BATS_TEST_TMPDIR/result.json"
  SUBMIT="$BATS_TEST_TMPDIR/submit-result.json"
  ERROR_LOG="$BATS_TEST_TMPDIR/submit-error.log"
}

summary() {
  cat "$GITHUB_STEP_SUMMARY"
}

write_result() {
  printf '%s' "$1" > "$RESULT"
}

write_submit() {
  printf '%s' "$1" > "$SUBMIT"
}

@test "duration_label formats hours, minutes and plain seconds" {
  [ "$(duration_label 3661)" = "1h 1m 1s" ]
  [ "$(duration_label 372)" = "6m 12s" ]
  [ "$(duration_label 9)" = "9s" ]
}

@test "success renders status, cost, duration and created pull requests" {
  write_result '{"status":"success","pendingPrRequests":[],"costUsd":0.42,"durationSeconds":372}'
  write_submit '{"status":"success","createdPrs":[{"branch":"rix/my-fix","url":"https://github.com/owner/repo/pull/12"}]}'

  run rix_post_job_summary "$RESULT" "$SUBMIT"

  [ "$status" -eq 0 ]
  [[ "$(summary)" == *"## Rix job summary"* ]]
  [[ "$(summary)" == *"**Status:** success"* ]]
  [[ "$(summary)" == *'**Cost:** $0.42'* ]]
  [[ "$(summary)" == *"**Duration:** 6m 12s"* ]]
  [[ "$(summary)" == *"### Pull requests opened"* ]]
  [[ "$(summary)" == *"- [rix/my-fix](https://github.com/owner/repo/pull/12)"* ]]
}

@test "success with no pull requests omits the pull-request section" {
  write_result '{"status":"success","pendingPrRequests":[],"costUsd":0,"durationSeconds":1}'
  write_submit '{"status":"success","createdPrs":[]}'

  run rix_post_job_summary "$RESULT" "$SUBMIT"

  [ "$status" -eq 0 ]
  [[ "$(summary)" == *"## Rix job summary"* ]]
  [[ "$(summary)" != *"Pull requests opened"* ]]
}

@test "failure renders status and error" {
  write_result '{"status":"failure","error":"agent failed: boom","costUsd":0,"durationSeconds":10}'

  run rix_post_job_summary "$RESULT"

  [ "$status" -eq 0 ]
  [[ "$(summary)" == *"**Status:** failure"* ]]
  [[ "$(summary)" == *"**Error:** agent failed: boom"* ]]
}

@test "submit failure is rendered from the error log when no submit result exists" {
  write_result '{"status":"success","pendingPrRequests":[],"costUsd":0,"durationSeconds":1}'
  printf 'branch already exists on remote: rix/my-fix' > "$ERROR_LOG"

  run rix_post_job_summary "$RESULT" "" "$ERROR_LOG"

  [ "$status" -eq 0 ]
  [[ "$(summary)" == *"### Submit"* ]]
  [[ "$(summary)" == *"**Status:** failure"* ]]
  [[ "$(summary)" == *"branch already exists on remote"* ]]
}

@test "submit failure is rendered from a present submit result" {
  write_result '{"status":"success","pendingPrRequests":[],"costUsd":0,"durationSeconds":1}'
  write_submit '{"status":"failure","error":"creating PR for rix/my-fix failed"}'

  run rix_post_job_summary "$RESULT" "$SUBMIT"

  [ "$status" -eq 0 ]
  [[ "$(summary)" == *"### Submit"* ]]
  [[ "$(summary)" == *"**Status:** failure"* ]]
  [[ "$(summary)" == *"creating PR for rix/my-fix failed"* ]]
}

@test "missing result.json renders a notice, not a failure" {
  run rix_post_job_summary "$BATS_TEST_TMPDIR/nope.json"

  [ "$status" -eq 0 ]
  [[ "$(summary)" == *"produced no result.json"* ]]
}

@test "without GITHUB_STEP_SUMMARY exits 0 and writes nothing" {
  unset GITHUB_STEP_SUMMARY
  write_result '{"status":"success","pendingPrRequests":[],"costUsd":0,"durationSeconds":1}'

  run rix_post_job_summary "$RESULT"

  [ "$status" -eq 0 ]
  [ ! -e "$BATS_TEST_TMPDIR/step-summary.md" ]
}
