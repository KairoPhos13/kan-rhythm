# kan_analyze - song performance descriptor (offline analysis tool)
add_executable(kan_analyze main.cpp)
kan_first_party_target(kan_analyze)
target_include_directories(kan_analyze SYSTEM PRIVATE ${miniaudio_SOURCE_DIR})

if(TARGET libkan_bacf)
    target_link_libraries(kan_analyze PRIVATE libkan_bacf)
else()
    target_link_libraries(kan_analyze PRIVATE libkan)
endif()

if(UNIX AND NOT APPLE)
    target_link_libraries(kan_analyze PRIVATE pthread m dl)
elseif(APPLE)
    target_link_libraries(kan_analyze PRIVATE "-framework CoreAudio" "-framework AudioToolbox" pthread m)
endif()

if(KAN_BUILD_TESTS)
    add_test(NAME kan_analyze_selftest COMMAND kan_analyze --synth ocarina --instrument ocarina)
    set_tests_properties(kan_analyze_selftest PROPERTIES PASS_REGULAR_EXPRESSION "performance descriptor")

    # Report honesty: one consistent estimate per row (note/Hz/cents verify
    # each other) and "-" / null pitch cells on MIX/HOP rows, exercised on
    # the synthesized "hop" fixture (one attack, two competing sources).
    add_test(NAME kan_analyze_hop_test COMMAND kan_analyze --selftest)
    set_tests_properties(kan_analyze_hop_test PROPERTIES PASS_REGULAR_EXPRESSION "selftest: ok")
endif()
