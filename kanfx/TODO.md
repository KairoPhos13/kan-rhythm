# kanfx/ — TODO

Design reference: [docs/architecture/platform-architecture.md](../docs/architecture/platform-architecture.md) §3.

## Foundations (target: Milestone 1–2 — pitchshift/ducker are required by M2 gates)

- [ ] ADR: graph model (fixed buses v0 vs free graph later)
- [ ] `KanFxNode` interface: `process()`, atomic parameter block, latency
      reporting, reset/seek for backing tracks
- [ ] Bus mixer with non-consuming taps for libkan detection
- [ ] Double-buffered topology swap (RT-safe reconfiguration)
- [ ] Built-in nodes v0: highpass, noisegate, gain, meter, limiter
- [ ] `pitchshift` node wrapping Signalsmith Stretch (±50 cents, BPM kept) —
      the dynamic consonance module (SPECIFICATION.md §2)
- [ ] `ducker` node: combo attenuation (−12 dB target stem) + mute-on-miss
- [ ] Offline render mode (process a WAV through the graph) for tests
- [ ] CTest suite: golden-file tests for gain/HPF; pitchshift round-trip
      accuracy in cents; zero-allocation assertion strategy (custom allocator
      or heap-profiler harness in tests)

## Integration (Milestone 2)

- [ ] Wire into game client: input/backing/sfx/master buses
- [ ] Skin-driven SFX playback (per-sound gain → reverb send)
- [ ] CPU budget instrumentation per node (dev overlay)

## Later

- [ ] Reverb send/return bus
- [ ] Community FX packs = parameter presets over built-in nodes (JSON)
- [ ] Native custom-DSP node ABI (blocked on its own ADR + sandboxing design)
