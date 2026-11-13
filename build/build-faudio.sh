#!/bin/sh
# Rebuilds prebuilt/libFAudio.so from upstream FAudio 21.07 (zlib licence, unmodified) with the RG35XX toolchain.
# Usage: build/build-faudio.sh [toolchain image]
set -e
IMG=${1:-koriki-toolchain}; ROOT=$(cd "$(dirname "$0")/.." && pwd)
docker run --rm -v "$ROOT/prebuilt":/out "$IMG" sh -c '
  git clone -q --depth 1 --branch 21.07 https://github.com/FNA-XNA/FAudio.git /tmp/FAudio && cd /tmp/FAudio && mkdir b && cd b &&
  cmake .. -DCMAKE_TOOLCHAIN_FILE=/opt/rg35xx/share/buildroot/toolchainfile.cmake -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_C_FLAGS="-O2 -mcpu=cortex-a9 -mfpu=neon -mfloat-abi=hard" \
    -DSDL2_DIR=/opt/rg35xx/arm-buildroot-linux-gnueabihf/sysroot/usr/lib/cmake/SDL2 > /dev/null &&
  make -j"$(nproc)" > /dev/null && cp -L libFAudio.so.0 /out/libFAudio.so'
ls -la "$ROOT/prebuilt/libFAudio.so"
