#!/usr/bin/env bats
# End-to-end tests for the rix binary itself - one level deeper than ci.yml's agent-plumbing job,
# which only proves the composite-action/workflow plumbing works for the happy path. These tests
# invoke the built rix binary directly against the real opencode/claude/pi CLIs (no npm version
# pinning - see EnsureInstalledViaNpmAsync - so this is knowingly a bit brittle to upstream CLI
# changes) and do real JSON parsing on the result.
#
# A regression that breaks agent invocation (e.g. a CLI usage error from a missing required flag)
# must be told apart from an expected runtime failure (bad key, no login, ...) without hardcoding
# either agent CLI's prose, since that wording isn't ours to pin and doesn't scale as a per-failure
# assertion. The discriminator used below is structural instead: rix's error field embeds the
# agent CLI's own last-written line verbatim (see ProcessWrapper.Diagnostic). For claude, that line
# is always its final "type":"result" JSON object, whose "is_error" field is true on every runtime
# failure this has been confirmed against (invalid key, no credentials at all) - so asserting
# is_error:true rules out a usage error (plain, non-JSON text, emitted before claude ever enters
# its output protocol) being mistaken for the runtime failure this test expects, without pinning
# any vendor prose. Note this can't tell an auth failure apart from some other is_error:true
# failure (e.g. a bad model id) - rix's own result.json carries no error kind beyond this raw
# diagnostic line, and reaching for a more specific field (e.g. claude's "subtype") would mean
# depending on vendor-specific JSON shape, the same coupling this test is designed to avoid.
#
# pi doesn't fit the pattern above, and gets its own tests further down. Confirmed (by running
# the real CLI locally, both with no credentials at all and with an explicit invalid key) that:
#   - With no provider resolvable at all, pi throws before ever starting a turn and its last
#     stderr line (see ProcessWrapper.Diagnostic) is plain prose, not JSON - it's a doc pointer
#     baked into the installed package (see auth-guidance.js's getProviderLoginHelp), e.g.
#     ".../docs/models.md". So the structural check here is that it's a real file that ships with
#     the CLI, not that it parses as JSON.
#   - Unlike opencode/claude, an invalid key does not fail pi's process at all: pi embeds the
#     failure in its own JSON event stream as a per-turn error (stopReason:"error", errorMessage)
#     and still exits 0. So the "reached the CLI's own request/auth-failure protocol" case for pi
#     looks like rix job *succeeding* with no PRs produced, not a JobFailure - the discriminator
#     against a real regression (our flags rejected as unknown) is the same exit-0/success shape,
#     since a rejected flag instead fails the process outright with a plain "Unknown option" line.
#
# Requires: RIX_BIN (a built rix binary), RIX_REPO/RIX_READ_TOKEN (a real repo + token to clone),
# and network access to install/run the real agent CLIs via npm.

setup() {
  : "${RIX_BIN:?RIX_BIN must point at a built rix binary}"
  : "${RIX_REPO:?RIX_REPO must name a real GitHub repo to clone (e.g. Tim-Pohlmann/rix)}"
  : "${RIX_READ_TOKEN:?RIX_READ_TOKEN must be a GitHub token with read access to RIX_REPO}"
  export RIX_PROMPT="Make no changes to any files and do not call the PR endpoint. Just reply with the text: OK"
  export RIX_OUTPUT_DIR="$BATS_TEST_TMPDIR/out"
  export RIX_WORK_DIR="$BATS_TEST_TMPDIR/work"
  mkdir -p "$RIX_OUTPUT_DIR" "$RIX_WORK_DIR"
}

teardown() {
  unset RIX_AGENT RIX_MODEL OPENCODE_API_KEY OPENAI_API_KEY ANTHROPIC_API_KEY
}

result_field() {
  jq -r ".$1" "$RIX_OUTPUT_DIR/result.json"
}

# Strips rix's own "agent failed: exited with code N: " wrapper (see JobRunner.RunAsync) to get
# at the agent CLI's own diagnostic line (see ProcessWrapper.Diagnostic).
diagnostic_of() {
  sed -E 's/^agent failed: exited with code [0-9]+: //'
}

@test "opencode succeeds with its free default model and no key" {
  export RIX_AGENT=opencode
  run "$RIX_BIN" job
  [ "$status" -eq 0 ]
  [ "$(result_field status)" = success ]
}

@test "claude with an explicit model fails without a key with a structured (not plain-text) diagnostic" {
  export RIX_AGENT=claude RIX_MODEL=claude-opus-4-1
  run "$RIX_BIN" job
  [ "$status" -eq 1 ]
  [ "$(result_field status)" = failure ]
  diagnostic="$(result_field error | diagnostic_of)"
  echo "$diagnostic" | jq empty
  [ "$(echo "$diagnostic" | jq -r .is_error)" = true ]
}

@test "pi fails without a key, pointing at a real doc file it ships" {
  export RIX_AGENT=pi
  run "$RIX_BIN" job
  [ "$status" -eq 1 ]
  [ "$(result_field status)" = failure ]
  # See the file header for why this is a .md path rather than JSON, and why it needs trimming.
  diagnostic="$(result_field error | diagnostic_of | xargs)"
  [[ "$diagnostic" == *.md ]]
  [ -f "$diagnostic" ]
}

@test "pi treats an invalid key as a successful run producing no PRs, not a failure" {
  export RIX_AGENT=pi RIX_MODEL=openai/gpt-4o
  export OPENAI_API_KEY=sk-invalid-0000000000000000000000000000000000000000
  run "$RIX_BIN" job
  # See the file header: pi swallows a per-turn auth error into its JSON stream and still exits 0.
  [ "$status" -eq 0 ]
  [ "$(result_field status)" = success ]
  [ "$(result_field pendingPrRequests | jq 'length')" = 0 ]
}
