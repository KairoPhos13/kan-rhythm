# kan_stem_split - Layer 0: source separation (Demucs wrapper) + stem
# remixing for game modes (autoplay=all, instrument mode=all minus stem).
add_executable(kan_stem_split main.cpp)
target_include_directories(kan_stem_split PRIVATE
    ${miniaudio_SOURCE_DIR}
    ${CMAKE_SOURCE_DIR}/libkan/include)
target_link_libraries(kan_stem_split PRIVATE libkan)
if(UNIX AND NOT APPLE)
    target_link_libraries(kan_stem_split PRIVATE pthread m dl)
elseif(APPLE)
    target_link_libraries(kan_stem_split PRIVATE pthread m)
endif()
add_test(NAME stem_split_selftest COMMAND kan_stem_split --selftest)
