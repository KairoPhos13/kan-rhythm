# libkan - header-only real-time DSP library (onset + pitch detection)
add_library(libkan INTERFACE)
target_include_directories(libkan INTERFACE
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)
target_compile_features(libkan INTERFACE cxx_std_20)

# Score-informed BACF detector backend (Cycfi Q, MIT — fetched by the root
# CMakeLists under KAN_ENABLE_BACF). Consumers of bacf_detector.hpp link
# libkan_bacf instead of plain libkan.
if(TARGET libq)
    add_library(libkan_bacf INTERFACE)
    target_link_libraries(libkan_bacf INTERFACE libkan libq)
    # GCC's IPA -Wmaybe-uninitialized can fire at call sites of Cycfi Q
    # header code even with SYSTEM includes; silence just that diagnostic
    # for consumers of the BACF backend (all other warnings stay on).
    if(CMAKE_CXX_COMPILER_ID STREQUAL "GNU")
        target_compile_options(libkan_bacf INTERFACE -Wno-maybe-uninitialized)
    endif()
endif()

# Score-informed chord validation backends:
# - chord_goertzel.hpp / harmonic_chord.hpp: dependency-free (core libkan)
# - chroma_fft.hpp: KissFFT (BSD-3) 12-bin chroma, via the libkan_fft target
if(TARGET kissfft)
    add_library(libkan_fft INTERFACE)
    target_link_libraries(libkan_fft INTERFACE libkan kissfft::kissfft)
endif()

# Backing-track pitch-shift primitive (Signalsmith Stretch, MIT).
if(TARGET signalsmith-stretch)
    add_library(libkan_stretch INTERFACE)
    target_link_libraries(libkan_stretch INTERFACE libkan signalsmith-stretch)
    # Upstream's INTERFACE target carries no include dirs; the canonical
    # header is include/signalsmith-stretch/signalsmith-stretch.h.
    target_include_directories(libkan_stretch INTERFACE
        ${signalsmith_stretch_SOURCE_DIR}/include)
endif()

