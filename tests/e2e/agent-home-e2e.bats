#!/usr/bin/env bats
# End-to-end tests for `rix agent`'s factory-repo agent home (--factory-repo/--agent-home-path): the
# files an operator keeps in one repo and wants in place in the runner's home before the agent
# starts. The pieces are unit tested separately - GitCliTests for the sparse clone's git commands,
# AgentRunnerTests against a stubbed git, DirectoryMergeTests against a temp tree - so what is only
# observable here is them meeting for real: a sparse clone of a real GitHub repo, merged into the
# home the agent actually reads, in a job that still succeeds afterwards.
#
# HOME is redirected per test. rix resolves the runner home via
# Environment.GetFolderPath(UserProfile), which consults $HOME on Unix (see AgentOptions.ReadAgentHome
# and the note there), so this is enough to keep every copy inside $BATS_TEST_TMPDIR and out of the
# real home of whoever runs these.
#
# The fixture the happy path copies is tests/fixtures/agent-home in this same repo (RIX_REPO doubles
# as the factory repo). The fetch is a depth-1 clone with no --branch, so it reads that repo's
# *default branch* and never the branch under test: until the fixture is on main, the happy path
# cannot run at all. It therefore skips - a skip that clears itself once the fixture lands on main,
# and that still fails rather than skips if the fixture is missing from this checkout too, which
# would mean it was deleted rather than merely not merged yet.
#
# Requires: RIX_BIN (a built rix binary), RIX_REPO/RIX_READ_TOKEN (a real repo + token, used both as
# the repo to work in and as the factory repo to fetch from), and network access to clone it. No npm
# and no agent key: the agent here is tests/e2e/stub-opencode (see setup).

# The directory inside the factory repo that the happy path asks for, named once: it is also the
# path this checkout is searched at, which is what tells "not merged yet" from "deleted".
AGENT_HOME_PATH=tests/fixtures/agent-home

setup() {
  load ../scripts/result-schema
  : "${RIX_BIN:?RIX_BIN must point at a built rix binary}"
  : "${RIX_REPO:?RIX_REPO must name a real GitHub repo to clone (e.g. Tim-Pohlmann/rix)}"
  : "${RIX_READ_TOKEN:?RIX_READ_TOKEN must be a GitHub token with read access to RIX_REPO}"
  export RIX_PROMPT="Make no changes to any files and do not call the PR endpoint. Just reply with the text: OK"
  # The agent is incidental to these tests - what they assert is the clone and the merge - but it
  # does run: the copy has to survive a real job. So it is the stub (see tests/e2e/stub-opencode),
  # put on PATH ahead of any real opencode, which rix then uses without an npm install or a key.
  # These legs never needed a particular model's judgement, and after opencode withdrew its keyless
  # tier no real CLI runs here for free - see rix-job-e2e.bats for the legs that do want a real one.
  export RIX_AGENT=opencode
  # Installed under the name rix looks up, not its own: rix resolves the agent CLI by name on PATH
  # and accepts whatever answers `opencode --version`, which is what lets a stub stand in with no
  # rix-side switch for it (see EnsureInstalledViaNpmAsync). Its own directory, so this is the only
  # file these tests add as a command.
  mkdir -p "$BATS_TEST_TMPDIR/stub-bin"
  install -m 0755 "$BATS_TEST_DIRNAME/stub-opencode" "$BATS_TEST_TMPDIR/stub-bin/opencode"
  PATH="$BATS_TEST_TMPDIR/stub-bin:$PATH"
  export PATH
  export RIX_FACTORY_REPO="$RIX_REPO"
  # Well under rix's 30-minute default, because the agent turn is incidental to what these legs
  # assert - the stub only has to reply OK. Kept now that the stub cannot hang: it is the backstop
  # for rix's own side of the wait, which should fail this in minutes rather than hold a runner for
  # half an hour.
  export RIX_TIMEOUT=5
  export RIX_OUTPUT_DIR="$BATS_TEST_TMPDIR/out"
  export RIX_WORK_DIR="$BATS_TEST_TMPDIR/work"
  mkdir -p "$RIX_OUTPUT_DIR" "$RIX_WORK_DIR"

  local_fixture="$BATS_TEST_DIRNAME/../../$AGENT_HOME_PATH"

  # Both of these are explained in rix-job-e2e.bats's setup: a relative RIX_BIN has to be resolved
  # while the caller's directory is still current, and rix is then launched from a directory these
  # tests own rather than from the caller's checkout.
  case "$RIX_BIN" in
    /*) ;;
    */*) RIX_BIN="$PWD/$RIX_BIN" ;;
  esac
  export RIX_BIN
  cd "$BATS_TEST_TMPDIR" || return 1

  # The home under test. Empty except for what each test puts there itself - although rix installs
  # the agent CLI before it copies anything (see AgentRunner.RunAsync), so npm's own files land here
  # too and "nothing else is in HOME" is not something these tests can assert.
  export HOME="$BATS_TEST_TMPDIR/home"
  mkdir -p "$HOME"
}

