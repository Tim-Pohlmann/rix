#!/usr/bin/env bats
# End-to-end tests for `rix ci-failure` against the real GitHub Actions API - one level below
# ci.yml's ci-failure-plumbing job, which drives the real composite action but can only ever see its
# own still-running run. These invoke the built binary directly and parse its result JSON, so every
# outcome rix reports about a real run is pinned to what that run actually is, rather than to a
# canned API response.
#
# The point of testing these against the live API at all is the translation at the edge: GitHub's
# `conclusion` field (a word per state, plus null for "not finished") becomes rix's own CiOutcome,
# and a failed request becomes a CiFailureError. A unit test can only assert that mapping against
# the payloads we believe GitHub sends; these assert it against the ones it does send.
#
# Deliberately not covered here: the `detected` path. It needs a run whose conclusion really is
# "failure", and detecting one always hands a prompt to a coding agent (CiFailureRunner.RunAsync) -
# a prompt built from that run's own logs, so nothing here could control what the agent is asked to
# do or how long it takes. The one shortcut past the agent is a loop guard that trips immediately,
# which needs --max-rix-commits 0, and rix rejects that (1..100). Pointing this at some older failed
# run in the repo's history would not fix either half, and would make the test depend on history
# that ages out. So the failure path stays with the unit tests, and everything around it - the
# outcomes that stop before the agent, and the errors that stop before the outcome - is here.
#
# Requires: RIX_BIN (a built rix binary), RIX_REPO/RIX_READ_TOKEN (a real repo and a token with
# actions:read on it), plus curl and jq.

setup() {
  : "${RIX_BIN:?RIX_BIN must point at a built rix binary}"
  : "${RIX_REPO:?RIX_REPO must name a real GitHub repo (e.g. Tim-Pohlmann/rix)}"
  : "${RIX_READ_TOKEN:?RIX_READ_TOKEN must be a GitHub token with actions:read on RIX_REPO}"
  export RIX_OUTPUT_DIR="$BATS_TEST_TMPDIR/out"
  export RIX_WORK_DIR="$BATS_TEST_TMPDIR/work"
  export RIX_RESULT="$BATS_TEST_TMPDIR/result.json"
  export RIX_STDERR="$BATS_TEST_TMPDIR/stderr.log"
  mkdir -p "$RIX_OUTPUT_DIR" "$RIX_WORK_DIR"
}

# A check that stops before the agent writes no result.json at all (see
# Startup.WriteCiFailureResult), so rix's stdout is the only copy of its result - captured to a file
# of its own because bats' `run` merges the process's stderr into $output, and any log line rix
# writes there would then have to be parsed back out of the JSON.
ci_failure() {
  run bash -c 'rix=$1; shift; "$rix" ci-failure "$@" >"$RIX_RESULT" 2>"$RIX_STDERR"' bash "$RIX_BIN" "$@"
}

result_field() {
  jq -r ".$1" "$RIX_RESULT"
}

# Asks the same API rix is about to ask, because these tests have no fixture to compare against:
# what a real run's outcome should be reported as is whatever that run's own conclusion says today.
# --fail-with-body so a rejected request surfaces here as a failure instead of as an empty jq result
# further down.
api() {
  curl -sS --fail-with-body \
    -H "Authorization: Bearer $RIX_READ_TOKEN" \
    -H "Accept: application/vnd.github+json" \
    -H "X-GitHub-Api-Version: 2022-11-28" \
    "https://api.github.com/repos/$RIX_REPO/$1"
}

@test "a successful run is reported as skipped, naming the outcome GitHub gave it" {
  # Asked for by outcome rather than picked out of a list, so the run this ends up pointing at is
  # one GitHub itself calls successful; newest, so it is the least likely to have aged out of
  # retention, and found by query rather than hardcoded for the same reason. It is already finished,
  # so the only thing that could change its outcome between these two calls is somebody re-running
  # it in that second.
  run_id=$(api "actions/runs?status=success&per_page=1" | jq -r '.workflow_runs[0].id // empty')
  [ -n "$run_id" ] || skip "$RIX_REPO has no successful workflow run to point at"

  ci_failure --run-id "$run_id"
  [ "$status" -eq 0 ]
  [ "$(result_field status)" = skipped ]
  # rix's word for it, not GitHub's "success": the outcome is translated at the host boundary, and
  # this is the assertion that it is translated at all.
  [ "$(result_field outcome)" = succeeded ]
}

@test "the in-progress run these tests are part of has no outcome yet" {
  # The same case ci-failure-plumbing covers through the composite action, asserted one layer down
  # on the word rix reports rather than on the action's has-result: GitHub sends conclusion:null for
  # a run that is still going, which is a state of its own and not an unrecognized outcome.
  [ -n "${GITHUB_RUN_ID:-}" ] || skip "not running inside a workflow run"

  ci_failure --run-id "$GITHUB_RUN_ID"
  [ "$status" -eq 0 ]
  [ "$(result_field status)" = skipped ]
  [ "$(result_field outcome)" = pending ]
}

@test "a run id that does not exist is an error, not a run that did not fail" {
  # Run ids are global to GitHub, so 1 belongs to some other repo's first-ever run and is a 404
  # under any repo this test could be pointed at. The distinction being pinned: a run rix cannot
  # read is not the same as a run that did not fail - reporting the latter would silently swallow
  # every API problem as "nothing to do".
  ci_failure --run-id 1
  [ "$status" -eq 1 ]
  [ "$(result_field status)" = error ]
  [[ "$(result_field error)" == *"get workflow run 1"* ]]
  [[ "$(result_field error)" == *404* ]]
}

@test "a token GitHub rejects is an error naming the failed operation" {
  # Shaped like a real token so it is rix's own validation this gets past, and GitHub's that turns
  # it down. Worth its own case next to the 404 above because this repo is public: a regression that
  # stopped sending the token at all would keep working against it, and only a request that must be
  # authenticated to be accepted can catch that.
  RIX_READ_TOKEN=ghp_0000000000000000000000000000000000000 ci_failure --run-id 1
  [ "$status" -eq 1 ]
  [ "$(result_field status)" = error ]
  [[ "$(result_field error)" == *"get workflow run 1"* ]]
  [[ "$(result_field error)" == *401* ]]
}
