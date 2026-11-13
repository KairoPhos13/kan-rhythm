# kan_chart_scribe - offline transcription helper (audio -> chart draft,
# MIDI sketch, readable log). Extractable as its own repo later.
add_executable(kan_chart_scribe main.cpp)
kan_first_party_target(kan_chart_scribe)
target_include_directories(kan_chart_scribe PRIVATE
    ${miniaudio_SOURCE_DIR}
    ${CMAKE_SOURCE_DIR}/libkan/include)
target_link_libraries(kan_chart_scribe PRIVATE libkan)
if(UNIX AND NOT APPLE)
    target_link_libraries(kan_chart_scribe PRIVATE pthread m dl)
elseif(APPLE)
    target_link_libraries(kan_chart_scribe PRIVATE pthread m)
endif()
add_test(NAME chart_scribe_selftest
         COMMAND kan_chart_scribe --selftest)
