#!/usr/bin/env bats
# Guards the second half of the contract ci-failure-status.bats covers: having found the branch
# for a status, the action still has to print the right values in it. Most results leave most
# fields unset - an untrustedRun carries no outcome or error - and a separator that collapses on
# empty fields silently shifts every later one left, so the ::notice::/::error:: messages come out
# blank or wrong while the run itself looks healthy. That only shows up in a target repo's Actions
# tab, so pin it here.
#
# The action's own parse is extracted and run rather than restated, so this can't pass while the
# action drifts away from it.

ACTION="${BATS_TEST_DIRNAME}/../../.github/actions/run-ci-failure/action.yml"

# Takes the read command whole, following backslash continuations, so it extracts the same thing
# whether the action writes it on one line or several.
extract_parse() {
  awk '/^[[:space:]]*IFS=/ { found = 1 } found { print; if (!/\\$/) exit }' "$ACTION"
}

parse_result() {
  local result="$1"
  eval "$(extract_parse)"
}

@test "an untrustedRun result keeps its branch and head repo in their own fields" {
  parse_result '{"status":"untrustedRun","branch":"main","headRepo":"someone/fork"}'
  [ "$status" = untrustedRun ]
  [ "$outcome" = "" ]
  [ "$error" = "" ]
  [ "$branch" = main ]
  [ "$head_repo" = someone/fork ]
}

@test "a loopGuarded result keeps its commit count and branch in their own fields" {
  parse_result '{"status":"loopGuarded","rixCommits":5,"branch":"rix/fix-the-thing"}'
  [ "$status" = loopGuarded ]
  [ "$rix_commits" = 5 ]
  [ "$branch" = rix/fix-the-thing ]
}

@test "an error result puts its message in error, not in the fields before it" {
  parse_result '{"status":"error","error":"read token lacks actions:read"}'
  [ "$status" = error ]
  [ "$outcome" = "" ]
  [ "$error" = "read token lacks actions:read" ]
}

@test "a skipped result keeps its outcome" {
  parse_result '{"status":"skipped","outcome":"succeeded"}'
  [ "$status" = skipped ]
  [ "$outcome" = succeeded ]
}

@test "a tab or newline inside a field value stays in that field" {
  # @tsv escapes both before the separators are translated, so neither can pose as one - worth
  # pinning, since the error text is the one field rix builds out of a failing run's own output.
  parse_result '{"status":"error","error":"line one\nline two\twith a tab"}'
  [ "$status" = error ]
  [ "$error" = 'line one\nline two\twith a tab' ]
}
