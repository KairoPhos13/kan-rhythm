# kan_demo - live real-instrument demo for the libkan detection pipeline
add_executable(kan_demo main.cpp)
target_include_directories(kan_demo PRIVATE ${miniaudio_SOURCE_DIR})

# Prefer the BACF-enabled libkan when Cycfi Q is available (KAN_ENABLE_BACF).
if(TARGET libkan_bacf)
    target_link_libraries(kan_demo PRIVATE libkan_bacf)
    target_compile_definitions(kan_demo PRIVATE KAN_DEMO_HAVE_BACF=1)
else()
    target_link_libraries(kan_demo PRIVATE libkan)
endif()

# System linkage for real-time audio threads on Linux/macOS
if(UNIX AND NOT APPLE)
    target_link_libraries(kan_demo PRIVATE pthread m dl)
elseif(APPLE)
    target_link_libraries(kan_demo PRIVATE "-framework CoreAudio" "-framework AudioToolbox" pthread m)
endif()

# Offline pipeline check (no audio device): keeps the demo regression-tested.
if(KAN_BUILD_TESTS)
    add_test(NAME kan_demo_selftest COMMAND kan_demo --selftest)
endif()
