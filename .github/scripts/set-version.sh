#!/usr/bin/env bash
set -euo pipefail
version="$1"

sed -i "s|<Version>[^<]*</Version>|<Version>$version</Version>|" src/Rix/Rix.csproj
csproj_version="$(grep -oPm1 '(?<=<Version>)[^<]+' src/Rix/Rix.csproj || true)"
if [[ "$csproj_version" != "$version" ]]; then
  echo "Error: failed to update src/Rix/Rix.csproj version to $version (found: ${csproj_version:-<missing>})" >&2
  exit 1
fi
