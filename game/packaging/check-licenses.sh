#!/usr/bin/env bash
# Verify the licence payload of a release archive (audit C3, November 2026;
# docs/release-credits.md GAP 9). Every entry of the ONE list
# game/packaging/licenses.manifest ("source destination") must exist:
#
#   - as a source file in the repository (non-empty), and
#   - as <payload>/licenses/<destination> in the assembled payload
#     (non-empty).
#
# Plus one drift tripwire: every file in libkan/licenses/ must be listed in
# the manifest, so a licence added there cannot silently skip the archive.
#
# game/packaging/windows/assemble.sh runs this after building the payload
# (every platform goes through that script) and the workflow runs it again
# as its own visible CI step. Exit 0 = complete; exit 1 = something is
# missing, one plainly worded line per problem.
#
# Usage: game/packaging/check-licenses.sh [payload-dir]   (default dist/game)
set -euo pipefail

repo="$(cd "$(dirname "$0")/../.." && pwd)"
payload="${1:-dist/game}"
manifest="$repo/game/packaging/licenses.manifest"

if [ ! -f "$manifest" ]; then
    echo "licence list missing: $manifest" >&2
    exit 1
fi

missing=0
listed_sources=""

while read -r src dst _rest; do
    case "$src" in
        ''|'#'*) continue ;;
    esac
    if [ -z "$dst" ]; then
        echo "manifest entry without a destination name: '$src'" >&2
        missing=1
        continue
    fi
    listed_sources="$listed_sources $src"
    if [ ! -s "$repo/$src" ]; then
        echo "listed licence source missing or empty: $src" >&2
        missing=1
    fi
    if [ ! -s "$payload/licenses/$dst" ]; then
        echo "missing from the payload: licenses/$dst (from $src)" >&2
        missing=1
    fi
done < "$manifest"

# Drift tripwire: everything in libkan/licenses/ belongs in the list.
for f in "$repo"/libkan/licenses/*; do
    [ -e "$f" ] || continue
    rel="libkan/licenses/$(basename "$f")"
    case "$listed_sources" in
        *" $rel "*) ;;
        *)
            echo "not listed in game/packaging/licenses.manifest: $rel" >&2
            missing=1
            ;;
    esac
done

if [ "$missing" != 0 ]; then
    echo "licence payload INCOMPLETE ($payload/licenses)" >&2
    exit 1
fi
echo "licence payload complete: $(ls "$payload/licenses" | wc -l) files in $payload/licenses"
