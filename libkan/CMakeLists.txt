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

    add_executable(libkan_precondition_test tests/precondition_test.cpp)
    target_link_libraries(libkan_precondition_test PRIVATE libkan)
    add_test(NAME libkan_precondition_test COMMAND libkan_precondition_test)

    add_executable(libkan_blanking_test tests/blanking_test.cpp)
    target_link_libraries(libkan_blanking_test PRIVATE libkan)
    add_test(NAME libkan_blanking_test COMMAND libkan_blanking_test)

    add_executable(libkan_chord_test tests/chord_test.cpp)
    target_link_libraries(libkan_chord_test PRIVATE libkan)
    add_test(NAME libkan_chord_test COMMAND libkan_chord_test)

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
