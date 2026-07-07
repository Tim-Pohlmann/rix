#!/usr/bin/env bats
# Live integration tests: install the real opencode and claude CLIs and invoke them with the
# exact argument shapes OpenCodeAgent/ClaudeAgent build (src/Rix/Agents/OpenCodeAgent.cs,
# ClaudeAgent.cs), without any provider API key. Unit tests assert those shapes against a fake
# process runner; this catches the real CLIs rejecting a flag we assume they accept (renamed,
# removed, etc.) without needing paid credentials in CI:
#   - opencode's free default model is expected to actually succeed with no key at all.
#   - every other (agent, model) combination is expected to fail with an auth-shaped error
#     rather than an "unknown argument" error — proving the flags were accepted and parsed.

setup() {
  WORKDIR="$(mktemp -d)"
  cd "$WORKDIR" || exit 1
  # Guarantee no ambient credentials leak into these runs regardless of the runner's environment.
  unset ANTHROPIC_API_KEY OPENCODE_API_KEY OPENCODE_ZEN_API_KEY OPENAI_API_KEY
}

teardown() {
  rm -rf "$WORKDIR"
}

# Passes when $output/$status look like an auth-shaped failure (missing/invalid key, unauthorized,
# etc.) rather than success or an "unknown argument" style failure. The latter would mean our
# constructed flags no longer match the real CLI, which must fail the test, not be swallowed by a
# blanket "exit != 0" check.
assert_auth_failure() {
  local output="$1"
  local status="$2"

  [ "$status" -ne 0 ]

  if ! echo "$output" | grep -qiE 'auth|api.?key|credential|unauthorized|login|401'; then
    echo "expected an auth-shaped failure, got: $output" >&2
    return 1
  fi

  if echo "$output" | grep -qiE 'unknown (option|argument|command)|unrecognized argument|invalid option'; then
    echo "CLI rejected our flags as unknown, not an auth failure: $output" >&2
    return 1
  fi
}

# opencode wraps request-level failures (missing key, unauthorized, upstream 500s, etc.) in a
# JSON error envelope rather than a human-readable "auth" message, so the keyword check above
# doesn't fit here. A `"type":"error"` envelope is still proof the CLI parsed --model/--format as
# real flags — an unrecognized flag fails with a CLI usage error before any JSON is ever emitted —
# so check for that shape instead of matching specific wording.
assert_opencode_error_envelope() {
  local output="$1"
  local status="$2"

  [ "$status" -ne 0 ]

  if ! echo "$output" | grep -q '"type"[[:space:]]*:[[:space:]]*"error"'; then
    echo "expected a JSON error envelope, got: $output" >&2
    return 1
  fi

  if echo "$output" | grep -qiE 'unknown (option|argument|command)|unrecognized argument|invalid option'; then
    echo "CLI rejected our flags as unknown, not a request-level failure: $output" >&2
    return 1
  fi
}

@test "opencode: no --model, no key -> succeeds on the free default model" {
  run timeout 60 opencode run "Reply with exactly: OK" --format json
  [ "$status" -eq 0 ]
  [ -n "$output" ]
}

@test "opencode: explicit paid --model, no key -> a JSON error envelope (flags still accepted)" {
  run timeout 60 opencode run "Reply with exactly: OK" --model openai/gpt-4o --format json
  assert_opencode_error_envelope "$output" "$status"
}

@test "claude: no --model, no key -> auth failure (flags still accepted)" {
  run timeout 60 claude --output-format stream-json --print "Reply with exactly: OK" \
    --append-system-prompt "You are a test." --verbose
  assert_auth_failure "$output" "$status"
}

@test "claude: explicit --model, no key -> auth failure (flags still accepted)" {
  run timeout 60 claude --output-format stream-json --print "Reply with exactly: OK" \
    --append-system-prompt "You are a test." --verbose --model claude-opus-4-1
  assert_auth_failure "$output" "$status"
}
