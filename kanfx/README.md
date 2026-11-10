# kanfx/

Real-time audio effects graph for **kan!**: buses, FX nodes, and lock-free
parameter automation. Powers the dynamic consonance engine (backing-track
pitch shifting), mute-on-miss ducking, input preconditioning, and skin-driven
SFX mixing.

**Status:** planned, see [TODO.md](TODO.md).
Design: [docs/architecture/platform-architecture.md](../docs/architecture/platform-architecture.md) §3.

## Concept

```
input bus    HPF 35 Hz → noise gate → (monitor pitch-correct) → meter → ┐
backing bus  stem gain → pitch shift (Δc) → ducker (combo/miss) ────────┤→ master → limiter → out
sfx bus      per-sound gain → reverb send ──────────────────────────────┘
```

- Node contract: `process(in, out, frames)`, RT-safe, zero allocations
- Parameters: atomics written by game threads, read by the audio thread
- Topology immutable while playing; double-buffered swap at safe points
- Detection taps are non-consuming: monitoring FX never alter what the
  judges analyze

## Ground rules

- English only (root `CONTRIBUTING.md`)
- Real-time safety: no allocs, no locks, no I/O on the audio path
- May depend on `libkan/` primitives; `libkan/` must never depend on kanfx
- MIT/BSD/Apache dependencies only (no GPL): pitch shifting via
  Signalsmith Stretch (MIT)
- Extractable into its own repository later
