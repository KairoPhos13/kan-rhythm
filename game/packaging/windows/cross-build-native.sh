#!/usr/bin/env bash
# Cross-compile game/forge/native for Windows x64 (kan_forge_native.dll) in
# a Docker container with the MinGW-w64 toolchain. No installs on the host,
# no sudo. The toolchain image (game/packaging/windows/Dockerfile.mingw) is
# kept locally; every build container is --rm and there is only one at a
# time (--cpus 6, --parallel 6, nice -n 10).
#
# Usage: game/packaging/windows/cross-build-native.sh [out-dir]
#   out-dir  where kan_forge_native.dll lands
#            (default: game/forge/native/build-win-x64)
#
# Build choices:
#   KAN_GGML_BACKEND=CPU   CPU-only separation for this test build (no
#                          Vulkan in the cross build; the runtime keeps its
#                          CPU fallback everywhere and BSR_FORCE_CPU=1
#                          forces it)
#   -static -static-libgcc -static-libstdc++
#                          the C++ runtime is linked INTO the DLL, so it
#                          needs nothing beyond Windows system DLLs
#   CMAKE_SYSTEM_PROCESSOR=x86_64 + GGML_NATIVE=OFF
#                          correct target architecture for ggml's x86 code
#                          paths, but portable baseline codegen: no
#                          -march=native of the BUILD host (an AVX-512 host
#                          would otherwise produce a DLL that dies with
#                          "illegal instruction" on older CPUs). Since the
#                          November 2026 audit (C2) the bridge's CMake
#                          enforces this itself (ggml's x86 kernels capped
#                          at SSE4.2, -march=x86-64-v2 for everything); the
#                          explicit flags here stay as documentation and
#                          belt-and-braces.
#   GGML_OPENMP=OFF        no libgomp-1.dll dependency (the MinGW driver
#                          will not link libgomp statically next to
#                          -shared; the macOS builds ship without OpenMP
#                          too: ggml's own thread pool keeps threading)
#
# Verified after every build (the mission's acceptance check):
#   - imported DLLs (objdump -p "DLL Name"): only Windows system DLLs
#     (KERNEL32, msvcrt/ucrt, WS2_32, ...): no libwinpthread/libgcc/
#     libstdc++/libgomp
#   - every entry point declared in game/core/Native/KanForgeNative.cs is
#     exported by the DLL
set -euo pipefail

repo="$(cd "$(dirname "$0")/../../.." && pwd)"
out="${1:-game/forge/native/build-win-x64}"
image="kan-mingw-w64:bookworm"

cd "$repo"

# Toolchain image (kept; the install is cached in the image layer).
docker build -t "$image" -f game/packaging/windows/Dockerfile.mingw game/packaging/windows

docker run --rm --cpus 6 \
    --user "$(id -u):$(id -g)" -e HOME=/tmp \
    -v "$repo:/work" -w /work \
    -e OUT="$out" \
    -e KAN_VERSION="${KAN_VERSION:-0.0.0}" \
    "$image" bash -eu -c '
        echo "== toolchain"
        x86_64-w64-mingw32-g++-posix --version | head -1

        cmake -S game/forge/native -B "$OUT" \
            -DCMAKE_SYSTEM_NAME=Windows \
            -DCMAKE_SYSTEM_PROCESSOR=x86_64 \
            -DCMAKE_C_COMPILER=x86_64-w64-mingw32-gcc-posix \
            -DCMAKE_CXX_COMPILER=x86_64-w64-mingw32-g++-posix \
            -DCMAKE_BUILD_TYPE=Release \
            -DKAN_GGML_BACKEND=CPU \
            -DKAN_VERSION="$KAN_VERSION" \
            -DGGML_NATIVE=OFF \
            -DGGML_OPENMP=OFF \
            -DCMAKE_SHARED_LINKER_FLAGS="-static -static-libgcc -static-libstdc++"
        nice -n 10 cmake --build "$OUT" --target kan_forge_native --parallel 6

        dll="$OUT/kan_forge_native.dll"
        test -f "$dll"

        echo "== imported DLLs (must be Windows system DLLs only)"
        imports="$(x86_64-w64-mingw32-objdump -p "$dll" | grep "DLL Name")"
        echo "$imports"
        if echo "$imports" | grep -Eiq "libwinpthread|libgcc|libstdc|libgomp|libssp"; then
            echo "ERROR: a MinGW runtime DLL is imported; link it statically" >&2
            exit 1
        fi

        echo "== exported kan_forge_* symbols"
        x86_64-w64-mingw32-objdump -p "$dll" | grep -o "kan_forge_[a-z_]*" | sort -u

        echo "== entry points from game/core/Native/KanForgeNative.cs (all must be exported)"
        missing=0
        for sym in $(grep -o "EntryPoint = @\"[A-Za-z0-9_]*\"" game/core/Native/KanForgeNative.cs \
                     | sed "s/.*@\"\(.*\)\"/\1/" | sort -u); do
            if x86_64-w64-mingw32-objdump -p "$dll" | grep -qw "$sym"; then
                echo "ok   $sym"
            else
                echo "MISSING $sym" >&2
                missing=1
            fi
        done
        test "$missing" = 0

        ls -l "$dll"
        echo "== cross build complete: $dll"
    '
