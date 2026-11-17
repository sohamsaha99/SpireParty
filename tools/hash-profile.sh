#!/usr/bin/env bash
# Snapshot or compare the game's profile folders, to prove a party run wrote nothing there.
# Read-only: never changes a file.
#
#   tools/hash-profile.sh snapshot before.txt [user-data-dir]
#   tools/hash-profile.sh compare before.txt after.txt
#
# Covers <user data>/steam/** and <user data>/default/**. Pass the user data dir if the
# default guess is wrong (the SpireParty startup line in the game log prints it).
set -euo pipefail

mode="${1:-}"
case "$mode" in
  snapshot)
    out="${2:?usage: snapshot <file> [user-data-dir]}"
    dir="${3:-${XDG_DATA_HOME:-$HOME/.local/share}/SlayTheSpire2}"
    : > "$out"
    for tree in steam default; do
      [[ -d "$dir/$tree" ]] || continue
      (cd "$dir" && find "$tree" -type f -print0 | sort -z | while IFS= read -r -d '' f; do
        printf '%s\t%s\t%s\n' "$(sha256sum "$f" | cut -d' ' -f1)" "$(stat -c %s "$f")" "$f"
      done) >> "$out"
    done
    echo "Snapshot of $(wc -l < "$out") files written to $out"
    ;;
  compare)
    before="${2:?usage: compare <before> <after>}"
    after="${3:?usage: compare <before> <after>}"
    changes=$(diff <(sort -t$'\t' -k3 "$before" | awk -F'\t' '{print $3"\t"$1}') \
                   <(sort -t$'\t' -k3 "$after"  | awk -F'\t' '{print $3"\t"$1}') || true)
    if [[ -z "$changes" ]]; then echo "No changes in the profile folders."; else echo "$changes"; fi
    ;;
  *)
    echo "usage: $0 snapshot <file> [user-data-dir] | compare <before> <after>" >&2
    exit 2
    ;;
esac
