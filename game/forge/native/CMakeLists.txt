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

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

include(FetchContent)

# Automatic download of miniaudio (MIT-0) — same source as the monorepo root.
FetchContent_Declare(
    miniaudio
    GIT_REPOSITORY https://github.com/mackron/miniaudio.git
    GIT_TAG        master
    GIT_SHALLOW    TRUE
)
FetchContent_MakeAvailable(miniaudio)

# libkan is header-only (C++20).
add_library(kan_libkan_headers INTERFACE)
target_include_directories(kan_libkan_headers INTERFACE
    ${CMAKE_CURRENT_SOURCE_DIR}/../../../libkan/include)

add_library(kan_forge_native SHARED kan_forge_api.cpp)
target_include_directories(kan_forge_native PRIVATE ${miniaudio_SOURCE_DIR})
target_link_libraries(kan_forge_native PRIVATE kan_libkan_headers)
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
