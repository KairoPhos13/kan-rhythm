cmake_minimum_required(VERSION 3.20)
project(kan-rhythm VERSION 0.1.0 LANGUAGES CXX C)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Optimización máxima para DSP en tiempo real
if(MSVC)
    add_compile_options(/O2 /arch:AVX2)
else()
    add_compile_options(-O3 -march=native -Wall -Wextra)
endif()

include(FetchContent)

# Descarga automática de miniaudio (MIT-0)
FetchContent_Declare(
    miniaudio
    GIT_REPOSITORY https://github.com/mackron/miniaudio.git
    GIT_TAG        master
    GIT_SHALLOW    TRUE
)
FetchContent_MakeAvailable(miniaudio)

add_executable(kan_cli src/main.cpp)
target_include_directories(kan_cli PRIVATE src ${miniaudio_SOURCE_DIR})

# Enlaces de sistema para hilos de tiempo real en Linux/macOS
if(UNIX AND NOT APPLE)
    target_link_libraries(kan_cli PRIVATE pthread m dl)
elseif(APPLE)
    target_link_libraries(kan_cli PRIVATE "-framework CoreAudio" "-framework AudioToolbox" pthread m)
endif()
