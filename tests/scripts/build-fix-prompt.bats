#!/usr/bin/env bats
# Unit tests for .github/scripts/build-fix-prompt.sh. They source the script (which guards its
# main entry point) and exercise build_fix_prompt with stubbed environment variables, asserting
# on the rendered prompt - no network, no side effects.

setup() {
  source "${BATS_TEST_DIRNAME}/../../.github/scripts/build-fix-prompt.sh"
}

@test "renders run details and the default fix task" {
  RIX_REPO=owner/repo \
  RUN_URL=https://github.com/owner/repo/actions/runs/123 \
  RUN_NAME=CI \
  HEAD_BRANCH=main \
  HEAD_SHA=abc123def456 \
  DEFAULT_BRANCH=main \
  FAILED_JOBS='Build, Test (ubuntu)' \
  run build_fix_prompt

  [ "$status" -eq 0 ]
  [[ "$output" == *"A CI workflow run failed in owner/repo"* ]]
  [[ "$output" == *"The failed run: https://github.com/owner/repo/actions/runs/123"* ]]
  [[ "$output" == *"- Workflow: CI"* ]]
  [[ "$output" == *"- Branch: main"* ]]
  [[ "$output" == *"- Commit: abc123def456"* ]]
  [[ "$output" == *"- Failing job(s): Build, Test (ubuntu)"* ]]
  [[ "$output" == *"default branch (main)"* ]]
  [[ "$output" == *"open a pull request"* ]]
}

@test "appends the caller prompt when provided" {
  RIX_REPO=owner/repo \
  RUN_URL=https://github.com/owner/repo/actions/runs/123 \
  RUN_NAME=CI \
  CALLER_PROMPT='Focus on the flaky network tests first.' \
  run build_fix_prompt

  [ "$status" -eq 0 ]
  [[ "$output" == *"Focus on the flaky network tests first."* ]]
}

@test "omits the caller prompt section when not provided" {
  RIX_REPO=owner/repo RUN_URL=https://github.com/owner/repo/actions/runs/123 RUN_NAME=CI \
    run build_fix_prompt

  [ "$status" -eq 0 ]
  # The last line is the default-branch instruction, not the caller prompt.
  [[ "$output" == *"applies cleanly to unknown"* ]]
}

@test "defaults missing optional context to unknown" {
  RIX_REPO=owner/repo RUN_URL=https://github.com/owner/repo/actions/runs/123 RUN_NAME=CI \
    run build_fix_prompt

  [ "$status" -eq 0 ]
  [[ "$output" == *"- Branch: unknown"* ]]
  [[ "$output" == *"- Commit: unknown"* ]]
  [[ "$output" == *"- Failing job(s): unknown"* ]]
  [[ "$output" == *"default branch (unknown)"* ]]
}

@test "fails clearly when a required variable is missing" {
  # The harness environment may set RIX_REPO (rix runs export it), so unset it explicitly
  # instead of assuming it's absent.
  unset RIX_REPO RUN_URL RUN_NAME
  RUN_URL=https://github.com/owner/repo/actions/runs/123 RUN_NAME=CI run build_fix_prompt
  [ "$status" -ne 0 ]
  [[ "$output" == *"RIX_REPO, RUN_URL and RUN_NAME are required"* ]]

  RIX_REPO=owner/repo RUN_NAME=CI run build_fix_prompt
  [ "$status" -ne 0 ]

  RIX_REPO=owner/repo RUN_URL=https://github.com/owner/repo/actions/runs/123 run build_fix_prompt
  [ "$status" -ne 0 ]
}

@test "running as a real process with only required vars exits 0" {
  RIX_REPO=owner/repo \
  RUN_URL=https://github.com/owner/repo/actions/runs/123 \
  RUN_NAME=CI \
  run bash "${BATS_TEST_DIRNAME}/../../.github/scripts/build-fix-prompt.sh"

  [ "$status" -eq 0 ]
  [[ "$output" == *"A CI workflow run failed in owner/repo"* ]]
  [[ "$output" != *"unbound variable"* ]]
}
