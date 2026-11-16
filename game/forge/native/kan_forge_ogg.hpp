// Ogg Vorbis encoding of the level files (issue 42). Internal to the
// bridge: kan_forge_api.cpp exports it as kan_forge_encode_ogg.
//
// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
#pragma once

#include "kan_forge_api.h"

#include <string>

namespace kan_forge {

// Decode audio_path (any format the bridge reads) and write it to out_ogg
// as Ogg Vorbis at the source's own rate and channels. quality is
// libvorbis' VBR quality, -0.1 to 1.0. On failure the partial file is
// removed and error says why.
kan_forge_status encode_ogg(const char* audio_path, const char* out_ogg, float quality, std::string& error);

} // namespace kan_forge