if(KAN_BUILD_TESTS)
    add_executable(libkan_dsp_test tests/dsp_test.cpp)
    target_link_libraries(libkan_dsp_test PRIVATE libkan)
    add_test(NAME libkan_dsp_test COMMAND libkan_dsp_test)

    add_executable(libkan_loudness_test tests/loudness_test.cpp)
    target_link_libraries(libkan_loudness_test PRIVATE libkan)
    add_test(NAME libkan_loudness_test COMMAND libkan_loudness_test)

    add_executable(libkan_precondition_test tests/precondition_test.cpp)
    target_link_libraries(libkan_precondition_test PRIVATE libkan)
    add_test(NAME libkan_precondition_test COMMAND libkan_precondition_test)

    add_executable(libkan_blanking_test tests/blanking_test.cpp)
    target_link_libraries(libkan_blanking_test PRIVATE libkan)
    add_test(NAME libkan_blanking_test COMMAND libkan_blanking_test)

    add_executable(libkan_chord_test tests/chord_test.cpp)
    target_link_libraries(libkan_chord_test PRIVATE libkan)
    add_test(NAME libkan_chord_test COMMAND libkan_chord_test)

    add_executable(libkan_note_stream_test tests/note_stream_test.cpp)
    target_link_libraries(libkan_note_stream_test PRIVATE libkan)
    add_test(NAME libkan_note_stream_test COMMAND libkan_note_stream_test)

    add_executable(libkan_calibration_test tests/calibration_test.cpp)
    target_link_libraries(libkan_calibration_test PRIVATE libkan)
    add_test(NAME libkan_calibration_test COMMAND libkan_calibration_test)

    add_executable(libkan_technique_test tests/technique_test.cpp)
    target_link_libraries(libkan_technique_test PRIVATE libkan)
    add_test(NAME libkan_technique_test COMMAND libkan_technique_test)

    add_executable(libkan_tap_test tests/tap_test.cpp)
    target_link_libraries(libkan_tap_test PRIVATE libkan)
    add_test(NAME libkan_tap_test COMMAND libkan_tap_test)

    add_executable(libkan_range_test tests/range_test.cpp)
    target_link_libraries(libkan_range_test PRIVATE libkan)
    if(TARGET libkan_bacf)
        target_compile_definitions(libkan_range_test PRIVATE RANGE_TEST_HAVE_BACF=1)
        target_link_libraries(libkan_range_test PRIVATE libkan_bacf)
    endif()
    add_test(NAME libkan_range_test COMMAND libkan_range_test)

    if(TARGET libkan_stretch)
        add_executable(libkan_stretch_test tests/stretch_test.cpp)
        target_link_libraries(libkan_stretch_test PRIVATE libkan_stretch)
        add_test(NAME libkan_stretch_test COMMAND libkan_stretch_test)
    endif()

    # Loopback regression harness: generate the WAV fixtures, then run the
    # pipeline over them (CTest fixture ordering guarantees the sequence).
    add_executable(libkan_fixture_gen tests/fixture_gen.cpp)
    target_link_libraries(libkan_fixture_gen PRIVATE libkan)
    add_executable(libkan_loopback_test tests/loopback_test.cpp)
    target_link_libraries(libkan_loopback_test PRIVATE libkan)

    add_test(NAME libkan_fixture_gen
             COMMAND libkan_fixture_gen ${CMAKE_CURRENT_BINARY_DIR}/fixtures)
    set_tests_properties(libkan_fixture_gen PROPERTIES
        FIXTURES_SETUP kan_fixtures)
    add_test(NAME libkan_loopback_test
             COMMAND libkan_loopback_test ${CMAKE_CURRENT_BINARY_DIR}/fixtures)
    set_tests_properties(libkan_loopback_test PROPERTIES
        FIXTURES_REQUIRED kan_fixtures)

    if(TARGET libkan_fft)
        add_executable(libkan_chroma_test tests/chroma_test.cpp)
        target_link_libraries(libkan_chroma_test PRIVATE libkan_fft)
        add_test(NAME libkan_chroma_test COMMAND libkan_chroma_test)

        # Evidence probe for chord validation on real recordings (Phase 1 of
        # the note-description-stream work). Test-side tool, deliberately NOT
        # a ctest: it consumes real audio from test_results/ (gitignored).
        # See libkan/tests/harmonic_probe_notes.hpp for the annotation.
        add_executable(libkan_harmonic_probe tests/harmonic_probe.cpp)
        target_link_libraries(libkan_harmonic_probe PRIVATE libkan_fft)
    endif()

    if(TARGET libkan_bacf)
            add_executable(libkan_bacf_test tests/bacf_test.cpp)
        target_link_libraries(libkan_bacf_test PRIVATE libkan_bacf)
        add_test(NAME libkan_bacf_test COMMAND libkan_bacf_test)

        add_executable(libkan_bacf_bench tests/bacf_bench.cpp)
        target_link_libraries(libkan_bacf_bench PRIVATE libkan_bacf)
        add_test(NAME libkan_bacf_bench COMMAND libkan_bacf_bench)
    endif()
endif()
# ---- Neural transcription: Basic Pitch (Spotify AB, Apache-2.0) ----
# Owner decision 2026-10-15: a trained transcriber for the editor's
# starting score and, later, the judge's "is this note sounding?". The
# network runs in plain C++ (src/basic_pitch.cpp); its weights are
# compiled in (src/basic_pitch_weights.inc, tools/basic_pitch_export.py).
# Licence and notice: libkan/THIRD_PARTY_NOTICES.md.
find_package(Threads REQUIRED)
add_library(libkan_basic_pitch STATIC src/basic_pitch.cpp)
target_include_directories(libkan_basic_pitch PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include)
target_compile_features(libkan_basic_pitch PUBLIC cxx_std_20)
target_compile_definitions(libkan_basic_pitch PUBLIC KAN_HAVE_BASIC_PITCH=1)
target_link_libraries(libkan_basic_pitch PUBLIC Threads::Threads)
target_link_libraries(libkan INTERFACE libkan_basic_pitch)
if(KAN_BUILD_TESTS)
    add_executable(libkan_basic_pitch_test tests/basic_pitch_test.cpp)
    target_link_libraries(libkan_basic_pitch_test PRIVATE libkan_basic_pitch)
    add_test(NAME libkan_basic_pitch_test COMMAND libkan_basic_pitch_test)
