#!/usr/bin/env bats
# Unit tests for .github/scripts/download-rix.sh. They source the script (which guards its main
# entry point) and exercise the pure OS/asset-resolution functions with stubbed uname output and
# a canned releases-API payload — no network, no real download.

setup() {
  source "${BATS_TEST_DIRNAME}/../../.github/scripts/download-rix.sh"

  # A realistic /releases/latest payload covering every published RID.
  RELEASE_JSON='{"assets":[
    {"browser_download_url":"https://github.com/Tim-Pohlmann/rix/releases/download/v1.2.0/rix-1.2.0-linux-x64.tar.gz"},
    {"browser_download_url":"https://github.com/Tim-Pohlmann/rix/releases/download/v1.2.0/rix-1.2.0-linux-arm64.tar.gz"},
    {"browser_download_url":"https://github.com/Tim-Pohlmann/rix/releases/download/v1.2.0/rix-1.2.0-osx-x64.tar.gz"},
    {"browser_download_url":"https://github.com/Tim-Pohlmann/rix/releases/download/v1.2.0/rix-1.2.0-osx-arm64.tar.gz"},
    {"browser_download_url":"https://github.com/Tim-Pohlmann/rix/releases/download/v1.2.0/rix-1.2.0-win-x64.zip"}
  ]}'
}

# --- rix_detect_target: platform -> "<rid> <ext>" ---

@test "detect: linux x86_64 -> linux-x64 tar.gz" {
  RIX_UNAME_S=Linux RIX_UNAME_M=x86_64 run rix_detect_target
  [ "$status" -eq 0 ]
  [ "$output" = "linux-x64 tar.gz" ]
}

@test "detect: linux aarch64 -> linux-arm64 tar.gz" {
  RIX_UNAME_S=Linux RIX_UNAME_M=aarch64 run rix_detect_target
  [ "$status" -eq 0 ]
  [ "$output" = "linux-arm64 tar.gz" ]
}

@test "detect: macOS arm64 -> osx-arm64 tar.gz" {
  RIX_UNAME_S=Darwin RIX_UNAME_M=arm64 run rix_detect_target
  [ "$status" -eq 0 ]
  [ "$output" = "osx-arm64 tar.gz" ]
}

@test "detect: Windows (MINGW) x86_64 -> win-x64 zip" {
  RIX_UNAME_S=MINGW64_NT-10.0 RIX_UNAME_M=x86_64 run rix_detect_target
  [ "$status" -eq 0 ]
  [ "$output" = "win-x64 zip" ]
}

@test "detect: unsupported OS fails with a message" {
  RIX_UNAME_S=Plan9 RIX_UNAME_M=x86_64 run rix_detect_target
  [ "$status" -ne 0 ]
  [[ "$output" == *"Unsupported OS: Plan9"* ]]
}

@test "detect: unsupported arch fails with a message" {
  RIX_UNAME_S=Linux RIX_UNAME_M=riscv64 run rix_detect_target
  [ "$status" -ne 0 ]
  [[ "$output" == *"Unsupported architecture: riscv64"* ]]
}

# --- rix_asset_url: pick the matching asset, and only that one ---

@test "asset: linux-x64 selects exactly the linux-x64 tarball" {
  run bash -c "printf '%s' '$RELEASE_JSON' | { source '${BATS_TEST_DIRNAME}/../../.github/scripts/download-rix.sh'; rix_asset_url linux-x64 tar.gz; }"
  [ "$status" -eq 0 ]
  [ "$output" = "https://github.com/Tim-Pohlmann/rix/releases/download/v1.2.0/rix-1.2.0-linux-x64.tar.gz" ]
}

@test "asset: linux-x64 does not match linux-arm64" {
  run bash -c "printf '%s' '$RELEASE_JSON' | { source '${BATS_TEST_DIRNAME}/../../.github/scripts/download-rix.sh'; rix_asset_url linux-x64 tar.gz; }"
  [[ "$output" != *"arm64"* ]]
}

@test "asset: win-x64 selects the zip" {
  run bash -c "printf '%s' '$RELEASE_JSON' | { source '${BATS_TEST_DIRNAME}/../../.github/scripts/download-rix.sh'; rix_asset_url win-x64 zip; }"
  [ "$status" -eq 0 ]
  [ "$output" = "https://github.com/Tim-Pohlmann/rix/releases/download/v1.2.0/rix-1.2.0-win-x64.zip" ]
}

@test "asset: a RID with no published asset (win-arm64) yields nothing" {
  run bash -c "printf '%s' '$RELEASE_JSON' | { source '${BATS_TEST_DIRNAME}/../../.github/scripts/download-rix.sh'; rix_asset_url win-arm64 zip; }"
  [ "$status" -eq 0 ]
  [ -z "$output" ]
}
