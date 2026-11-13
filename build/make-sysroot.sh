#!/bin/sh
# Builds the armhf sysroot the NativeAOT linker needs (glibc, libstdc++, libgcc of the RG35XX toolchain) from the
# Koriki/MiaUI buildroot toolchain image. Usage: build/make-sysroot.sh [image] [out dir]
# Any armhf sysroot whose glibc is not newer than the device's also works.
set -e
IMG=${1:-koriki-toolchain}; OUT=${2:-$(dirname "$0")/../.sysroot}
TC=/opt/rg35xx
mkdir -p "$OUT/usr/lib/gcc/arm-linux-gnueabihf"
docker run --rm "$IMG" tar -C $TC/arm-buildroot-linux-gnueabihf/sysroot -chf - . | tar -C "$OUT" -xf -
docker run --rm "$IMG" tar -C $TC/lib/gcc/arm-buildroot-linux-gnueabihf -chf - 12.2.0 | tar -C "$OUT/usr/lib/gcc/arm-linux-gnueabihf" -xf -
du -sh "$OUT"