endif()

add_executable(libkan_score_error_test tests/score_error_test.cpp)
target_include_directories(libkan_score_error_test PRIVATE include)
add_test(NAME score_error COMMAND libkan_score_error_test)

# ---- In-process GGUF RoFormer separation runtime (single stack) ----
# Official separation flow is self-contained (docs/implementation/
# kan-editor-agent-prompt.md, game/docs/editor-pipeline.md s7b): no
# installed separator binary — only the auto-downloaded GGUF model.
# Third-party code (BSRoformer.cpp + GGML, both MIT) compiles INTO
# libkan_roformer; consumers get it automatically via libkan.
option(KAN_ENABLE_ROFORMER "Compile the GGUF RoFormer runtime into libkan" ON)
if(KAN_ENABLE_ROFORMER)
    include(FetchContent)
    # Static everywhere (repo style): ggml's option(BUILD_SHARED_LIBS) would
    # otherwise cache ON and conflict with KISSFFT_STATIC later in the run.
    set(BUILD_SHARED_LIBS OFF CACHE BOOL "static libraries" FORCE)
    # GPU backend for the shipped runtime (device selection is automatic at
    # runtime; CPU fallback inherent; BSR_FORCE_CPU stays as the escape
    # hatch). KAN_GGML_BACKEND = AUTO | VULKAN | METAL | CPU:
    #   AUTO   Metal on Apple, Vulkan when its tools are found, CPU otherwise
    #   VULKAN Vulkan when its tools are found, CPU otherwise (with a warning)
    #   METAL  Metal (Apple only; the shader is embedded in the library so the
    #          shipped dylib needs no ggml-metal.metal file next to the game)
    #   CPU    no GPU backend
    # FORCE wins over stale cache values from earlier configure runs.
    set(KAN_GGML_BACKEND "AUTO" CACHE STRING
        "ggml GPU backend for the RoFormer runtime: AUTO, VULKAN, METAL or CPU")
    set_property(CACHE KAN_GGML_BACKEND PROPERTY STRINGS AUTO VULKAN METAL CPU)
    string(TOUPPER "${KAN_GGML_BACKEND}" _kan_ggml_backend)
    set(_kan_ggml_vulkan OFF)
    set(_kan_ggml_metal OFF)
    if(_kan_ggml_backend STREQUAL "AUTO")
        if(APPLE)
            set(_kan_ggml_metal ON)
        else()
            find_package(Vulkan QUIET)
            find_program(KAN_GLSLC glslc)
            if(Vulkan_FOUND AND KAN_GLSLC)
                set(_kan_ggml_vulkan ON)
            else()
                message(STATUS "libkan: Vulkan tools not found — RoFormer runs on CPU")
            endif()
        endif()
    elseif(_kan_ggml_backend STREQUAL "VULKAN")
        find_package(Vulkan QUIET)
        find_program(KAN_GLSLC glslc)
        if(Vulkan_FOUND AND KAN_GLSLC)
            set(_kan_ggml_vulkan ON)
        else()
            message(WARNING
                "libkan: KAN_GGML_BACKEND=VULKAN but the Vulkan SDK was not "
                "found (Vulkan headers + glslc) — RoFormer runs on CPU")
        endif()
    elseif(_kan_ggml_backend STREQUAL "METAL")
        if(NOT APPLE)
            message(FATAL_ERROR "libkan: KAN_GGML_BACKEND=METAL needs an Apple platform")
        endif()
        set(_kan_ggml_metal ON)
    elseif(_kan_ggml_backend STREQUAL "CPU")
        # no GPU backend
    else()
        message(FATAL_ERROR
            "libkan: unknown KAN_GGML_BACKEND '${KAN_GGML_BACKEND}' (AUTO, VULKAN, METAL, CPU)")
    endif()
    set(GGML_VULKAN ${_kan_ggml_vulkan} CACHE BOOL "ggml: Vulkan backend" FORCE)
    set(GGML_METAL ${_kan_ggml_metal} CACHE BOOL "ggml: Metal backend" FORCE)
    if(_kan_ggml_metal)
        set(GGML_METAL_EMBED_LIBRARY ON CACHE BOOL "ggml: embed the Metal shader" FORCE)
    endif()
    message(STATUS "libkan: RoFormer ggml backend — Vulkan=${_kan_ggml_vulkan} Metal=${_kan_ggml_metal}")
    # PINNED: the Vulkan backend is correctness-critical (ggml-vulkan q8_0
    # integer dot product produced wrong stems on RADV NAVI21 at
    # 3612c0af). Update only WITH the parity test (libkan_runtime_test,
    # KAN_ROFORMER_PARITY=1) green on real hardware.
    set(KAN_GGML_PIN 3612c0af93f2cfc8f048295300a61187723b12ae)
    set(KAN_BSR_PIN a7b9625f0f4146cacf3c46080d1139833cd4d4c2)
    FetchContent_Declare(ggml
        GIT_REPOSITORY https://github.com/ggerganov/ggml.git
        GIT_TAG ${KAN_GGML_PIN} GIT_SHALLOW FALSE)
    FetchContent_MakeAvailable(ggml)
    FetchContent_Declare(bsroformer
        GIT_REPOSITORY https://github.com/chenmozhijin/BSRoformer.cpp.git
        GIT_TAG ${KAN_BSR_PIN} GIT_SHALLOW FALSE
        SOURCE_SUBDIR _skip) # its own CMake needs GGML_DIR; we compile sources
    FetchContent_MakeAvailable(bsroformer)

    set(BSR ${bsroformer_SOURCE_DIR})
    add_library(libkan_roformer STATIC
        src/roformer_runtime.cpp
        ${BSR}/src/inference.cpp
        ${BSR}/src/model.cpp
        ${BSR}/src/audio.cpp
        ${BSR}/src/utils.cpp)
    target_include_directories(libkan_roformer PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include)
    target_include_directories(libkan_roformer SYSTEM PRIVATE ${BSR}/include)
    target_include_directories(libkan_roformer PRIVATE ${BSR}/src)
    target_include_directories(libkan_roformer SYSTEM PRIVATE ${BSR}/third_party)
    target_link_libraries(libkan_roformer PUBLIC ggml)
    target_compile_features(libkan_roformer PUBLIC cxx_std_17)
    target_compile_definitions(libkan_roformer PUBLIC KAN_HAVE_ROFORMER_RUNTIME=1)
    # The parity marker is keyed on the pins: the probe re-runs when the GPU
    # code changes, not on every rebuild of libkan (its CPU pass takes minutes).
    target_compile_definitions(libkan_roformer PRIVATE
        KAN_GGML_PIN="${KAN_GGML_PIN}" KAN_BSR_PIN="${KAN_BSR_PIN}")
    # Third-party sources must not pollute the -Wall -Wextra clean rule.
    set_source_files_properties(${BSR}/src/inference.cpp ${BSR}/src/model.cpp
        ${BSR}/src/audio.cpp ${BSR}/src/utils.cpp
        PROPERTIES COMPILE_OPTIONS "-w")
    find_package(Threads REQUIRED)
    target_link_libraries(libkan_roformer PUBLIC Threads::Threads)
    if(TARGET ggml-vulkan)
        target_compile_definitions(libkan_roformer PRIVATE KAN_GGML_HAS_VULKAN=1)
    endif()
    target_link_libraries(libkan INTERFACE libkan_roformer)
    if(KAN_BUILD_TESTS)
        add_executable(libkan_runtime_test tests/runtime_test.cpp)
        target_link_libraries(libkan_runtime_test PRIVATE libkan_roformer)
        add_test(NAME roformer_runtime COMMAND libkan_runtime_test)
    endif()
endif()
