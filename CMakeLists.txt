cmake_minimum_required(VERSION 3.20)
project(kan-rhythm VERSION 0.1.0 LANGUAGES CXX C)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

option(KAN_BUILD_TESTS "Build component tests" ON)

if(KAN_BUILD_TESTS)
    enable_testing()
endif()

# Maximum optimization for real-time DSP
if(MSVC)
    add_compile_options(/O2 /arch:AVX2)
else()
    add_compile_options(-O3 -march=native -Wall -Wextra)
endif()

include(FetchContent)

# Automatic download of miniaudio (MIT-0)
FetchContent_Declare(
    miniaudio
    GIT_REPOSITORY https://github.com/mackron/miniaudio.git
    GIT_TAG        master
    GIT_SHALLOW    TRUE
)
FetchContent_MakeAvailable(miniaudio)

# Components (each top-level folder is designed to be splittable into its own repo)
add_subdirectory(libkan)
add_subdirectory(apps)
