#!/usr/bin/env bash
set -euo pipefail
version="$1"

sed -i "s|<Version>[^<]*</Version>|<Version>$version</Version>|" src/Rix/Rix.csproj
csproj_version="$(grep -oPm1 '(?<=<Version>)[^<]+' src/Rix/Rix.csproj || true)"
if [[ "$csproj_version" != "$version" ]]; then
  echo "Error: failed to update src/Rix/Rix.csproj version to $version (found: ${csproj_version:-<missing>})" >&2
  exit 1
fi

# The binary the reusable workflows download is pinned in download-rix, and has to move in this
# same commit: the release is cut from the commit this script produces, so whatever that commit's
# workflows name is what every caller pinning that release will run. Derived from the argument
# rather than maintained by hand, and verified the same way as the csproj edit.
pin_file=".github/actions/download-rix/action.yml"
sed -i "s|^\( *default: \)v\?[0-9][^ ]*$|\1v$version|" "$pin_file"
pin_version="$(grep -oPm1 '(?<=^    default: ).+' "$pin_file" || true)"
if [[ "$pin_version" != "v$version" ]]; then
  echo "Error: failed to update $pin_file rix-version default to v$version (found: ${pin_version:-<missing>})" >&2
  exit 1
fi
