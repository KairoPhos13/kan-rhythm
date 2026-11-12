# Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
# See the LICENCE file in the kan-rhythm repository root for full licence text.
"""Programme loudness (ITU-R BS.1770-4), the game's own measurement.

Every level's audio is levelled to the game's reference so moving from one
level to the next never needs the volume touched: kan! renders at
``LevelLoudness.REFERENCE_LUFS`` (-20 LUFS) with the sample peak held under
``LevelLoudness.CEILING_DBFS`` (-1 dBFS). The algorithm here mirrors
``libkan/include/libkan/loudness.hpp`` note for note (K-weighting shelf and
high-pass derived from the BS.1770 analogue prototypes at 48 kHz, 400 ms
blocks overlapping by 75 %, -70 LUFS absolute gate, then -10 LU relative
gate), so the pack measures the same as the game that plays it.

Offline only: nothing here belongs on an audio callback path.
"""

from __future__ import annotations

import math

# numpy is imported where it is used: `pack` (zipping the committed level
# files) must run with nothing but the standard library.
REFERENCE_LUFS = -20.0
CEILING_DBFS = -1.0
SAMPLE_RATE = 44100


def _biquad(b0, b1, b2, a1, a2):
    import numpy as np

    return np.array([b0, b1, b2, a1, a2], dtype=float)


def _shelf(sample_rate: float) -> np.ndarray:
    f0, gain_db, q = 1681.974450955533, 3.999843853973347, 0.7071752369554196
    k = math.tan(math.pi * f0 / sample_rate)
    vh = 10 ** (gain_db / 20)
    vb = vh ** 0.4996667741545416
    a0 = 1 + k / q + k * k
    return _biquad((vh + vb * k / q + k * k) / a0,
                   2 * (k * k - vh) / a0,
                   (vh - vb * k / q + k * k) / a0,
                   2 * (k * k - 1) / a0,
                   (1 - k / q + k * k) / a0)


def _high_pass(sample_rate: float) -> np.ndarray:
    f0, q = 38.13547087602444, 0.5003270373238773
    k = math.tan(math.pi * f0 / sample_rate)
    a0 = 1 + k / q + k * k
    return _biquad(1 / a0, -2 / a0, 1 / a0, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0)


def _run(coeffs: np.ndarray, signal_: np.ndarray) -> np.ndarray:
    """Direct form II transposed, as the C++ biquad (scipy when present)."""
    import numpy as np

    b0, b1, b2, a1, a2 = coeffs
    try:
        from scipy.signal import lfilter
        return lfilter([b0, b1, b2], [1.0, a1, a2], signal_)
    except ImportError:
        pass

    out = np.empty_like(signal_)
    z1 = z2 = 0.0
    for i, x in enumerate(signal_):
        y = b0 * x + z1
        z1 = b1 * x - a1 * y + z2
        z2 = b2 * x - a2 * y
        out[i] = y
    return out


def integrated_loudness(samples: np.ndarray, sample_rate: float = SAMPLE_RATE) -> float:
    """Integrated loudness (LUFS) of mono samples; -inf for silence."""
    import numpy as np

    samples = np.asarray(samples, dtype=float)
    hop = int(sample_rate * 0.1)
    if hop == 0 or samples.size < 4 * hop:
        return -math.inf

    weighted = _run(_high_pass(sample_rate), _run(_shelf(sample_rate), samples))
    steps = (weighted[:len(weighted) // hop * hop] ** 2).reshape(-1, hop).sum(axis=1)
    if steps.size < 4:
        return -math.inf

    blocks = (steps[:-3] + steps[1:-2] + steps[2:-1] + steps[3:]) / (4 * hop)

    def lufs(mean_square: float) -> float:
        return -0.691 + 10 * math.log10(mean_square)

    def gated(gate: float) -> float:
        kept = [z for z in blocks if z > 0 and lufs(z) > gate]
        return sum(kept) / len(kept) if kept else 0.0

    absolute = gated(-70.0)
    if absolute <= 0:
        return -math.inf

    relative = gated(lufs(absolute) - 10.0)
    return lufs(relative) if relative > 0 else -math.inf


def sample_peak(samples: np.ndarray) -> float:
    import numpy as np

    samples = np.asarray(samples, dtype=float)
    return float(np.max(np.abs(samples))) if samples.size else 0.0


def loudness_gain(lufs: float, peak: float, target_lufs: float = REFERENCE_LUFS,
                  ceiling: float = None) -> float:
    """Gain that brings a signal to the target, holding its peak at the ceiling."""
    if ceiling is None:
        ceiling = 10 ** (CEILING_DBFS / 20)
    if not math.isfinite(lufs) or peak <= 0:
        return 1.0
    gain = 10 ** ((target_lufs - lufs) / 20)
    return min(gain, ceiling / peak)


def level(samples: np.ndarray, sample_rate: float = SAMPLE_RATE,
          target_lufs: float = REFERENCE_LUFS) -> np.ndarray:
    """Level samples to the reference loudness, peak held under the ceiling.

    A quiet mix with sharp peaks comes up only as far as its peaks allow,
    exactly like the editor's render (LevelLoudness, editor-pipeline §6a).
    """
    gain = loudness_gain(integrated_loudness(samples, sample_rate),
                         sample_peak(samples), target_lufs)
    return samples * gain
