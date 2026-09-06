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
#   VERSION        release tag, or "latest"/empty (default: latest)
#   GH_TOKEN       token for the releases REST API (optional; only raises rate limits)
# Test hooks (override auto-detection / the network call):
#   RIX_UNAME_S, RIX_UNAME_M   stand in for `uname -s` / `uname -m`
#   RIX_RELEASE_JSON           releases-API JSON to parse instead of calling curl

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
  # Escape dots so the extension (e.g. tar.gz) matches literally rather than as regex wildcards.
  local ext_re="${ext//./\\.}"
  # Require the closing JSON quote right after the extension so a sibling asset like
  # rix-…-$rid.tar.gz.sig can't yield a truncated match.
  { grep -o "https://[^\"]*rix-[^\"]*-$rid\.$ext_re\"" || true; } | head -n1 | tr -d '"'
  return 0
}

# Echoes the SHA-256 hex digest of file $1, using whichever tool the runner has: sha256sum on
# Linux/Git-bash, shasum on macOS. Fails (non-zero) with a clear message when neither tool exists
# or hashing errors, so a broken environment never masquerades as a checksum mismatch downstream.
rix_sha256() {
  local file="$1" digest
  if command -v sha256sum >/dev/null 2>&1; then
    digest="$(sha256sum "$file")" || return 1
  elif command -v shasum >/dev/null 2>&1; then
    digest="$(shasum -a 256 "$file")" || return 1
  else
    echo "No SHA-256 tool found (need sha256sum or shasum) to verify the download." >&2
    return 1
  fi
  printf '%s\n' "${digest%% *}"
  return 0
}

# Verifies file $1 against a sha256sum-style line ("<hex>  <name>") $2. Returns non-zero with a
# message on mismatch so the caller can abort before extracting an unverified archive. Kept pure
# (no network) so tests/scripts/download-rix.bats can exercise it directly.
rix_verify_checksum() {
  local file="$1" expected actual
  expected="${2%% *}"
  if [[ -z "$expected" ]]; then
    echo "No expected checksum for $file (empty or malformed .sha256 sidecar)." >&2
    return 1
  fi
  # Propagate a hashing failure (e.g. no SHA-256 tool) as itself, rather than letting an empty
  # digest fall through to a misleading "mismatch".
  if ! actual="$(rix_sha256 "$file")"; then
    return 1
  fi
  if [[ "$expected" != "$actual" ]]; then
    echo "Checksum mismatch for $file: expected '$expected', got '$actual'" >&2
    return 1
  fi
}

rix_main() {
  local repo_slug="Tim-Pohlmann/rix"
  local version="${VERSION:-latest}"
  local target rid ext api json asset_url archive bin

  target="$(rix_detect_target)"
  rid="${target% *}"
  ext="${target#* }"

  if [[ "$version" == "latest" || -z "$version" ]]; then
    api="https://api.github.com/repos/$repo_slug/releases/latest"
  else
    api="https://api.github.com/repos/$repo_slug/releases/tags/$version"
  fi

  if [[ -n "${RIX_RELEASE_JSON:-}" ]]; then
    json="$RIX_RELEASE_JSON"
  else
    local -a auth=()
    [[ -n "${GH_TOKEN:-}" ]] && auth=(-H "Authorization: Bearer $GH_TOKEN")
    json="$(curl -fsSL --proto '=https' --tlsv1.2 "${auth[@]}" -H "Accept: application/vnd.github+json" "$api")"
  fi

  asset_url="$(printf '%s' "$json" | rix_asset_url "$rid" "$ext")"
  if [[ -z "$asset_url" ]]; then
    echo "No rix-*-$rid.$ext asset in release '$version' of $repo_slug" >&2
    return 1
  fi

  archive="rix-archive.$ext"
  # The asset download is unauthenticated: rix releases are public, and forwarding the API auth
  # header through GitHub's redirect to S3 would be rejected.
  curl -fSL --proto '=https' --tlsv1.2 -o "$archive" "$asset_url"

  # Verify the archive against its published .sha256 sidecar before extracting it, so a tampered
  # or truncated download never reaches `tar`.
  curl -fSL --proto '=https' --tlsv1.2 -o "$archive.sha256" "$asset_url.sha256"
  rix_verify_checksum "$archive" "$(cat "$archive.sha256")"

  mkdir -p rix-bin
  # `tar -xf` extracts the .tar.gz on Linux/macOS and the .zip on Windows, where `tar` is bsdtar
  # and reads zips natively — so there is no unzip dependency. (GNU tar on Linux does not read
  # zips, but a .zip is only ever downloaded on Windows.)
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
  # Set strict mode only when executed directly — sourcing (e.g. by the bats tests) must not
  # mutate the caller's shell options.
  set -euo pipefail
  rix_main
fi
