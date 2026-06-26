#!/usr/bin/env bash
# Downloads the rix release binary matching the current runner's OS/arch and extracts it to
# ./rix-bin, then exports the resolved binary path as RIX_BIN (via $GITHUB_ENV when run in
# Actions). Used by .github/workflows/job.yml.
#
# The OS/asset-resolution logic lives in small functions so it is unit-tested by
# tests/scripts/download-rix.bats without any network access — the recurring class of bug here
# (platform mapping, the Windows rix.exe name, a missing RID) is exactly what those tests cover.
#
# Env:
#   RIX_REPO_SLUG  owner/name of the repo publishing rix releases (required)
#   VERSION        release tag, or "latest"/empty (default: latest)
#   GH_TOKEN       token for the releases REST API (optional; only raises rate limits)
# Test hooks (override auto-detection / the network call):
#   RIX_UNAME_S, RIX_UNAME_M   stand in for `uname -s` / `uname -m`
#   RIX_RELEASE_JSON           releases-API JSON to parse instead of calling curl
set -euo pipefail

# Maps the runner platform to a release RID and archive extension. Echoes "<rid> <ext>".
# Releases ship linux-x64, linux-arm64, osx-x64, osx-arm64 (.tar.gz) and win-x64 (.zip).
rix_detect_target() {
  local sys machine os cpu
  sys="${RIX_UNAME_S:-$(uname -s)}"
  machine="${RIX_UNAME_M:-$(uname -m)}"
  case "$sys" in
    Linux)  os=linux ;;
    Darwin) os=osx ;;
    MINGW*|MSYS*|CYGWIN*) os=win ;;
    *) echo "Unsupported OS: $sys" >&2; return 1 ;;
  esac
  case "$machine" in
    x86_64|amd64)  cpu=x64 ;;
    arm64|aarch64) cpu=arm64 ;;
    *) echo "Unsupported architecture: $machine" >&2; return 1 ;;
  esac
  if [[ "$os" == "win" ]]; then
    echo "$os-$cpu zip"
  else
    echo "$os-$cpu tar.gz"
  fi
}

# Reads releases-API JSON from stdin and echoes the first asset download URL for <rid>.<ext>,
# or nothing (still exit 0) when there is no match — so the caller can report a friendly error
# instead of tripping `set -e`/`pipefail` on grep's no-match status.
rix_asset_url() {
  local rid="$1" ext="$2"
  { grep -o "https://[^\"]*rix-[^\"]*-$rid\.$ext" || true; } | head -n1
}

rix_main() {
  : "${RIX_REPO_SLUG:?RIX_REPO_SLUG is required}"
  local version="${VERSION:-latest}"
  local target rid ext api json asset_url archive bin

  target="$(rix_detect_target)"
  rid="${target% *}"
  ext="${target#* }"

  if [[ "$version" == "latest" || -z "$version" ]]; then
    api="https://api.github.com/repos/$RIX_REPO_SLUG/releases/latest"
  else
    api="https://api.github.com/repos/$RIX_REPO_SLUG/releases/tags/$version"
  fi

  if [[ -n "${RIX_RELEASE_JSON:-}" ]]; then
    json="$RIX_RELEASE_JSON"
  else
    local -a auth=()
    [[ -n "${GH_TOKEN:-}" ]] && auth=(-H "Authorization: Bearer $GH_TOKEN")
    json="$(curl -fsSL "${auth[@]}" -H "Accept: application/vnd.github+json" "$api")"
  fi

  asset_url="$(printf '%s' "$json" | rix_asset_url "$rid" "$ext")"
  if [[ -z "$asset_url" ]]; then
    echo "No rix-*-$rid.$ext asset in release '$version' of $RIX_REPO_SLUG" >&2
    return 1
  fi

  archive="rix-archive.$ext"
  # The asset download is unauthenticated: rix releases are public, and forwarding the API auth
  # header through GitHub's redirect to S3 would be rejected.
  curl -fSL -o "$archive" "$asset_url"
  mkdir -p rix-bin
  # `tar -xf` extracts both .tar.gz (GNU tar) and .zip (bsdtar on Windows) — no unzip dependency.
  tar -xf "$archive" -C rix-bin

  # The Windows build is rix.exe; every other RID is rix. Resolve the real path so later steps
  # never have to assume a name.
  if [[ -f rix-bin/rix.exe ]]; then
    bin="rix-bin/rix.exe"
  else
    bin="rix-bin/rix"
    chmod +x "$bin"
  fi
  echo "Resolved rix binary: $bin"
  if [[ -n "${GITHUB_ENV:-}" ]]; then
    echo "RIX_BIN=$bin" >> "$GITHUB_ENV"
  fi
}

# Only run when executed directly, so the bats tests can source this file and exercise the
# functions in isolation.
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
  rix_main
fi
