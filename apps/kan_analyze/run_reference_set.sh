#!/usr/bin/env bash
# Reference-set analyzer: run the performance descriptor (kan_analyze) over
# a folder of songs and collect per-song logs (text + JSON), the repeatable
# version of the reference run.
#
# Usage:
#   ./run_reference_set.sh [audio_dir] [out_dir]   (defaults: test_results/audio,
#                                                  test_results/logs)
#   ./run_reference_set.sh --demo [out_dir]        regenerate synth scenarios
#                                                 (audio + logs)
#
# All test material and results live under test_results/ (gitignored):
#   test_results/audio/  input audio (recordings + synthesized scenarios)
#   test_results/logs/   one .log + one .json descriptor per source
#
# Songs are matched to instrument profiles by filename substring (lowercase):
#   piano  -> piano profile   (e.g. nier-weight-of-the-world_piano.mp3,
#                              lisa-homura_piano.mp3)
#   ocarina/xun/frieren/blue-bird/mononoke -> ocarina profile
#   flute/recorder -> flute profile
#   guitar -> guitar profile
# anything else -> ocarina (override by renaming the file).
#
# This script and the analyzer never download audio; provide recordings
# yourself (WAV/MP3/FLAC/OGG). For the six reference songs:
#   1. Nier Automata - Weight of the World (piano)      -> *_piano.*
#   2. Frieren - Time Flows Ever Onward (ocarina)       -> *_ocarina.*
#   3. Elegance From Across Time (xun + piano companion)-> *_xun.*  (MIX
#      flags expected: companion piano = competing fundamentals)
#   4. Naruto - Blue Bird (ocarina)                     -> *_ocarina.*
#   5. Princess Mononoke - Mononoke Hime (ocarina)      -> *_ocarina.*
#   6. Lisa - Homura (piano)                            -> *_piano.*
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
BIN="$ROOT/build/apps/kan_analyze/kan_analyze"

if [ ! -x "$BIN" ]; then
    echo "kan_analyze not found at $BIN; build first:" >&2
    echo "  cmake -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build -j" >&2
    exit 1
fi

profile_for() {
    local name
    name="$(echo "$1" | tr '[:upper:]' '[:lower:]')"
    case "$name" in
        *piano*)                 echo piano ;;
        *flute*|*recorder*)      echo flute ;;
        *guitar*)                echo guitar ;;
        *ocarina*|*xun*|*frieren*|*blue-bird*|*bluebird*|*mononoke*) echo ocarina ;;
        *)                       echo ocarina ;;
    esac
}

run_one() {
    local src="$1" out="$2" profile="$3"
    mkdir -p "$out"
    local base
    base="$(basename "$src")"
    base="${base%.*}"
    echo "== $base (profile: $profile)"
    # Single pass: text report to .log, machine events to .json.
    "$BIN" "$src" --instrument "$profile" \
        --json-out "$out/$base.json" > "$out/$base.log" 2>&1 || true
    head -5 "$out/$base.log"
    echo "   -> $out/$base.log, $out/$base.json"
}

if [ "${1:-}" = "--demo" ]; then
    OUT="${2:-$ROOT/test_results/logs}"
    AUD="$ROOT/test_results/audio"
    mkdir -p "$OUT" "$AUD"
    echo "# demo run (built-in scenarios) -> $AUD/ (audio) + $OUT/ (logs)"
    for s in ocarina piano duet fx hop; do
        prof=ocarina; [ "$s" = piano ] && prof=piano
        echo "== synth:$s (profile: $prof)"
        "$BIN" --synth "$s" --instrument "$prof" \
            --out "$AUD/synth-$s.wav" \
            --json-out "$OUT/synth-$s.json" > "$OUT/synth-$s.log" 2>&1 || true
        head -5 "$OUT/synth-$s.log"
        echo "   -> $AUD/synth-$s.wav, $OUT/synth-$s.log, $OUT/synth-$s.json"
    done
    echo "done. test_results/ holds audio + logs; both are gitignored."
    exit 0
fi

DIR="${1:-$ROOT/test_results/audio}"
OUT="${2:-$ROOT/test_results/logs}"
shopt -s nullglob
FILES=("$DIR"/*.wav "$DIR"/*.mp3 "$DIR"/*.flac "$DIR"/*.ogg)
if [ ${#FILES[@]} -eq 0 ]; then
    echo "no audio files in $DIR (wav/mp3/flac/ogg)" >&2
    exit 1
fi

echo "# reference run: $DIR -> $OUT/"
for f in "${FILES[@]}"; do
    profile="$(profile_for "$(basename "$f")")"
    run_one "$f" "$OUT" "$profile"
done
echo "done. $OUT/ holds one .log + one .json per song."
