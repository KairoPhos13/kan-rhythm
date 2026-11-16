# Ogg Vorbis encoder for the level files (issue 42): libogg and libvorbis
# (both BSD, Xiph.Org), built from source as two static libraries linked
# into the bridge, so no player machine needs them installed.
#
# Their own CMake is not used: libvorbis 1.3.7 calls find_package(Ogg),
# which a source build in the same tree cannot satisfy without CMake 3.24,
# and both install and build shared libraries we do not want. The source
# lists below follow their lib/CMakeLists.txt of the pinned versions.
#
# Defines the target kan_vorbis (libvorbis + vorbisenc + libogg).

include(FetchContent)

# SOURCE_SUBDIR names a folder that does not exist: FetchContent then only
# downloads the sources and adds nothing to the build.
FetchContent_Declare(
    kan_libogg
    GIT_REPOSITORY https://github.com/xiph/ogg.git
    GIT_TAG        v1.3.6
    GIT_SHALLOW    TRUE
    SOURCE_SUBDIR  kan-sources-only
)
FetchContent_Declare(
    kan_libvorbis
    GIT_REPOSITORY https://github.com/xiph/vorbis.git
    GIT_TAG        v1.3.7
    GIT_SHALLOW    TRUE
    SOURCE_SUBDIR  kan-sources-only
)
FetchContent_MakeAvailable(kan_libogg kan_libvorbis)

# libogg's types header comes from configure; fixed-width types on every
# platform that reaches it (Windows and macOS define their own in os_types.h).
set(_kan_ogg_generated ${CMAKE_CURRENT_BINARY_DIR}/kan_ogg_include)
file(WRITE ${_kan_ogg_generated}/ogg/config_types.h
"#ifndef __CONFIG_TYPES_H__
#define __CONFIG_TYPES_H__
#include <stdint.h>
typedef int16_t ogg_int16_t;
typedef uint16_t ogg_uint16_t;
typedef int32_t ogg_int32_t;
typedef uint32_t ogg_uint32_t;
typedef int64_t ogg_int64_t;
typedef uint64_t ogg_uint64_t;
#endif
")

set(_kan_vorbis_lib ${kan_libvorbis_SOURCE_DIR}/lib)
add_library(kan_vorbis STATIC
    ${kan_libogg_SOURCE_DIR}/src/bitwise.c
    ${kan_libogg_SOURCE_DIR}/src/framing.c
    ${_kan_vorbis_lib}/mdct.c
    ${_kan_vorbis_lib}/smallft.c
    ${_kan_vorbis_lib}/block.c
    ${_kan_vorbis_lib}/envelope.c
    ${_kan_vorbis_lib}/window.c
    ${_kan_vorbis_lib}/lsp.c
    ${_kan_vorbis_lib}/lpc.c
    ${_kan_vorbis_lib}/analysis.c
    ${_kan_vorbis_lib}/synthesis.c
    ${_kan_vorbis_lib}/psy.c
    ${_kan_vorbis_lib}/info.c
    ${_kan_vorbis_lib}/floor1.c
    ${_kan_vorbis_lib}/floor0.c
    ${_kan_vorbis_lib}/res0.c
    ${_kan_vorbis_lib}/mapping0.c
    ${_kan_vorbis_lib}/registry.c
    ${_kan_vorbis_lib}/codebook.c
    ${_kan_vorbis_lib}/sharedbook.c
    ${_kan_vorbis_lib}/lookup.c
    ${_kan_vorbis_lib}/bitrate.c
    ${_kan_vorbis_lib}/vorbisenc.c
)
# Third-party code: SYSTEM includes for its users, its own warnings off
# (the first-party policy never applies to it).
target_include_directories(kan_vorbis SYSTEM PUBLIC
    ${kan_libogg_SOURCE_DIR}/include
    ${kan_libvorbis_SOURCE_DIR}/include
    ${_kan_ogg_generated})
target_include_directories(kan_vorbis PRIVATE ${_kan_vorbis_lib})
if(MSVC)
    target_compile_options(kan_vorbis PRIVATE /w)
    target_compile_definitions(kan_vorbis PRIVATE _CRT_SECURE_NO_WARNINGS _CRT_NONSTDC_NO_DEPRECATE)
else()
    target_compile_options(kan_vorbis PRIVATE -w)
endif()
if(UNIX)
    target_link_libraries(kan_vorbis PRIVATE m)
endif()
