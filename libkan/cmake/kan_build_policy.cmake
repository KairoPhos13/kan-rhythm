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
#   x86_64   x86-64-v3 (AVX2, FMA, F16C, BMI2; Intel Haswell 2013+, AMD
#            Zen and later, the Steam Deck included). GCC/Clang/MinGW get
#            -march=x86-64-v3 and ggml's x86 kernels go up to AVX2, FMA
#            and F16C, never AVX-512 or the build host's own extras. MSVC
#            gets /arch:AVX2.
#
#            The baseline was x86-64-v2 with ggml capped at SSE4.2 (audit
#            C2, 2026-11-12). That made the stem separator's CPU pass many
#            times slower: the f16 model converts every value in scalar
#            code without F16C, and the device check's CPU reference did
#            not finish in 20 minutes on a 12 core desktop (owner
#            2026-11-15: a demux stayed on "device check"; the owner asked
#            to lift the cap).
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

    # ggml: no host tuning (a CI host may have AVX-512, which the Steam
    # Deck and many player CPUs lack), x86 kernels at the x86-64-v3 level.
    # CACHE FORCE also overrides values left over from earlier configure
    # runs of the same build directory.
    set(GGML_NATIVE OFF CACHE BOOL
        "ggml: optimize the build for the current system" FORCE)
    if(CMAKE_SYSTEM_PROCESSOR MATCHES "^(x86_64|amd64|AMD64)$")
        foreach(_kan_ggml_feat GGML_SSE42 GGML_AVX GGML_AVX2 GGML_BMI2
                               GGML_FMA GGML_F16C)
            set(${_kan_ggml_feat} ON CACHE BOOL
                "ggml: the kan! x86-64-v3 baseline" FORCE)
        endforeach()
        foreach(_kan_ggml_feat GGML_AVX_VNNI GGML_AVX512 GGML_AVX512_VBMI
                               GGML_AVX512_VNNI GGML_AVX512_BF16 GGML_AMX_TILE
                               GGML_AMX_INT8 GGML_AMX_BF16)
            set(${_kan_ggml_feat} OFF CACHE BOOL
                "ggml: above the kan! x86-64-v3 baseline" FORCE)
        endforeach()
    endif()

    # The baseline for everything compiled below the calling directory.
    if(MSVC)
        add_compile_options(/arch:AVX2)
    elseif(CMAKE_SYSTEM_PROCESSOR MATCHES "^(x86_64|amd64|AMD64)$")
        add_compile_options(-march=x86-64-v3)
    elseif(CMAKE_SYSTEM_PROCESSOR MATCHES "^(aarch64|arm64)$")
        if(NOT APPLE)
            add_compile_options(-march=armv8-a)
        endif()
    endif()
endfunction()

# First-party warning policy (CONTRIBUTING section 4: -Wall -Wextra and no
# new warnings). With KAN_WERROR=ON (what CI passes) the warnings become
# errors for kan!-owned targets on GCC and Clang; third-party targets are never touched, so
# their warnings stay non-fatal. Default OFF keeps local builds calm.
option(KAN_WERROR "Treat warnings as errors for first-party targets" OFF)
function(kan_first_party_target target)
    if(MSVC)
        # Headers included as SYSTEM (miniaudio) stay quiet: /external:W0.
        # /W4 is shown but not fatal on MSVC even with KAN_WERROR: the
        # first tag build with /WX (v0.1.1, 2026-11-13) stopped on a
        # miniaudio warning, and MSVC cannot be run before a push here.
        # GCC and Clang (Linux, macOS, the MinGW build) keep -Werror.
        target_compile_options(${target} PRIVATE /W4 /external:W0)
    else()
        target_compile_options(${target} PRIVATE -Wall -Wextra)
        if(KAN_WERROR)
            target_compile_options(${target} PRIVATE -Werror)
        endif()
    endif()
endfunction()
