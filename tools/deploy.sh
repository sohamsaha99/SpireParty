#!/usr/bin/env bash
# Deploys a built SpireParty into <Sts2GameDir>/mods/SpireParty/.
# Usage: tools/deploy.sh [Debug|Release] [--build]
set -euo pipefail

configuration="Debug"
build=0
for arg in "$@"; do
  case "$arg" in
    --build) build=1 ;;
    Debug|Release) configuration="$arg" ;;
    *) echo "unknown argument: $arg" >&2; exit 2 ;;
  esac
done

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
proj="$root/src/SpireParty/SpireParty.csproj"

if pgrep -f "SlayTheSpire2" >/dev/null 2>&1; then
  echo "Slay the Spire 2 is running. Close the game before deploying." >&2
  exit 1
fi

if [[ $build -eq 1 ]]; then
  dotnet build "$proj" -c "$configuration"
fi

# Ask MSBuild so the path logic lives only in Directory.Build.props.
mods_dir="$(dotnet msbuild "$proj" -nologo -getProperty:Sts2ModsDir | tr -d '\r' | xargs)"
if [[ -z "$mods_dir" ]]; then
  echo "Could not resolve Sts2ModsDir from MSBuild." >&2
  exit 1
fi

out_dir="$root/src/SpireParty/bin/$configuration/net9.0"
if [[ ! -f "$out_dir/SpireParty.dll" ]]; then
  echo "Build output not found: $out_dir/SpireParty.dll (run with --build)" >&2
  exit 1
fi

mkdir -p "$mods_dir"
cp -f "$out_dir/SpireParty.dll" "$mods_dir/"
[[ -f "$out_dir/SpireParty.pdb" ]] && cp -f "$out_dir/SpireParty.pdb" "$mods_dir/"
cp -f "$out_dir/SpireParty.json" "$mods_dir/"

for dir in lib web; do
  if [[ -d "$root/$dir" ]]; then
    rm -rf "${mods_dir:?}/$dir"
    cp -r "$root/$dir" "$mods_dir/$dir"
  fi
done
[[ -d "$out_dir/lib" ]] && cp -rf "$out_dir/lib" "$mods_dir/"

for file in LICENSE THIRD_PARTY_NOTICES.md; do
  [[ -f "$root/$file" ]] && cp -f "$root/$file" "$mods_dir/"
done

echo "Deployed SpireParty ($configuration) to $mods_dir"
