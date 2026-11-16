#!/bin/bash
# Play bench (issue 40): plays a level in an isolated, headless kan! with a
# recording as the player, on a few CPU cores, and prints how smoothly it ran.
# Measures only; changes nothing. Never opens a microphone (dev play).
#
#   game/tools/bench-play.sh <take.ogg> [title] [cores] [runs]
#
# take.ogg   the melody alone, as a player plays it (the level's reference
#            mixes in the companion, which a one-note listener hears as chords)
# title      the level, by title (default "Sakura Sakura")
# cores      CPUs to run on, a taskset list (default "0,1": two cores)
# runs       how many plays (default 3)
#
# Needs the local build (make deploy), weston, unshare and taskset.
# The describer figure is wall time per 10 ms block, so on few cores it also
# shows the thread waiting for its turn; its CPU cost alone is measured
# offline (libkan's yin_early_exit_test documents the pitch reading).
set -euo pipefail

take=$(readlink -f "$1"); title=${2:-Sakura Sakura}; cores=${3:-0,1}; runs=${4:-3}
here=$(cd "$(dirname "$0")/.." && pwd)
bin=$here/client/osu.Desktop/bin/Release/net10.0
work=$(mktemp -d "${TMPDIR:-/tmp}/kan-bench.XXXXXX")
rt=/run/user/$(id -u); mnt=$rt/kan-bench-data
trap '[ -n "${KAN_BENCH_KEEP:-}" ] && echo "kept $work" || rm -rf "$work"; rmdir "$mnt" 2>/dev/null || true' EXIT
mkdir -p "$mnt"; cp "$take" "$work/take.ogg"

for run in $(seq 1 "$runs"); do
  rm -rf "$work/data"; mkdir -p "$work/data/kan"
  printf 'VolumeUniversal = 0\nWindowMode = Windowed\n' > "$work/data/kan/framework.ini"
  weston --backend=headless --renderer=gl --socket=kan-bench --width=1280 --height=800 > "$work/weston.log" 2>&1 &
  w=$!; sleep 2
  taskset -c "$cores" unshare -rnm bash -c "
    mount --bind '$work' '$mnt'
    mount -t tmpfs tmpfs /tmp
    unset TMPDIR
    export DOTNET_ROOT=\$HOME/.dotnet SDL_VIDEODRIVER=wayland WAYLAND_DISPLAY=kan-bench XDG_RUNTIME_DIR=$rt XDG_DATA_HOME=$mnt/data
    export KAN_DEV_PLAY='title:$title' KAN_DEV_MODS=NF KAN_DEV_FOCUSED=1 KAN_LIVE_INPUT=$mnt/take.ogg KAN_LIVE_INPUT_REALTIME=1
    timeout ${KAN_BENCH_SECONDS:-100} '$bin/kan' > '$mnt/game.log' 2>&1 || true
  "
  kill $w 2>/dev/null || true; wait $w 2>/dev/null || true
  trace=$(ls -t "$work"/data/kan/logs/kan-play-*.csv 2>/dev/null | head -1 || true)
  [ -n "$trace" ] || { echo "run $run: no play trace"; continue; }
  python3 -I - "$trace" "$run" <<'PY'
import sys, csv
path, run = sys.argv[1], sys.argv[2]
frames, load, fps, verdicts = [], [], [], {}
for row in csv.reader(l for l in open(path) if not l.startswith("#")):
    if len(row) < 4: continue
    if row[2] == "frame": frames.append(max(float(row[3]), float(row[4])))
    elif row[2] == "load":
        load.append(float(row[3]))
        if len(row) > 8: fps.append([float(v) for v in row[5:9]])
    elif row[2] == "verdict": verdicts[row[6]] = verdicts.get(row[6], 0) + 1
over = lambda ms: sum(1 for f in frames if f > ms)
# 12 ms is not reported: the headless display runs at 60 Hz, so every frame is ~16.7 ms.
print(f"run {run}: frames over 25/50 ms: {over(25)}/{over(50)}; worst frame {max(frames, default=0):.0f} ms; "
      f"describer worst {max(load, default=0):.1f} ms per 10 ms, median {sorted(load)[len(load)//2] if load else 0:.1f}; "
      f"verdicts {dict(sorted(verdicts.items()))}")
if fps:
    med = lambda i: sorted(r[i] for r in fps)[len(fps) // 2]
    print(f"run {run}: frames per second, median of the play: update {med(0):.0f} (aim {med(2):.0f}), draw {med(1):.0f} (aim {med(3):.0f})")
PY
done
