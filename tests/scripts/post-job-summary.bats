#!/usr/bin/env bats
# Unit tests for .github/actions/post-job-summary/post-job-summary.sh. They source the script
# (which guards its main entry point), point GITHUB_STEP_SUMMARY at a temp file, and assert on the
# rendered markdown for the success, failure, submit-failure, transcript, and missing-input
# cases - no network.

setup() {
  source "${BATS_TEST_DIRNAME}/../../.github/actions/post-job-summary/post-job-summary.sh"
  export GITHUB_STEP_SUMMARY="$BATS_TEST_TMPDIR/step-summary.md"
  RESULT="$BATS_TEST_TMPDIR/result.json"
  SUBMIT="$BATS_TEST_TMPDIR/submit-result.json"
  ERROR_LOG="$BATS_TEST_TMPDIR/submit-error.log"
  TRANSCRIPT="$BATS_TEST_TMPDIR/transcript.md"
}

write_transcript() {
  printf '%s' "$1" > "$TRANSCRIPT"
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

@test "success renders pushed branches" {
  write_result '{"status":"success","pendingPrRequests":[],"pendingPushRequests":[],"costUsd":0.42,"durationSeconds":372}'
  write_submit '{"status":"success","createdPrs":[],"pushedBranches":["rix/my-fix"]}'

  run rix_post_job_summary "$RESULT" "$SUBMIT"

  [ "$status" -eq 0 ]
  [[ "$(summary)" == *"### Branches pushed"* ]]
  [[ "$(summary)" == *"- \`rix/my-fix\`"* ]]
}

@test "success with no pushed branches omits the pushed-branches section" {
  write_result '{"status":"success","pendingPrRequests":[],"costUsd":0,"durationSeconds":1}'
  write_submit '{"status":"success","createdPrs":[]}'

  run rix_post_job_summary "$RESULT" "$SUBMIT"

  [ "$status" -eq 0 ]
  [[ "$(summary)" != *"Branches pushed"* ]]
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

@test "submit failure is rendered from the error log when submit result is empty" {
  write_result '{"status":"success","pendingPrRequests":[],"costUsd":0,"durationSeconds":1}'
  : > "$SUBMIT"
  printf 'panic: rix submit crashed' > "$ERROR_LOG"

  run rix_post_job_summary "$RESULT" "$SUBMIT" "$ERROR_LOG"

  [ "$status" -eq 0 ]
  [[ "$(summary)" == *"### Submit"* ]]
  [[ "$(summary)" == *"**Status:** failure"* ]]
  [[ "$(summary)" == *"panic: rix submit crashed"* ]]
}

@test "transcript renders as a collapsible details section" {
  write_result '{"status":"success","pendingPrRequests":[],"costUsd":0,"durationSeconds":1}'
  write_transcript '# Session transcript

assistant: explored the codebase and applied the fix
'

  run rix_post_job_summary "$RESULT" "" "" "$TRANSCRIPT"

  [ "$status" -eq 0 ]
  [[ "$(summary)" == *"<details>"* ]]
  [[ "$(summary)" == *"<summary>Agent transcript</summary>"* ]]
  [[ "$(summary)" == *"assistant: explored the codebase and applied the fix"* ]]
  [[ "$(summary)" == *"</details>"* ]]
}

@test "transcript with HTML-sensitive characters is escaped so it can't break the details block" {
  write_result '{"status":"success","pendingPrRequests":[],"costUsd":0,"durationSeconds":1}'
  write_transcript 'a </details> <script>alert(1)</script> & b'

  run rix_post_job_summary "$RESULT" "" "" "$TRANSCRIPT"

  [ "$status" -eq 0 ]
  [[ "$(summary)" == *"&lt;/details&gt; &lt;script&gt;alert(1)&lt;/script&gt; &amp; b"* ]]
  [[ "$(summary)" != *"a </details> <script>"* ]]
}

@test "transcript is omitted when the arg is empty, missing, or the file is empty" {
  write_result '{"status":"success","pendingPrRequests":[],"costUsd":0,"durationSeconds":1}'

  run rix_post_job_summary "$RESULT" "" "" ""

  [ "$status" -eq 0 ]
  [[ "$(summary)" != *"Agent transcript"* ]]

  run rix_post_job_summary "$RESULT" "" "" "$BATS_TEST_TMPDIR/nope.md"

  [ "$status" -eq 0 ]
  [[ "$(summary)" != *"Agent transcript"* ]]

  : > "$TRANSCRIPT"
  run rix_post_job_summary "$RESULT" "" "" "$TRANSCRIPT"

  [ "$status" -eq 0 ]
  [[ "$(summary)" != *"Agent transcript"* ]]
}

@test "transcript larger than 900000 bytes is truncated and points at the rix-output artifact" {
  write_result '{"status":"success","pendingPrRequests":[],"costUsd":0,"durationSeconds":1}'
  # First 900000 bytes are 'a's, the overflowing tail is 'b's, so the summary's truncated content
  # and the cut-off tail are distinguishable from each other.
  head -c 900000 /dev/zero | tr '\0' 'a' > "$TRANSCRIPT"
  head -c 100 /dev/zero | tr '\0' 'b' >> "$TRANSCRIPT"

  run rix_post_job_summary "$RESULT" "" "" "$TRANSCRIPT"

  [ "$status" -eq 0 ]
  [[ "$(summary)" == *"rix-output artifact"* ]]
  [[ "$(summary)" == *"aaaaaaaaaa"* ]]
  [[ "$(summary)" != *"bbbbbbbbbb"* ]]
}

@test "transcript of at most 900000 bytes renders in full with no artifact pointer" {
  write_result '{"status":"success","pendingPrRequests":[],"costUsd":0,"durationSeconds":1}'
  head -c 900000 /dev/zero | tr '\0' 'a' > "$TRANSCRIPT"

  run rix_post_job_summary "$RESULT" "" "" "$TRANSCRIPT"

  [ "$status" -eq 0 ]
  [[ "$(summary)" != *"rix-output artifact"* ]]
}

@test "running as a real process with no submit args does not hit set -u" {
  # The sourced tests above call rix_post_job_summary directly, which never trips the script's
  # `set -euo pipefail` guard (only armed when run as its own process, not sourced) - so they
  # can't catch a local left unbound on some code path. Invoke the real script as a subprocess,
  # exactly like the composite action does for a plain `rix job` run (no submit step, so both
  # submit args are ""), to reproduce that class of bug.
  write_result '{"status":"success","pendingPrRequests":[],"costUsd":0,"durationSeconds":1}'

  run bash "${BATS_TEST_DIRNAME}/../../.github/actions/post-job-summary/post-job-summary.sh" "$RESULT" "" ""

  [ "$status" -eq 0 ]
  [[ "$output" != *"unbound variable"* ]]
  [[ "$(summary)" == *"**Status:** success"* ]]
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