# Same check rix-job-e2e.bats makes for the same reason, and for one this file has to itself: it is
# the only place a real binary prints a setupFailure, so the schema's setupFailure variant would
# otherwise only ever be checked against results written by hand.
assert_result_matches_schema() {
  assert_matches_schema agent-result.schema.json "$(cat "$RIX_OUTPUT_DIR/result.json")"
}

result_field() {
  jq -r ".$1" "$RIX_OUTPUT_DIR/result.json"
}

# The status GitHub answers for the fixture directory on the factory repo's default branch - the
# only branch the fetch reads. The contents endpoint takes no ref here on purpose: asking about the
# default branch is the whole point.
fixture_status_on_default_branch() {
  curl -sS -o /dev/null -w '%{http_code}' \
    -H "Authorization: Bearer $RIX_READ_TOKEN" \
    -H "Accept: application/vnd.github+json" \
    -H "X-GitHub-Api-Version: 2022-11-28" \
    "https://api.github.com/repos/$RIX_REPO/contents/$AGENT_HOME_PATH"
}

@test "the factory repo's agent home lands in the runner home, keeping what is already there" {
  local fixture_status
  fixture_status="$(fixture_status_on_default_branch)"
  case "$fixture_status" in
    200) ;;
    404)
      [ -d "$local_fixture" ] || {
        echo "$AGENT_HOME_PATH is missing from $RIX_REPO's default branch *and* from this checkout:" \
          "the fixture was deleted, so this test can never run again" >&2
        return 1
      }
      skip "$AGENT_HOME_PATH is not on $RIX_REPO's default branch yet, and that is the only branch the fetch reads"
      ;;
    *)
      echo "unexpected HTTP $fixture_status asking $RIX_REPO for $AGENT_HOME_PATH" >&2
      return 1
      ;;
  esac

  # Content of the runner's own at a path the fixture has too. The merge must leave this alone: the
  # runner's files win over the factory repo's (see DirectoryMerge.CopySkippingExisting).
  printf 'runner\n' >"$HOME/.rix-e2e-marker"

  export RIX_AGENT_HOME_PATH="$AGENT_HOME_PATH"
  run "$RIX_BIN" agent
  [ "$status" -eq 0 ]
  assert_result_matches_schema
  [ "$(result_field status)" = success ]

  [ "$(cat "$HOME/.rix-e2e-marker")" = runner ]

  # The assertion with the teeth: a fetch that silently copied nothing would still pass the check
  # above. This file exists only in the fixture, and only under a directory the merge had to create.
  [ -f "$HOME/nested/context.md" ]
  grep -q 'Nested agent home file' "$HOME/nested/context.md"
}

@test "a path the factory repo does not have ends the job as a setup failure naming that path" {
  export RIX_AGENT_HOME_PATH=tests/fixtures/not-an-agent-home
  run "$RIX_BIN" agent

  # 2 rather than 1: the operator's configuration is wrong, which is a different thing from the
  # agent's work failing (see ExitCodes.SetupFailed), and job.yml reports the two differently.
  [ "$status" -eq 2 ]
  assert_result_matches_schema
  [ "$(result_field status)" = setupFailure ]
  local error
  error="$(result_field error)"
  [[ "$error" == *"agent home fetch failed"* ]]
  # Names the repo and the path it looked for, so a typo in either is visible from the result alone.
  [[ "$error" == *"$RIX_REPO"* ]]
  [[ "$error" == *tests/fixtures/not-an-agent-home* ]]
}
