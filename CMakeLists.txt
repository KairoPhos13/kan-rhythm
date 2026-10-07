cmake_minimum_required(VERSION 3.20)
project(kan-rhythm VERSION 0.1.0 LANGUAGES CXX C)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

option(KAN_BUILD_TESTS "Build component tests" ON)

if(KAN_BUILD_TESTS)
    enable_testing()
endif()

# Maximum optimization for real-time DSP
if(MSVC)
    add_compile_options(/O2 /arch:AVX2)
else()
    add_compile_options(-O3 -march=native -Wall -Wextra)
endif()

include(FetchContent)

# Automatic download of miniaudio (MIT-0)
FetchContent_Declare(
    miniaudio
    GIT_REPOSITORY https://github.com/mackron/miniaudio.git
    GIT_TAG        master
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
        GIT_TAG        main
        GIT_SHALLOW    TRUE
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
add_subdirectory(apps)
add_subdirectory(tools/dataset_bench)
