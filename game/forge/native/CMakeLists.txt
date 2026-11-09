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
# ggml backend: build the Vulkan backend so the runtime's
# `ggml_backend_init_best()` takes the GPU when present (single stack:
# Vulkan/GPU, CPU fallback — chart-transcription-evaluation.md). BSR_FORCE_CPU
# stays as the runtime escape hatch. FORCE wins over stale cache values from
# CPU-only configure runs; the monorepo root build should follow suit
# (tracked as a libkan-lane note).
if(NOT DEFINED KAN_DISABLE_VULKAN)
    find_package(Vulkan QUIET)
    find_program(KAN_GLSLC glslc)
    if(Vulkan_FOUND AND KAN_GLSLC)
        set(GGML_VULKAN ON CACHE BOOL "ggml Vulkan backend (GPU inference)" FORCE)
    else()
        message(STATUS "kan_forge_native: Vulkan tools not found — RoFormer runs on CPU")
    endif()
endif()

add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/../../../libkan libkan_build)

add_library(kan_forge_native SHARED kan_forge_api.cpp)
target_include_directories(kan_forge_native PRIVATE ${miniaudio_SOURCE_DIR})
target_link_libraries(kan_forge_native PRIVATE libkan)
target_compile_options(kan_forge_native PRIVATE -Wall -Wextra)
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
target_compile_options(kan_forge_selftest PRIVATE -Wall -Wextra)
add_test(NAME kan_forge_selftest COMMAND kan_forge_selftest)

# Detection quality against ground truth (validation exports of the compose
# tab): blind transcription, expected mode and decoys — through this bridge,
# exactly as the game calls it. Not a test: a measurement tool
# (game/docs/transcription-quality.md).
add_executable(kan_forge_bench kan_forge_bench.cpp)
target_compile_options(kan_forge_bench PRIVATE -Wall -Wextra)
target_link_libraries(kan_forge_bench PRIVATE kan_forge_native)
