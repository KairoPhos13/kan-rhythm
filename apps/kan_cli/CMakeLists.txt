# kan_cli - real-time microphone benchmark for the hybrid DSP engine
add_executable(kan_cli main.cpp)
target_link_libraries(kan_cli PRIVATE libkan)
target_include_directories(kan_cli PRIVATE ${miniaudio_SOURCE_DIR})

# System linkage for real-time audio threads on Linux/macOS
if(UNIX AND NOT APPLE)
    target_link_libraries(kan_cli PRIVATE pthread m dl)
elseif(APPLE)
    target_link_libraries(kan_cli PRIVATE "-framework CoreAudio" "-framework AudioToolbox" pthread m)
endif()
