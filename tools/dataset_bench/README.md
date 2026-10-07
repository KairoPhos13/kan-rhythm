# kan_dataset_bench

Large-scale evaluation of `libkan` against datasets with millisecond-aligned
ground truth. **MAESTRO** (Google Magenta) is the first target: ~200 h of
competition-level piano captured on Disklavier, so the MIDI labeling is
nearly perfect. The manifest format keeps the tool open for other datasets
and instruments.

The tool **measures, it never judges**: per-note similarity, cents vs ground
truth, duration error, peak-time offset, and unmatched strikes. The balance
of factors it depends on is documented in
[`../../docs/architecture/measurement-invariants.md`](../../docs/architecture/measurement-invariants.md).

## Dataset setup (MAESTRO)

Data is **never committed** here (license + size). Download from the official
page: <https://magenta.withgoogle.com/datasets/maestro> (direct archive:
`https://storage.googleapis.com/magentadata/datasets/maestro/v3.0.0/maestro-v3.0.0.zip`).

```sh
mkdir -p tools/dataset_bench/data
cd tools/dataset_bench/data
# download and unzip maestro-v3.0.0.zip here -> ./maestro-v3.0.0/
```

**License:** CC BY-NC-SA 4.0 — research and non-commercial use only. Keep
the attribution in any published result that uses these runs.

Ground truth comes from the aligned MIDI (`midi_filename` in
`maestro-v3.0.0.csv`); durations are key-hold times (note-off), which is
exactly what the release-bias invariant (I8) is measured against.

## Usage

```sh
# pipeline self-check (no dataset needed; runs in ctest)
./build/tools/dataset_bench/kan_dataset_bench --selftest

# smoke: first 5 files
./build/tools/dataset_bench/kan_dataset_bench \
    --maestro tools/dataset_bench/data/maestro-v3.0.0 --limit 5 --jobs 6

# subset: the official validation split
./build/tools/dataset_bench/kan_dataset_bench \
    --maestro tools/dataset_bench/data/maestro-v3.0.0 --split validation --jobs 12

# full run (see throughput below before you do this)
./build/tools/dataset_bench/kan_dataset_bench \
    --maestro tools/dataset_bench/data/maestro-v3.0.0 --jobs 12

# any other dataset: write a manifest (audio ground_truth), one per line
./build/tools/dataset_bench/kan_dataset_bench --manifest my_set/manifest.txt
```

Outputs in `<out>/` (default `dataset_bench_out/`):

- `notes.csv` — one row per ground-truth note: `file, onset_ms,
  dur_written_ms, midi, found, sim, cents, amp, meas_dur_ms, dur_err_ms,
  t_peak_ms, onset_err_ms`
- `files.csv` — per-file summary
- `summary.txt` — dataset aggregate with the invariant guardrails (I3 pitch
  center, I8 release-bias estimate)

`--dump-score file.mid` prints a converted score text for inspection.

## Throughput and hardware

Measured core throughput is ~0.17× real time on one core (AVX2 Gram), so
the full 200 h is ~34 CPU-hours: about **3 h wall on 12 `--jobs`**, or
~1.5 h with `--fit-step 150` for triage runs. The `summary.txt` throughput
line reports the achieved real-time factor — use `--limit`/`--offset` to
page through subsets while refining.

**GPU (decided):** the analysis core stays CPU-only (invariants I9/I11 —
the benchmark must validate exactly the code the game runs). A GPU batch
backend would only be acceptable as an *optional* accelerator behind an
equivalence test against CPU on a reference subset (|Δcents| ≤ 1 c,
|Δsim| ≤ 0.01), never as the source of published measurements before that
passes. See `measurement-invariants.md` T3. Offline runs are exempt from
the live ≤2-analysis-thread budget: `--jobs` may use the machine's cores.

## Adding datasets and instruments

- **Another dataset** (e.g. a flute corpus): produce ground truth as `.mid`
  or score text (`onset_ms dur_ms note`, see
  `libkan/include/libkan/score_verify.hpp`), then either a manifest or a
  discovery function like `discover_maestro`. No changes to `libkan`.
- **Another instrument**: set the right `kan::InstrumentProfile` in the
  pack; `libkan` gains no code (invariant I10). The runner's
  `bench_entry` currently uses the keyboard profile — a per-entry profile
  field is the planned extension point when the second instrument lands.

## Ground rules

- English only; one executable per tool; tools depend on `libkan/`, never
  the reverse; extractable into its own repository later.
- Datasets and run outputs are gitignored (see root `.gitignore`).
