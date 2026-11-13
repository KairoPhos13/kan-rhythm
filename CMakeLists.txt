cmake_minimum_required(VERSION 3.20)
project(kan-rhythm VERSION 0.1.0 LANGUAGES CXX C)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

option(KAN_BUILD_TESTS "Build component tests" ON)
# The apps/ and tools/ executables are developer tools; the game release
# builds (and CI) turn them off to stay bounded and portable.
option(KAN_BUILD_APPS "Build the apps/ executables" ON)
option(KAN_BUILD_TOOLS "Build the tools/ executables" ON)

if(KAN_BUILD_TESTS)
    enable_testing()
endif()

# CPU tuning is OPT-IN (November 2026 audit C2: -march=native in a binary
# that may leave the build machine raises "illegal instruction" on older
# CPUs). The default build is the portable baseline of
# libkan/cmake/kan_build_policy.cmake; -DKAN_MARCH_NATIVE=ON tunes local
# developer builds to this machine and must never be used for shipped
# artifacts (game/packaging/ and CI always build the baseline).
option(KAN_MARCH_NATIVE
    "Tune code generation for the build machine's CPU (-march=native / /arch:AVX2); never for shipped artifacts" OFF)
include(${CMAKE_CURRENT_SOURCE_DIR}/libkan/cmake/kan_build_policy.cmake)
if(KAN_MARCH_NATIVE)
    kan_cpu_baseline(NATIVE)
else()
    kan_cpu_baseline(BASELINE)
endif()

# Optimization plus the first-party warning level (MSVC had none before;
# /W4 now reaches every kan!-owned target through kan_first_party_target).
if(MSVC)
    add_compile_options(/O2)
    add_compile_definitions(_USE_MATH_DEFINES) # libkan headers use M_PI
    # getenv/fopen are standard C and used portably here; MSVC's C4996
    # "unsafe, use getenv_s/fopen_s" advice has no POSIX equivalent.
    add_compile_definitions(_CRT_SECURE_NO_WARNINGS)
else()
    add_compile_options(-O3 -Wall -Wextra)
endif()

# Static everywhere (repo style). Forced before any FetchContent: ggml's
# option(BUILD_SHARED_LIBS) caches ON otherwise and conflicts with
# KISSFFT_STATIC on reconfigure.
set(BUILD_SHARED_LIBS OFF CACHE BOOL "static libraries" FORCE)

include(FetchContent)

# Automatic download of miniaudio (MIT-0)
FetchContent_Declare(
    miniaudio
    GIT_REPOSITORY https://github.com/mackron/miniaudio.git
    GIT_TAG        0.11.25   # pinned: following master broke rebuilds when upstream moved
    GIT_SHALLOW    TRUE
)
FetchContent_MakeAvailable(miniaudio)

# Cycfi Q (MIT): BACF bitstream-autocorrelation monophonic pitch detector
# used by libkan's score-informed alternative detector (libkan_bacf).
option(KAN_ENABLE_BACF "Fetch Cycfi Q (MIT) for the BACF pitch detector" ON)
if(KAN_ENABLE_BACF)
    set(Q_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
    set(Q_BUILD_TEST OFF CACHE BOOL "" FORCE)
    set(Q_BUILD_IO OFF CACHE BOOL "" FORCE)
    set(Q_BUILD_PLUG OFF CACHE BOOL "" FORCE)
    FetchContent_Declare(
        cycfi_q
        GIT_REPOSITORY https://github.com/cycfi/q.git
        GIT_TAG        v1.5.0
        GIT_SHALLOW    TRUE
    )
    FetchContent_MakeAvailable(cycfi_q)

    # Treat Cycfi Q + infra headers as SYSTEM includes: third-party warnings
    # must not pollute the -Wall -Wextra clean-build rule (CONTRIBUTING §4).
    foreach(_kan_q_target libq infra)
        if(TARGET ${_kan_q_target})
            get_target_property(_kan_q_inc ${_kan_q_target} INTERFACE_INCLUDE_DIRECTORIES)
            if(_kan_q_inc)
                set_target_properties(${_kan_q_target} PROPERTIES
                    INTERFACE_SYSTEM_INCLUDE_DIRECTORIES "${_kan_q_inc}")
            endif()
        endif()
    endforeach()
endif()

# Signalsmith Stretch (MIT): backing-track pitch shift with BPM preserved
# (SPECIFICATION §2 dynamic consonance module). Primitive lives in libkan;
# the bus wiring belongs to kanfx's backing chain.
option(KAN_ENABLE_STRETCH "Fetch Signalsmith Stretch (MIT) for pitch shift" ON)
if(KAN_ENABLE_STRETCH)
    FetchContent_Declare(
        signalsmith_stretch
        GIT_REPOSITORY https://github.com/signalsmith-audio/signalsmith-stretch.git
        GIT_TAG        88061fd4eda56a2353c5d65c74aa3f4ccf76f9da  # pinned (1.4.0+1): a moving branch breaks rebuilds
        GIT_SHALLOW    FALSE
    )
    FetchContent_MakeAvailable(signalsmith_stretch)
    if(TARGET signalsmith-stretch)
        get_target_property(_kan_stretch_inc signalsmith-stretch INTERFACE_INCLUDE_DIRECTORIES)
        if(_kan_stretch_inc)
            set_target_properties(signalsmith-stretch PROPERTIES
                INTERFACE_SYSTEM_INCLUDE_DIRECTORIES "${_kan_stretch_inc}")
        endif()
    endif()
endif()
# KissFFT (BSD-3): real FFT for the 12-bin chroma projection used by the
# score-informed chord validator (libkan_fft). Static, float, no tools/tests.
option(KAN_ENABLE_FFT "Fetch KissFFT (BSD-3) for chroma chord validation" ON)
if(KAN_ENABLE_FFT)
    set(KISSFFT_DATATYPE float CACHE STRING "" FORCE)
    set(KISSFFT_STATIC ON CACHE BOOL "" FORCE)
    set(KISSFFT_TOOLS OFF CACHE BOOL "" FORCE)
    set(KISSFFT_TEST OFF CACHE BOOL "" FORCE)
    set(KISSFFT_PKGCONFIG OFF CACHE BOOL "" FORCE)
    FetchContent_Declare(
        kissfft
        GIT_REPOSITORY https://github.com/mborgerding/kissfft.git
        GIT_TAG        131.2.0
        GIT_SHALLOW    TRUE
    )
    FetchContent_MakeAvailable(kissfft)
    if(TARGET kissfft)
        get_target_property(_kan_fft_inc kissfft INTERFACE_INCLUDE_DIRECTORIES)
        if(_kan_fft_inc)
            set_target_properties(kissfft PROPERTIES
                INTERFACE_SYSTEM_INCLUDE_DIRECTORIES "${_kan_fft_inc}")
        endif()
    endif()
endif()

# Components (each top-level folder is designed to be splittable into its own repo)
add_subdirectory(libkan)
if(KAN_BUILD_APPS)
    add_subdirectory(apps)
endif()
if(KAN_BUILD_TOOLS)
    add_subdirectory(tools/dataset_bench)
    add_subdirectory(tools/chart_scribe)
    add_subdirectory(tools/stem_split)
endif()
