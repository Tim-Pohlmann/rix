#!/usr/bin/env bats
# End-to-end tests for the rix binary itself - one level deeper than ci.yml's agent-plumbing job,
# which only proves the composite-action/workflow plumbing works for the happy path. These tests
# invoke the built rix binary directly against the real opencode/claude CLIs (no npm version
# pinning - see EnsureInstalledViaNpmAsync - so this is knowingly a bit brittle to upstream CLI
# changes) and do real JSON parsing on the result.
#
# A regression that breaks agent invocation (e.g. a CLI usage error from a missing required flag)
# must be told apart from an expected runtime failure (bad key, no login, ...) without hardcoding
# either agent CLI's prose, since that wording isn't ours to pin and doesn't scale as a per-failure
# assertion. The discriminator used below is structural instead: rix's error field embeds the
# agent CLI's own last-written line verbatim (see ProcessWrapper.Diagnostic) - for every runtime
# failure both CLIs support (auth, bad key, ...), that line is itself a JSON object from the CLI's
# own structured output/error protocol, whereas a usage error is plain, non-JSON text emitted
# before either CLI ever entered that protocol. Asserting the diagnostic parses as JSON therefore
# catches the same class of regression the old prose-matching assertions did, without needing to
# track exact vendor wording.
#
# For the two claude tests specifically, we go one step further and assert the CLI's own
# "error" field equals "authentication_failed" - confirmed (by running the real CLI locally,
# both with an explicit invalid key and with no credentials at all) to be the one field that's
# stable across both real auth-failure paths, unlike e.g. api_error_status (only set when the
# API itself rejects the call; null when the CLI catches "not logged in" client-side). This
# rules out a false pass from some other JSON-shaped CLI error unrelated to auth.
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

@test "opencode fails on an invalid key with a structured (not plain-text) diagnostic" {
  export RIX_AGENT=opencode RIX_MODEL=openai/gpt-4o
  # opencode forwards to the model's own provider SDK, which reads its own env var convention
  # (OPENAI_API_KEY for an openai/... model) rather than a generic OPENCODE_API_KEY.
  export OPENAI_API_KEY=sk-invalid-0000000000000000000000000000000000000000
  run "$RIX_BIN" job
  [ "$status" -eq 1 ]
  [ "$(result_field status)" = failure ]
  diagnostic="$(result_field error | diagnostic_of)"
  echo "$diagnostic" | jq empty
}

@test "claude fails without a key with a structured (not plain-text) diagnostic" {
  export RIX_AGENT=claude
  run "$RIX_BIN" job
  [ "$status" -eq 1 ]
  [ "$(result_field status)" = failure ]
  diagnostic="$(result_field error | diagnostic_of)"
  echo "$diagnostic" | jq empty
  [ "$(echo "$diagnostic" | jq -r .error)" = authentication_failed ]
}

@test "claude with an explicit model fails without a key with a structured (not plain-text) diagnostic" {
  export RIX_AGENT=claude RIX_MODEL=claude-opus-4-1
  run "$RIX_BIN" job
  [ "$status" -eq 1 ]
  [ "$(result_field status)" = failure ]
  diagnostic="$(result_field error | diagnostic_of)"
  echo "$diagnostic" | jq empty
  [ "$(echo "$diagnostic" | jq -r .error)" = authentication_failed ]
}
