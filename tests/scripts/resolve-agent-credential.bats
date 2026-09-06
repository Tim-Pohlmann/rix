#!/usr/bin/env bats
# Unit tests for .github/actions/resolve-agent-credential/resolve-agent-credential.sh. They
# source the script with AGENT_API_KEY unset (so its top-level auto-export body is a no-op) and
# exercise rix_resolve_agent_key_env directly.

setup() {
  AGENT_API_KEY=""
  source "${BATS_TEST_DIRNAME}/../../.github/actions/resolve-agent-credential/resolve-agent-credential.sh"
}

@test "defaults to ANTHROPIC_API_KEY for claude" {
  AGENT_API_KEY_ENV="" RIX_AGENT=claude run rix_resolve_agent_key_env
  [ "$status" -eq 0 ]
  [ "$output" = "ANTHROPIC_API_KEY" ]
}

@test "defaults to OPENCODE_API_KEY for opencode" {
  AGENT_API_KEY_ENV="" RIX_AGENT=opencode run rix_resolve_agent_key_env
  [ "$status" -eq 0 ]
  [ "$output" = "OPENCODE_API_KEY" ]
}

@test "normalizes case/whitespace before matching the agent" {
  AGENT_API_KEY_ENV="" RIX_AGENT=" Claude " run rix_resolve_agent_key_env
  [ "$status" -eq 0 ]
  [ "$output" = "ANTHROPIC_API_KEY" ]
}

@test "pi requires an explicit agent-api-key-env" {
  AGENT_API_KEY_ENV="" RIX_AGENT=pi run rix_resolve_agent_key_env
  [ "$status" -ne 0 ]
  [[ "$output" == *"agent-api-key-env is required when agent=pi"* ]]
}

@test "caller override passes through when credential-shaped" {
  AGENT_API_KEY_ENV=AWS_ACCESS_KEY_ID RIX_AGENT=pi run rix_resolve_agent_key_env
  [ "$status" -eq 0 ]
  [ "$output" = "AWS_ACCESS_KEY_ID" ]
}

@test "rejects a non-credential-shaped override" {
  AGENT_API_KEY_ENV=NOT_A_CREDENTIAL RIX_AGENT=claude run rix_resolve_agent_key_env
  [ "$status" -ne 0 ]
  [[ "$output" == *"must be a credential-shaped environment variable name"* ]]
}

@test "rejects an override that would clobber rix's own runtime vars" {
  AGENT_API_KEY_ENV=RIX_REPO RIX_AGENT=claude run rix_resolve_agent_key_env
  [ "$status" -ne 0 ]
  [[ "$output" == *"must be a credential-shaped environment variable name"* ]]
}

@test "exports nothing when AGENT_API_KEY is empty" {
  run bash -c "
    AGENT_API_KEY='' RIX_AGENT=claude
    source '${BATS_TEST_DIRNAME}/../../.github/actions/resolve-agent-credential/resolve-agent-credential.sh'
    echo \"ANTHROPIC_API_KEY=[\${ANTHROPIC_API_KEY:-unset}]\"
  "
  [ "$status" -eq 0 ]
  [[ "$output" == *"ANTHROPIC_API_KEY=[unset]"* ]]
}

@test "exports the resolved credential when AGENT_API_KEY is set" {
  run bash -c "
    AGENT_API_KEY=secret-value RIX_AGENT=claude
    source '${BATS_TEST_DIRNAME}/../../.github/actions/resolve-agent-credential/resolve-agent-credential.sh'
    echo \"ANTHROPIC_API_KEY=\$ANTHROPIC_API_KEY\"
  "
  [ "$status" -eq 0 ]
  [[ "$output" == *"ANTHROPIC_API_KEY=secret-value"* ]]
}
