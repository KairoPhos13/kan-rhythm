# ADR-0003: MIT license and no-GPL dependency policy

- **Status:** Accepted (2026-10-05, confirmed by project owner)
- **Deciders:** project owner (KairoPhos)

## Context

kan! is an open-source, community-driven rhythm game inspired by the osu!
model: the client is free and open, the project is sustained by donations
(`kan!supporter`), and the real value lives in the community map ecosystem
and official leaderboards. Platforms are PC only (Linux, Windows, macOS);
console ports are explicitly out of scope.

We need a license that:

1. Attracts C++/DSP contributors from day one with zero legal friction
2. Allows adopting the best permissively licensed open-source building
   blocks (miniaudio, Cycfi Q, KissFFT, Signalsmith Stretch, ONNX Runtime)
3. Keeps future options open (e.g. a closed backend for rankings/donations,
   or any distribution channel)

The alternative considered was GPLv3 (copyleft shield against closed
commercial clones), which osu! itself does not use.

## Decision

- The kan-rhythm repository is licensed under the **MIT License**
  (see root `LICENSE`), matching the osu!/osu!lazer philosophy.
- **No GPL/AGPL dependencies** are allowed in the engine core or any
  component of this monorepo. Acceptable licenses: MIT, MIT-0, BSD-2/3,
  Apache-2.0, Zlib, ISC, public domain (CC0/Unlicense).
- LGPL dependencies require explicit discussion in a PR and a new ADR
  (dynamic-linking only); none are currently used.
- The license-audited stack of record lives in
  [SPECIFICATION.md §1](../SPECIFICATION.md).

## Consequences

- Anyone may fork and even commercially reuse the code, as long as the
  copyright notice is retained. We accept this: community protection comes
  from the official servers, brand, and map ecosystem — not from the code
  license (the same trade-off osu! made).
- Maximum freedom choosing dependencies within the permissive set; GPL
  tools (Aubio, FFTW, RubberBand, Essentia, JACK) are excluded — see
  SPECIFICATION.md §1 for the adopted alternatives.
- A future closed-source backend (rankings, donations, map distribution)
  is compatible with this choice without dual licensing or CLAs.
- If copyleft protection is ever desired, relicensing requires the consent
  of all contributors — another reason this decision is recorded now, while
  the codebase and contributor set are small.
