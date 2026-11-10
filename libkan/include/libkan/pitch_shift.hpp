#pragma once
// Backing-track pitch-shift primitive (SPECIFICATION.md §2 dynamic
// consonance module): shift the song by the instrument's Δc so instrument
// and song lock together while scoring stays strict. Built on Signalsmith
// Stretch (MIT): time-stretching keeps the BPM, transposition moves the
// pitch.
//
// Ownership: this is the libkan primitive (platform-architecture.md §2
// lists "pitch-shift primitives" in the libkan layer). The bus wiring
// (the backing chain, ducker interaction, block scheduling) belongs to
// kanfx (platform-architecture.md §3), which links this through
// `libkan_stretch`.
//
// Real-time safety: set_detune_cents() and process() are allocation-free
// after construction; construct/configure on the setup path.
#include <signalsmith-stretch/signalsmith-stretch.h>

#include <cmath>
#include <cstddef>

namespace kan {

/// Mono backing-track pitch shifter with BPM preservation.
class PitchShifter {
public:
    explicit PitchShifter(float sample_rate = 48000.0f) : sample_rate_(sample_rate) {
        stretch_.presetDefault(1, sample_rate);
    }

    /// Shift playback pitch by `cents` (Δc = 1200·log2(f_measured/f_standard),
    /// applied as +Δc to the song). 0 = pass-through pitch.
    void set_detune_cents(float cents) noexcept {
        stretch_.setTransposeFactor(std::pow(2.0f, cents / 1200.0f));
    }

    /// Process one mono block. `in` and `out` must be distinct buffers.
    /// Equal counts = consonance pitch shift at natural speed; different
    /// counts = time-stretch (BPM change) at fixed pitch.
    void process(const float* in, size_t in_count, float* out, size_t out_count) noexcept {
        const float* in_ch[1] = {in};
        float* out_ch[1] = {out};
        stretch_.process(in_ch, int(in_count), out_ch, int(out_count));
    }

    /// Clear internal state (new song / seek).
    void reset() noexcept { stretch_.reset(); }

    int input_latency() const noexcept { return stretch_.inputLatency(); }
    int output_latency() const noexcept { return stretch_.outputLatency(); }
    float sample_rate() const noexcept { return sample_rate_; }

private:
    float sample_rate_;
    signalsmith::stretch::SignalsmithStretch<float> stretch_;
};

} // namespace kan
