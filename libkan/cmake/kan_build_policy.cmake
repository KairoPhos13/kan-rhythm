# Build policy shared by the monorepo root build and the shipped native
# bridge (game/forge/native): one place for the CPU baseline and the
# first-party warning policy (CONTRIBUTING section 4). Include this file
# from the top of a CMakeLists.txt, before any target is defined, then call
# kan_cpu_baseline() once and kan_first_party_target() per kan!-owned
# target.
#
# CPU baseline (November 2026 audit, C2). ggml's CMake tunes to the build
# host's CPU unless GGML_NATIVE is OFF (its default is ON outside
# cross-builds), and with GGML_NATIVE=OFF it still turns on the AVX2-era
# x86 kernels by default. A shipped library tuned to the build machine
# dies with "illegal instruction" on older player CPUs, so kan! builds for
# a fixed, documented baseline instead:
#
#   x86_64   x86-64-v2 (SSE4.2, POPCNT, CMPXCHG16B; Nehalem 2008+ and
#            later). GCC/Clang/MinGW get -march=x86-64-v2 and ggml's x86
#            kernels are capped at SSE4.2. MSVC has no -march levels, so
#            it builds at the x64 floor (SSE2): even more portable, and
#            ggml's SSE4.2 kernels are still compiled where they apply.
#   arm64    armv8-a: the arm64 floor. Apple silicon needs no flag (arm64
#            is the platform's baseline); other aarch64 targets get
#            -march=armv8-a. ggml never sees -mcpu=native here.
#   other    untouched (the compiler's own default).
#
# NATIVE mode (opt-in: -DKAN_MARCH_NATIVE=ON at the root) restores
# -march=native / /arch:AVX2 for local developer builds. Never use it for
# anything that ships (game/packaging/ and .github/workflows/build.yml
# always build BASELINE).

# kan_cpu_baseline(BASELINE|NATIVE): sets ggml's cache options and adds the
# mode's code-generation flags to the directory that calls it (they inherit
# into every subdirectory added afterwards).
function(kan_cpu_baseline mode)
    if(mode STREQUAL "NATIVE")
        if(MSVC)
            add_compile_options(/arch:AVX2)
        else()
            add_compile_options(-march=native)
        endif()
        # ggml keeps its own host tuning (GGML_NATIVE defaults ON).
        return()
    endif()
    if(NOT mode STREQUAL "BASELINE")
        message(FATAL_ERROR
            "kan_cpu_baseline: mode must be BASELINE or NATIVE (got '${mode}')")
    endif()

    # ggml: no host tuning, x86 kernels capped at the x86-64-v2 level.
    # The caps matter: with GGML_NATIVE=OFF but a native (non-cross) build,
    # ggml's INS_ENB default turns GGML_SSE42/AVX/AVX2/BMI2/FMA/F16C all
    # ON, which is above the baseline. CACHE FORCE also overrides values
    # left over from earlier configure runs of the same build directory.
    set(GGML_NATIVE OFF CACHE BOOL
        "ggml: optimize the build for the current system" FORCE)
    if(CMAKE_SYSTEM_PROCESSOR MATCHES "^(x86_64|amd64|AMD64)$")
        set(GGML_SSE42 ON CACHE BOOL "ggml: enable SSE 4.2" FORCE)
        foreach(_kan_ggml_feat GGML_AVX GGML_AVX_VNNI GGML_AVX2 GGML_BMI2
                               GGML_FMA GGML_F16C)
            set(${_kan_ggml_feat} OFF CACHE BOOL
                "ggml: capped at the kan! x86-64-v2 baseline" FORCE)
        endforeach()
    endif()

    # The baseline for everything compiled below the calling directory.
    if(MSVC)
        # No -march levels on MSVC: the x64 floor (SSE2) is the baseline.
    elseif(CMAKE_SYSTEM_PROCESSOR MATCHES "^(x86_64|amd64|AMD64)$")
        add_compile_options(-march=x86-64-v2)
    elseif(CMAKE_SYSTEM_PROCESSOR MATCHES "^(aarch64|arm64)$")
        if(NOT APPLE)
            add_compile_options(-march=armv8-a)
        endif()
    endif()
endfunction()

# First-party warning policy (CONTRIBUTING section 4: -Wall -Wextra and no
# new warnings). With KAN_WERROR=ON (what CI passes) the warnings become
# errors for kan!-owned targets; third-party targets are never touched, so
# their warnings stay non-fatal. Default OFF keeps local builds calm.
option(KAN_WERROR "Treat warnings as errors for first-party targets" OFF)
function(kan_first_party_target target)
    if(MSVC)
        target_compile_options(${target} PRIVATE /W4)
        if(KAN_WERROR)
            target_compile_options(${target} PRIVATE /WX)
        endif()
    else()
        target_compile_options(${target} PRIVATE -Wall -Wextra)
        if(KAN_WERROR)
            target_compile_options(${target} PRIVATE -Werror)
        endif()
    endif()
endfunction()
