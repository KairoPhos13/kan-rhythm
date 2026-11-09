# kan_forge_native — thin C-ABI bridge from the KAN client (C#) to libkan
# (audio fetch, source separation, offline transcription).
#
# Standalone build on purpose: this component is part of game/ (the client
# lane) and must not require changes to the monorepo root build. Build:
#
#   cmake -S game/forge/native -B game/forge/native/build -DCMAKE_BUILD_TYPE=Release
#   cmake --build game/forge/native/build -j
#   ctest --test-dir game/forge/native/build --output-on-failure
#
# The C# side (game/forge, Kan.Forge.Native) loads the produced library
# (libkan_forge_native.so / .dll / .dylib). Keep it in sync with
# kan_forge_api.h — the ABI is the contract.
cmake_minimum_required(VERSION 3.20)
project(kan_forge_native LANGUAGES C CXX)

enable_testing()

# Defaults (game/Makefile drives these; keep builds reproducible without flags).
if(NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release)
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

include(FetchContent)

# Automatic download of miniaudio (MIT-0) — same source as the monorepo root.
FetchContent_Declare(
    miniaudio
    GIT_REPOSITORY https://github.com/mackron/miniaudio.git
    GIT_TAG        0.11.25   # pinned: following master broke rebuilds when upstream moved
    GIT_SHALLOW    TRUE
)
FetchContent_MakeAvailable(miniaudio)

# libkan (C++20, header-only core) + its compiled RoFormer separation
# runtime (KAN_ENABLE_ROFORMER, default ON — the self-contained separation
# of editor-pipeline §7b). Consumers get everything via the libkan target.
#
# ggml backend: chosen by KAN_GGML_BACKEND = AUTO | VULKAN | METAL | CPU
# (default AUTO: Metal on Apple — the platform CI builds macOS with —
# Vulkan on Linux/Windows when its tools are found, CPU otherwise; single
# stack: GPU with inherent CPU fallback, BSR_FORCE_CPU stays as the runtime
# escape hatch). The decision is made in libkan/CMakeLists.txt, which owns
# ggml; this file only supplies the default by platform.
# KAN_DISABLE_VULKAN=1 is the pre-KAN_GGML_BACKEND spelling of CPU.
if(DEFINED KAN_DISABLE_VULKAN AND NOT DEFINED KAN_GGML_BACKEND)
    set(KAN_GGML_BACKEND "CPU" CACHE STRING
        "ggml GPU backend for the RoFormer runtime: AUTO, VULKAN, METAL or CPU")
endif()

# Warning policy: -Wall -Wextra (repo rule, CONTRIBUTING §4). MSVC has no
# such flags (/Wall there would enable the SDK's noisy informational
# warnings), so it gets /W4 plus the _USE_MATH_DEFINES that libkan headers
# using M_PI need.
if(MSVC)
    set(KAN_WARNING_FLAGS /W4)
    add_compile_definitions(_USE_MATH_DEFINES)
else()
    set(KAN_WARNING_FLAGS -Wall -Wextra)
endif()

add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/../../../libkan libkan_build)

add_library(kan_forge_native SHARED kan_forge_api.cpp)
# P/Invoke resolves "kan_forge_native" to kan_forge_native.dll on Windows:
# MinGW's CMake would name the DLL libkan_forge_native.dll — drop the lib
# prefix there (MSVC has none; POSIX keeps libkan_forge_native.{so,dylib}).
if(WIN32)
    set_target_properties(kan_forge_native PROPERTIES PREFIX "")
endif()
target_include_directories(kan_forge_native PRIVATE ${miniaudio_SOURCE_DIR})
target_link_libraries(kan_forge_native PRIVATE libkan)
target_compile_options(kan_forge_native PRIVATE ${KAN_WARNING_FLAGS})
if(UNIX AND NOT APPLE)
    target_link_libraries(kan_forge_native PRIVATE pthread m dl)
elseif(APPLE)
    target_link_libraries(kan_forge_native PRIVATE pthread m)
endif()

# Offline self-test (known synthesized input -> expected transcribed notes),
# per repo rule "feature work needs tests".
add_executable(kan_forge_selftest kan_forge_selftest.cpp)
target_include_directories(kan_forge_selftest PRIVATE ${miniaudio_SOURCE_DIR})
target_link_libraries(kan_forge_selftest PRIVATE kan_forge_native)
target_compile_options(kan_forge_selftest PRIVATE ${KAN_WARNING_FLAGS})
add_test(NAME kan_forge_selftest COMMAND kan_forge_selftest)

# Detection quality against ground truth (validation exports of the compose
# tab): blind transcription, expected mode and decoys — through this bridge,
# exactly as the game calls it. Not a test: a measurement tool
# (game/docs/transcription-quality.md).
add_executable(kan_forge_bench kan_forge_bench.cpp)
target_compile_options(kan_forge_bench PRIVATE ${KAN_WARNING_FLAGS})
target_link_libraries(kan_forge_bench PRIVATE kan_forge_native)
