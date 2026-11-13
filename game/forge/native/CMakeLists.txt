# kan_forge_native: thin C-ABI bridge from the KAN client (C#) to libkan
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
# kan_forge_api.h: the ABI is the contract.
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

# Shipped-library build policy (libkan/cmake/kan_build_policy.cmake):
# portable CPU baseline (audit C2; ggml's GGML_NATIVE default would tune
# to the CI host and the DLL would die with "illegal instruction" on
# older CPUs) and the first-party warning policy (CONTRIBUTING section 4).
# Every build path of this library gets the baseline; there is no opt-out
# because everything built here can end up in a player's install.
include(${CMAKE_CURRENT_SOURCE_DIR}/../../../libkan/cmake/kan_build_policy.cmake)
kan_cpu_baseline(BASELINE)

# The MSVC runtime links INTO the bridge (like the MinGW test build's
# -static): the DLL then needs nothing beyond Windows system DLLs, so no
# player machine has to carry the VC++ redistributable (audit C9). The C
# ABI passes only caller-owned buffers (kan_forge_api.h), so a private CRT
# instance per module is safe.
if(MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

# Version stamp for bug reports (audit C8): kan_forge_version() reports it,
# driven by KAN_VERSION from the packaging scripts and the workflow (tag
# builds pass the release version; local builds stay 0.0.0).
set(KAN_VERSION "0.0.0" CACHE STRING "version stamp reported by kan_forge_version()")

include(FetchContent)

# Automatic download of miniaudio (MIT-0), same source as the monorepo root.
FetchContent_Declare(
    miniaudio
    GIT_REPOSITORY https://github.com/mackron/miniaudio.git
    GIT_TAG        0.11.25   # pinned: following master broke rebuilds when upstream moved
    GIT_SHALLOW    TRUE
)
FetchContent_MakeAvailable(miniaudio)

# libkan (C++20, header-only core) + its compiled RoFormer separation
# runtime (KAN_ENABLE_ROFORMER, default ON, the self-contained separation
# of editor-pipeline §7b). Consumers get everything via the libkan target.
#
# ggml backend: chosen by KAN_GGML_BACKEND = AUTO | VULKAN | METAL | CPU
# (default AUTO: Metal on Apple (the platform CI builds macOS with),
# Vulkan on Linux/Windows when its tools are found, CPU otherwise; single
# stack: GPU with inherent CPU fallback, BSR_FORCE_CPU stays as the runtime
# escape hatch). The decision is made in libkan/CMakeLists.txt, which owns
# ggml; this file only supplies the default by platform.
# KAN_DISABLE_VULKAN=1 is the pre-KAN_GGML_BACKEND spelling of CPU.
if(DEFINED KAN_DISABLE_VULKAN AND NOT DEFINED KAN_GGML_BACKEND)
    set(KAN_GGML_BACKEND "CPU" CACHE STRING
        "ggml GPU backend for the RoFormer runtime: AUTO, VULKAN, METAL or CPU")
endif()

# Warning policy: -Wall -Wextra (repo rule, CONTRIBUTING section 4) with
# /W4 on MSVC, applied per first-party target by kan_first_party_target()
# (KAN_WERROR=ON turns them into errors for those targets only). MSVC has
# no -Wall/-Wextra flags (/Wall there would enable the SDK's noisy
# informational warnings). The _USE_MATH_DEFINES that libkan headers using
# M_PI need and _CRT_SECURE_NO_WARNINGS stay global here: getenv/fopen are
# standard C used portably here, and MSVC's C4996 getenv_s/fopen_s advice
# has no POSIX equivalent.
if(MSVC)
    add_compile_definitions(_USE_MATH_DEFINES _CRT_SECURE_NO_WARNINGS)
endif()

add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/../../../libkan libkan_build)

add_library(kan_forge_native SHARED kan_forge_api.cpp)
# P/Invoke resolves "kan_forge_native" to kan_forge_native.dll on Windows:
# MinGW's CMake would name the DLL libkan_forge_native.dll: drop the lib
# prefix there (MSVC has none; POSIX keeps libkan_forge_native.{so,dylib}).
if(WIN32)
    set_target_properties(kan_forge_native PROPERTIES PREFIX "")
endif()
target_include_directories(kan_forge_native SYSTEM PRIVATE ${miniaudio_SOURCE_DIR})
target_link_libraries(kan_forge_native PRIVATE libkan)
kan_first_party_target(kan_forge_native)
target_compile_definitions(kan_forge_native PRIVATE KAN_FORGE_VERSION="${KAN_VERSION}")
if(UNIX AND NOT APPLE)
    target_link_libraries(kan_forge_native PRIVATE pthread m dl)
elseif(APPLE)
    # libm is part of libSystem on macOS (libkan already links it; a second
    # -lm only makes ld warn about a duplicate library).
    target_link_libraries(kan_forge_native PRIVATE pthread)
endif()

# Offline self-test (known synthesized input -> expected transcribed notes),
# per repo rule "feature work needs tests".
add_executable(kan_forge_selftest kan_forge_selftest.cpp)
target_include_directories(kan_forge_selftest SYSTEM PRIVATE ${miniaudio_SOURCE_DIR})
target_link_libraries(kan_forge_selftest PRIVATE kan_forge_native)
kan_first_party_target(kan_forge_selftest)
add_test(NAME kan_forge_selftest COMMAND kan_forge_selftest)

# Detection quality against ground truth (validation exports of the compose
# tab): blind transcription, expected mode and decoys, through this bridge,
# exactly as the game calls it. Not a test: a measurement tool
# (game/docs/transcription-quality.md).
add_executable(kan_forge_bench kan_forge_bench.cpp)
kan_first_party_target(kan_forge_bench)
target_link_libraries(kan_forge_bench PRIVATE kan_forge_native)
