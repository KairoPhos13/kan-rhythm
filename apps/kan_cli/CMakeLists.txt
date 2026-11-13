# kan_cli - real-time microphone benchmark for the hybrid DSP engine
add_executable(kan_cli main.cpp)
kan_first_party_target(kan_cli)
target_include_directories(kan_cli SYSTEM PRIVATE ${miniaudio_SOURCE_DIR})

# Prefer the BACF-enabled libkan when Cycfi Q is available (KAN_ENABLE_BACF).
if(TARGET libkan_bacf)
    target_link_libraries(kan_cli PRIVATE libkan_bacf)
    target_compile_definitions(kan_cli PRIVATE KAN_CLI_HAVE_BACF=1)
else()
    target_link_libraries(kan_cli PRIVATE libkan)
endif()

# System linkage for real-time audio threads on Linux/macOS
if(UNIX AND NOT APPLE)
    target_link_libraries(kan_cli PRIVATE pthread m dl)
elseif(APPLE)
    target_link_libraries(kan_cli PRIVATE "-framework CoreAudio" "-framework AudioToolbox" pthread m)
endif()
