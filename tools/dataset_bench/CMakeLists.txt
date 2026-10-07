# kan_dataset_bench - large-scale evaluation of libkan against datasets with
# millisecond-aligned ground truth (MAESTRO first; manifest keeps it open for
# other datasets/instruments). Extractable as its own repo later.
add_executable(kan_dataset_bench main.cpp)
target_include_directories(kan_dataset_bench PRIVATE
    ${miniaudio_SOURCE_DIR}
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${CMAKE_SOURCE_DIR}/libkan/include)
target_link_libraries(kan_dataset_bench PRIVATE libkan)

if(UNIX AND NOT APPLE)
    target_link_libraries(kan_dataset_bench PRIVATE pthread m dl)
elseif(APPLE)
    target_link_libraries(kan_dataset_bench PRIVATE pthread m)
endif()

# Pipeline check without any dataset: SMF round-trip, wav, measurement.
add_test(NAME dataset_bench_selftest
         COMMAND kan_dataset_bench --selftest --selftest-dir dataset_bench_selftest_out)
