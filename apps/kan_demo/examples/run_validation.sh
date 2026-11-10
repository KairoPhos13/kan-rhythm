#!/usr/bin/env bash
# Validation pack: run the kan_demo LIVE pipeline over the example songs
# (--input = exact live consumer, offline) and collect, per song:
#   <song>.log    stream transcript + measured session summary
#   <song>.jsonl  the per-note description stream (one schema for all tools)
#   <song>.candidates  chart-stand-in hypotheses (piano takes only)
#
# Audio lives in test_results/audio/ (gitignored, supply your own copies);
# outputs land in test_results/validation/. Piano takes get auto-generated
# candidate hypotheses (libkan_harmonic_probe --emit-candidates) so every
# chord attack is described per candidate; monophonic takes need none.
#
# Songs run in PARALLEL (default 6 jobs: the per-song analysis is
# single-threaded; override with JOBS=4 ./run_validation.sh).
#
# Usage: ./run_validation.sh
set -euo pipefail
JOBS="${JOBS:-6}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
BIN="$ROOT/build/apps/kan_demo/kan_demo"
PROBE="$ROOT/build/libkan/libkan_harmonic_probe"
AUD="$ROOT/test_results/audio"
OUT="$ROOT/test_results/validation"

if [ ! -x "$BIN" ]; then
    echo "kan_demo not found; build first:" >&2
    echo "  cmake -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build -j" >&2
    exit 1
fi
mkdir -p "$OUT"

run_one() {
    local wav="$1" profile="$2"
    local base; base="$(basename "$wav")"; base="${base%.*}"
    local cands=()
    if [ "$profile" = "piano" ]; then
        "$PROBE" --emit-candidates "$wav" "$OUT/$base.candidates" 2>/dev/null || true
        [ -s "$OUT/$base.candidates" ] && cands=(--candidates "$OUT/$base.candidates")
    fi
    echo "== $base (instrument: $profile)"
    "$BIN" --input "$wav" --instrument "$profile" "${cands[@]}" \
        --events "$OUT/$base.jsonl" > "$OUT/$base.log" 2>&1
    sed -n '/--- note description stream ---/,/never .wrong./p' "$OUT/$base.log"
    echo "   -> $OUT/$base.log, $OUT/$base.jsonl"
}

shopt -s nullglob
echo "# kan_demo validation pack (live pipeline, offline)"
for f in "$AUD"/*piano*.wav "$AUD"/*piano*.mp3; do run_one "$f" piano; done
for f in "$AUD"/*ocarina*.wav "$AUD"/*xun*.wav "$AUD"/*flute*.wav; do run_one "$f" ocarina; done
echo "done. $OUT/ holds the transcripts + description streams."
