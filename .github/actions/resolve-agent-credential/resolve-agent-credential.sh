#!/usr/bin/env bash
# Resolves the environment variable name AGENT_API_KEY should be exported as, validates it, and
# exports it under that name. Meant to be `source`d (never executed) from the same shell step
# that invokes rix, so the export applies only to that one invocation and nowhere else in the
# job - unlike $GITHUB_ENV, which would leak the secret into every later step. Shared by
# run-rix-job and run-ci-failure-job so this logic lives in one place instead of two copies.
#
# Env:
#   AGENT_API_KEY       secret value to export; if unset/empty, nothing happens (e.g. opencode's
#                        free default model needs no key)
#   AGENT_API_KEY_ENV   caller-supplied override env var name; if set, only validated
#   RIX_AGENT           coding agent being run: 'opencode' (default), 'claude', or 'pi'

# Echoes the env var name AGENT_API_KEY should be exported as, or errors out.
rix_resolve_agent_key_env() {
  local key_env="$AGENT_API_KEY_ENV" agent_normalized

  if [ -z "$key_env" ]; then
    # Caller didn't say which env var to use - default based on agent, since opencode and claude
    # expect different credentials. Normalize case/whitespace the same way rix's own
    # AgentKindParser does, so e.g. "Claude" still matches.
    agent_normalized=$(printf '%s' "$RIX_AGENT" | tr '[:upper:]' '[:lower:]' | xargs)
    case "$agent_normalized" in
      claude) key_env=ANTHROPIC_API_KEY ;;
      pi)
        # pi is multi-provider with no single default credential, unlike opencode's own
        # free-model provider - the caller must say which env var to use.
        echo "::error::agent-api-key-env is required when agent=pi and agent-api-key is set" >&2
        return 1
        ;;
      *) key_env=OPENCODE_API_KEY ;;
    esac
  fi

  # Restrict to credential-shaped suffixes covering opencode's supported providers
  # (ANTHROPIC_API_KEY, AWS_ACCESS_KEY_ID, GOOGLE_APPLICATION_CREDENTIALS,
  # SNOWFLAKE_CORTEX_TOKEN, ...), rather than deny-listing every internal/runtime variable a
  # caller could otherwise clobber. A couple of the allowed suffixes (e.g. _TOKEN) would
  # otherwise overlap with vars the calling step relies on, so also block RIX_*/AGENT_API_KEY*
  # (rix's own runtime vars) and GITHUB_* (GITHUB_TOKEN etc) by name.
  if [[ "$key_env" =~ ^(RIX_|AGENT_API_KEY|GITHUB_) ]] \
    || ! [[ "$key_env" =~ ^[A-Z][A-Z0-9_]*_(API_KEY|TOKEN|KEY_ID|ACCESS_KEY|CREDENTIALS|PROFILE|ACCOUNT|PROJECT|PAT|ARN|RESOURCE_NAME)$ ]]; then
    echo "::error::agent-api-key-env '$key_env' must be a credential-shaped environment variable name (e.g. *_API_KEY, *_TOKEN, *_ACCESS_KEY_ID) and not one of rix's own runtime variables" >&2
    return 1
  fi

  echo "$key_env"
}

if [ -n "$AGENT_API_KEY" ]; then
  rix_key_env=$(rix_resolve_agent_key_env) || exit 1
  export "$rix_key_env"="$AGENT_API_KEY"
  unset rix_key_env
fi
