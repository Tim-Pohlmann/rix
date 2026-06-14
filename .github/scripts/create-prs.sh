#!/usr/bin/env bash
# Materialize the bundles produced by `rix job` into real pull requests.
#
# For each entry in <output-dir>/result.json:
#   - fetch the bundle's branch into a clone of the target repo,
#   - push the branch to the target remote (failing if it already exists),
#   - open a PR with the recorded title/body.
#
# Usage: create-prs.sh <output-dir> <target-repo> <write-token>
#   output-dir   directory holding result.json and *.bundle (from `rix job --output-dir`)
#   target-repo  owner/name of the repo to open PRs against
#   write-token  PAT with contents:write + pull-requests:write on the target repo
set -euo pipefail

output_dir="$1"
target_repo="$2"
write_token="$3"

result="$output_dir/result.json"
if [[ ! -f "$result" ]]; then
  echo "Error: $result not found" >&2
  exit 1
fi

status="$(jq -r '.status' "$result")"
if [[ "$status" != "success" ]]; then
  echo "Error: job did not succeed (status: $status)" >&2
  exit 1
fi

count="$(jq '.pendingPrRequests | length' "$result")"
if [[ "$count" -eq 0 ]]; then
  echo "No pending PRs to create."
  exit 0
fi

clone_dir="$(mktemp -d)"
git clone "https://x-access-token:${write_token}@github.com/${target_repo}.git" "$clone_dir"

export GH_TOKEN="$write_token"

i=0
while [[ "$i" -lt "$count" ]]; do
  branch="$(jq -r ".pendingPrRequests[$i].branch" "$result")"
  base="$(jq -r ".pendingPrRequests[$i].baseBranch" "$result")"
  title="$(jq -r ".pendingPrRequests[$i].title" "$result")"
  body="$(jq -r ".pendingPrRequests[$i].body" "$result")"
  bundle="$output_dir/$(jq -r ".pendingPrRequests[$i].bundleFile" "$result")"

  echo "Creating PR for $branch (base: $base)"

  if git -C "$clone_dir" ls-remote --exit-code --heads origin "$branch" >/dev/null 2>&1; then
    echo "Error: branch '$branch' already exists on $target_repo" >&2
    exit 1
  fi

  git -C "$clone_dir" fetch "$bundle" "$branch:$branch"
  git -C "$clone_dir" push origin "$branch"

  url="$(gh pr create -R "$target_repo" --head "$branch" --base "$base" --title "$title" --body "$body")"
  echo "Opened: $url"
  if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
    echo "- [$title]($url)" >> "$GITHUB_STEP_SUMMARY"
  fi

  i=$((i + 1))
done

rm -rf "$clone_dir"
