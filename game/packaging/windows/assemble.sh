#!/usr/bin/env bash
# Assemble one kan! release payload (the managed part of a build):
# self-contained osu.Desktop publish for a RID plus the kan! plugin files
# (ruleset, Kan.Forge, Kan.Core), the native bridge, brand/ and the
# third-party notices + licence as readable files, checked so a missing
# piece fails the build instead of shipping.
#
# The steps are exactly the ones .github/workflows/build.yml runs; they live
# here (not only in YAML) so a local build can produce the same payload,
# see game/docs/platform-builds.md and, for the Windows test build,
# game/packaging/windows/cross-build-native.sh (native bridge) plus this
# script. The name says windows/ because that is where kan!'s packaging
# scripts live; the steps themselves are RID-generic and the workflow calls
# this for every platform.
#
# Usage: game/packaging/windows/assemble.sh [rid] [out-dir]
#   rid          target runtime id       (default: win-x64)
#   out-dir      payload directory       (default: dist/game)
#   KAN_VERSION  version stamp           (default: 0.0.0, tag builds set it)
#   KAN_NATIVE_LIB  explicit native bridge path (skips the directory search)
#
# Native bridge search (first hit wins), per RID:
#   win-*  kan_forge_native.dll     game/forge/native/build{,/Release},
#                                   game/forge/native/build-win-x64 (cross)
#   osx-*  libkan_forge_native.dylib
#   linux-* libkan_forge_native.so
set -euo pipefail

rid="${1:-win-x64}"
out="${2:-dist/game}"
version="${KAN_VERSION:-0.0.0}"

repo="$(cd "$(dirname "$0")/../../.." && pwd)"
cd "$repo"

# dotnet may be user-local on the build machines (no global installs).
if command -v dotnet > /dev/null 2>&1; then DOTNET=dotnet; else DOTNET="$HOME/.dotnet/dotnet"; fi

echo "== kan! payload: $rid -> $out (version $version)"

# kan's own C# must build without a warning (the make check gate). A local
# build gets the same gate here; CI runs it earlier as its own step too.
# %3B is MSBuild's escape for ';' inside a property value: a raw ';' would
# split the -p: list into bogus properties (MSB1006).
for p in game/core/Kan.Core.csproj game/forge/Kan.Forge.csproj game/ruleset/Kan.Ruleset.csproj; do
    "$DOTNET" build "$p" -c Release -maxcpucount:4 -warnaserror \
        -p:WarningsNotAsErrors="NU1901%3BNU1902%3BNU1903%3BNU1904"
done

# Self-contained publish of the game (Velopack stays dormant: the zip is
# the installer for now).
"$DOTNET" publish game/client/osu.Desktop/osu.Desktop.csproj \
    -c Release -r "$rid" --self-contained true \
    -p:Version="$version" -maxcpucount:4 -o "$out"

# The ruleset plugin, Kan.Forge/Kan.Core ride along (the plugin is loaded
# from next to the executable); brand/ is copied by the fork's csproj
# (fork F-21).
cp game/ruleset/bin/Release/net10.0/osu.Game.Rulesets.Kan.dll "$out/"
cp game/ruleset/bin/Release/net10.0/Kan.Forge.dll "$out/"
cp game/ruleset/bin/Release/net10.0/Kan.Core.dll "$out/"

# The notices and the licence travel with every zip as readable files
# (docs/release-credits.md GAP 8): the notices also embed in osu.Game for
# the about screen, but players read licences offline too.
cp game/client/THIRD_PARTY_NOTICES.md "$out/"
cp game/client/LICENCE "$out/"

# P/Invoke resolves "kan_forge_native" to kan_forge_native.dll /
# libkan_forge_native.dylib / libkan_forge_native.so. Pick the name for
# this RID and search the CMake output directories for it.
case "$rid" in
    win-*)    native=kan_forge_native.dll ;;
    osx-*)    native=libkan_forge_native.dylib ;;
    linux-*)  native=libkan_forge_native.so ;;
    *)        echo "unknown rid: $rid" >&2; exit 1 ;;
esac

found=
if [ -n "${KAN_NATIVE_LIB:-}" ]; then
    cp "$KAN_NATIVE_LIB" "$out/$native"
    found="$KAN_NATIVE_LIB"
else
    for d in game/forge/native/build game/forge/native/build/Release game/forge/native/build-win-x64; do
        if [ -f "$d/$native" ]; then cp "$d/$native" "$out/"; found="$d/$native"; fi
    done
fi
if [ -z "$found" ]; then
    echo "no native bridge library found for $rid ($native)" >&2
    exit 1
fi

for f in osu.Game.Rulesets.Kan.dll Kan.Forge.dll Kan.Core.dll ppy.ManagedBass.dll brand THIRD_PARTY_NOTICES.md LICENCE "$native"; do
    test -e "$out/$f" || { echo "missing from the payload: $f" >&2; exit 1; }
done

# The starter level pack (issue 15): built from charts/official - the MIDI
# sources, the song metadata and the rendered audio committed there - and
# carried in the payload as levels/. The game imports it on first run, once
# per pack version. Building it here (stdlib-only Python, seconds) rather
# than committing the .osz keeps the levels reviewable as data and the zip
# identical on every platform.
PY=$(command -v python3 || command -v python)
"$PY" tools/level_pack/make_level.py pack --all --out charts/official/dist
mkdir -p "$out/levels"
cp charts/official/dist/*.osz charts/official/dist/pack.json "$out/levels/"
test -f "$out/levels/pack.json" || { echo "missing from the payload: levels/pack.json" >&2; exit 1; }
for level in "$out/levels"/*.osz; do
    test -s "$level" || { echo "empty level in the payload: $level" >&2; exit 1; }
done
echo "starter pack: $(ls "$out/levels"/*.osz | wc -l) levels in $out/levels"
echo "payload complete (native: $found)"
