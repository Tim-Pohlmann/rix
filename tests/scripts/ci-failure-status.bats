#!/usr/bin/env bats
# Guards the one contract rix's C# and its workflow bash share by convention rather than by any
# compiler: the "status" discriminator of the JSON `rix ci-failure` prints. A status added on the
# C# side without a matching branch in detect-ci-failure/action.yml falls into that case's `*)`,
# which reports a perfectly healthy run as "did not produce a usable result" - a failure mode that
# only shows up in a target repo's Actions tab, long after the C# tests have gone green.
#
# Both sides are read out of the source files here rather than restated, so this can't drift the
# way a hand-written list of statuses would.

REPO_ROOT="${BATS_TEST_DIRNAME}/../.."
ACTION="${REPO_ROOT}/.github/actions/detect-ci-failure/action.yml"

# `rix ci-failure` prints an ICiFailureResult and nothing else - it reports a verdict and stops,
# leaving the agent run (and its own IJobResult) to a separate job - so this one union is the whole
# set the action has to answer for, "detected" included.
emitted_statuses() {
  grep -hoP 'JsonDerivedType\([^,]+, "\K[^"]+' \
    "${REPO_ROOT}/src/Rix/CiFailure/CiFailureResult.cs" |
    sort -u
}

# The labels of the action's one `case` block, one per line, with `a | b)` split apart and the
# catch-all dropped - it's the branch this test exists to keep runs out of, not a handled status.
handled_statuses() {
  sed -n '/^ *case /,/^ *esac/p' "$ACTION" |
    grep -oP '^\s*\K[a-zA-Z|* ]+(?=\))' |
    tr '|' '\n' |
    tr -d ' ' |
    grep -vx '\*' |
    sort -u
}

# Names the offending statuses rather than only failing, so the fix is obvious from the CI log.
assert_empty() {
  local message="$1" offenders="$2"
  if [ -n "$offenders" ]; then
    echo "${message}: $(echo "$offenders" | tr '\n' ' ')" >&2
    return 1
  fi
}

@test "every status rix ci-failure can print is handled by detect-ci-failure's case block" {
  assert_empty "printed by rix but not handled in detect-ci-failure/action.yml" \
    "$(comm -23 <(emitted_statuses) <(handled_statuses))"
}

@test "every branch of detect-ci-failure's case block names a status rix can actually print" {
  assert_empty "handled in detect-ci-failure/action.yml but never printed by rix" \
    "$(comm -13 <(emitted_statuses) <(handled_statuses))"
}
