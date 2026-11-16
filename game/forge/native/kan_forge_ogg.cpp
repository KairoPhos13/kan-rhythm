// Ogg Vorbis encoding of the level files (issue 42): the decoded audio
// goes through libvorbis' VBR encoder and into Ogg pages. A separate
// translation unit from kan_forge_api.cpp, which compiles miniaudio and
// stb_vorbis: stb_vorbis' internals and libvorbis' headers stay apart.
//
// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.

#include "kan_forge_ogg.hpp"

#include "miniaudio.h"

#include <vorbis/vorbisenc.h>

#include <cstdio>
#include <vector>

namespace kan_forge {
namespace {

// Everything libvorbis and libogg allocate, released in any case.
struct VorbisEncoder {
    vorbis_info info{};
    vorbis_comment comment{};
    vorbis_dsp_state dsp{};
    vorbis_block block{};
    ogg_stream_state stream{};
    bool dsp_ready = false;
    bool block_ready = false;
    bool stream_ready = false;

    VorbisEncoder() {
        vorbis_info_init(&info);
        vorbis_comment_init(&comment);
    }

    ~VorbisEncoder() {
        if (stream_ready) ogg_stream_clear(&stream);
        if (block_ready) vorbis_block_clear(&block);
        if (dsp_ready) vorbis_dsp_clear(&dsp);
        vorbis_comment_clear(&comment);
        vorbis_info_clear(&info);
    }

    VorbisEncoder(const VorbisEncoder&) = delete;
    VorbisEncoder& operator=(const VorbisEncoder&) = delete;
};

struct File {
    std::FILE* f = nullptr;
    ~File() {
        if (f) std::fclose(f);
    }
};

bool write_page(std::FILE* f, const ogg_page& page) {
    return std::fwrite(page.header, 1, size_t(page.header_len), f) == size_t(page.header_len)
        && std::fwrite(page.body, 1, size_t(page.body_len), f) == size_t(page.body_len);
}

// Every finished block into packets, every full page to the file.
bool drain(VorbisEncoder& enc, std::FILE* f) {
    while (vorbis_analysis_blockout(&enc.dsp, &enc.block) == 1) {
        vorbis_analysis(&enc.block, nullptr);
        vorbis_bitrate_addblock(&enc.block);

        ogg_packet packet;
        while (vorbis_bitrate_flushpacket(&enc.dsp, &packet)) {
            ogg_stream_packetin(&enc.stream, &packet);

            ogg_page page;
            while (ogg_stream_pageout(&enc.stream, &page))
                if (!write_page(f, page)) return false;
        }
    }
    return true;
}

kan_forge_status encode(const char* audio_path, const char* out_ogg, float quality, std::string& error) {
    if (audio_path == nullptr || out_ogg == nullptr) {
        error = "encode: missing audio path or output path";
        return KAN_FORGE_ERR_ARGS;
    }
    if (!(quality >= -0.1f && quality <= 1.0f)) {
        error = "encode: quality must be between -0.1 and 1";
        return KAN_FORGE_ERR_ARGS;
    }

    ma_decoder_config dcfg = ma_decoder_config_init(ma_format_f32, 0, 0);
    ma_decoder dec;
    if (ma_decoder_init_file(audio_path, &dcfg, &dec) != MA_SUCCESS) {
        error = "encode: cannot decode audio (missing file or unsupported codec)";
        return KAN_FORGE_ERR_DECODE;
    }
    struct DecoderGuard {
        ma_decoder* d;
        ~DecoderGuard() { ma_decoder_uninit(d); }
    } dec_guard{&dec};

    const ma_uint32 channels = dec.outputChannels;
    const ma_uint32 rate = dec.outputSampleRate;
    if (channels == 0 || channels > 8 || rate == 0) {
        error = "encode: unsupported channel count or sample rate";
        return KAN_FORGE_ERR_DECODE;
    }

    VorbisEncoder enc;
    if (vorbis_encode_init_vbr(&enc.info, long(channels), long(rate), quality) != 0) {
        error = "encode: the encoder does not take this channel count or sample rate";
        return KAN_FORGE_ERR_DECODE;
    }
    vorbis_comment_add_tag(&enc.comment, "ENCODER", "kan!");
    if (vorbis_analysis_init(&enc.dsp, &enc.info) != 0) {
        error = "encode: cannot start the encoder";
        return KAN_FORGE_ERR_INTERNAL;
    }
    enc.dsp_ready = true;
    vorbis_block_init(&enc.dsp, &enc.block);
    enc.block_ready = true;
    // A fixed stream serial: the same audio always gives the same bytes,
    // so a level's file hash only changes when its sound does.
    ogg_stream_init(&enc.stream, 0x6b616e21);
    enc.stream_ready = true;

    File out;
    out.f = std::fopen(out_ogg, "wb");
    if (out.f == nullptr) {
        error = "encode: cannot write the output file";
        return KAN_FORGE_ERR_ARGS;
    }

    auto fail = [&](const char* message, kan_forge_status status) {
        std::fclose(out.f);
        out.f = nullptr;
        std::remove(out_ogg);
        error = message;
        return status;
    };

    // The three header packets, alone on their first pages.
    ogg_packet header, header_comment, header_code;
    vorbis_analysis_headerout(&enc.dsp, &enc.comment, &header, &header_comment, &header_code);
    ogg_stream_packetin(&enc.stream, &header);
    ogg_stream_packetin(&enc.stream, &header_comment);
    ogg_stream_packetin(&enc.stream, &header_code);
    ogg_page page;
    while (ogg_stream_flush(&enc.stream, &page))
        if (!write_page(out.f, page)) return fail("encode: cannot write the output file", KAN_FORGE_ERR_ARGS);

    constexpr ma_uint64 kChunk = 4096;
    std::vector<float> chunk(size_t(kChunk) * channels);
    ma_uint64 total = 0;
    for (;;) {
        ma_uint64 got = 0;
        if (ma_decoder_read_pcm_frames(&dec, chunk.data(), kChunk, &got) != MA_SUCCESS || got == 0) break;

        float** planes = vorbis_analysis_buffer(&enc.dsp, int(got));
        for (ma_uint64 i = 0; i < got; ++i)
            for (ma_uint32 c = 0; c < channels; ++c)
                planes[c][i] = chunk[size_t(i) * channels + c];
        vorbis_analysis_wrote(&enc.dsp, int(got));
        total += got;

        if (!drain(enc, out.f)) return fail("encode: cannot write the output file", KAN_FORGE_ERR_ARGS);
    }

    if (total == 0) return fail("encode: the file has no audio data", KAN_FORGE_ERR_DECODE);

    // End of stream: the last packets and the final page.
    vorbis_analysis_wrote(&enc.dsp, 0);
    if (!drain(enc, out.f)) return fail("encode: cannot write the output file", KAN_FORGE_ERR_ARGS);
    while (ogg_stream_flush(&enc.stream, &page))
        if (!write_page(out.f, page)) return fail("encode: cannot write the output file", KAN_FORGE_ERR_ARGS);

    if (std::fclose(out.f) != 0) {
        out.f = nullptr;
        std::remove(out_ogg);
        error = "encode: cannot write the output file";
        return KAN_FORGE_ERR_ARGS;
    }
    out.f = nullptr;
    return KAN_FORGE_OK;
}

} // namespace

kan_forge_status encode_ogg(const char* audio_path, const char* out_ogg, float quality, std::string& error) {
    return encode(audio_path, out_ogg, quality, error);
}

} // namespace kan_forge
