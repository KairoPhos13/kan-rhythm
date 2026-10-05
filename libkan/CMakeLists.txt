# libkan - header-only real-time DSP library (onset + pitch detection)
add_library(libkan INTERFACE)
target_include_directories(libkan INTERFACE
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)
target_compile_features(libkan INTERFACE cxx_std_20)

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
endif()
