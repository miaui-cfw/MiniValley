#!/bin/sh
# MiniValley: builds "Stardew Valley.pak" for the RG35XX from YOUR copy of Stardew Valley 1.6.8 (Steam, PC).
# Usage: ./build-pak.sh "/path/to/Stardew Valley" [options]
#   --sysroot DIR        armhf sysroot (default: .sysroot, created with build/make-sysroot.sh)
#   --out DIR            where to put "Stardew Valley.pak" (default: ./out)
#   --controllermaps DIR optional: folder with ControllerMaps*.xnb from your own copy of the Switch version
#                        (Joy-Con button prompts); without it the game shows the PC prompts
#   --strings FILE       optional: JSON that replaces game texts, e.g. the menu's "Exit to Desktop"
#                        (see examples/custom_menu_strings.json)
# Needs Docker. Nothing from the game leaves your machine; the output pak is for your own use only.
set -e
ROOT=$(cd "$(dirname "$0")" && pwd)
GAME=$1; [ -n "$GAME" ] && shift || { sed -n 2,11p "$0"; exit 1; }
SYSROOT=$ROOT/.sysroot; OUT=$ROOT/out; CMAPS=; STRINGS=
while [ $# -gt 0 ]; do case $1 in
  --sysroot) SYSROOT=$2; shift 2;; --out) OUT=$2; shift 2;; --controllermaps) CMAPS=$2; shift 2;;
  --strings) STRINGS=$(realpath "$2"); shift 2;;
  *) echo "unknown option $1"; exit 1;; esac; done

# the patches target exactly these files (Stardew Valley 1.6.8, PC)
for f in "Stardew Valley.dll:d064f2f8cd3e948d367fce63d10516f4" "MonoGame.Framework.dll:5f85f6b7271783914929afd1fe1dca18" "xTile.dll:49129fe7263d55ad1a3224cf5213a42e"; do
  name=${f%%:*}; want=${f##*:}
  [ -f "$GAME/$name" ] || { echo "not found: $GAME/$name (point at the folder with Stardew Valley.dll and Content/)"; exit 1; }
  have=$(md5sum "$GAME/$name" | cut -c1-32)
  [ "$have" = "$want" ] || echo "WARNING: $name is not the 1.6.8 PC file MiniValley was made for ($have); the build may fail"
done
[ -d "$GAME/Content" ] || { echo "not found: $GAME/Content"; exit 1; }
[ -d "$SYSROOT/usr/lib" ] || { echo "no sysroot at $SYSROOT: run build/make-sysroot.sh (or pass --sysroot)"; exit 1; }
[ -z "$STRINGS" ] || [ -f "$STRINGS" ] || { echo "not found: $STRINGS"; exit 1; }

echo "== build image"; docker build -q -t minivalley-build "$ROOT/build" > /dev/null
WORK=$(mktemp -d); trap 'rm -rf "$WORK"' EXIT

# the pak's Content (without the XACT banks), staged first so that --strings fails in seconds, not after the long build
( cd "$GAME" && while IFS= read -r f; do mkdir -p "$WORK/pak/$(dirname "$f")"; cp "$f" "$WORK/pak/$f"; done < "$ROOT/data/pak-content.txt" )
if [ -n "$STRINGS" ]; then
  echo "== replacing texts from $STRINGS"
  docker run --rm -v "$ROOT":/src:ro -v "$GAME":/game:ro -v "$STRINGS":/strings.json:ro -v "$WORK":/w minivalley-build sh -c \
    'cp -r /src/src/xnbstrings /w/xnbstrings && dotnet build /w/xnbstrings -c Release -o /w/xnbstrings/out --nologo -v q > /w/xs.log || { cat /w/xs.log; exit 1; }
     dotnet /w/xnbstrings/out/XnbStrings.dll /strings.json /game/Content /w/pak/Content; s=$?; chown -R '"$(id -u):$(id -g)"' /w; exit $s'
fi

docker run --rm -v "$ROOT":/src:ro -v "$GAME":/game:ro -v "$SYSROOT":/sysroot:ro -v "$WORK":/w minivalley-build sh /src/build/pipeline.sh
docker run --rm -v "$WORK":/w minivalley-build chown -R "$(id -u):$(id -g)" /w

echo "== assembling the pak"
PAK="$OUT/Stardew Valley.pak"; mkdir -p "$PAK"
cp "$WORK/out/StardewAot" "$PAK/"; cp "$ROOT/prebuilt/libFAudio.so" "$PAK/"
cp "$ROOT/pak-template/launch.sh" "$ROOT/pak-template/gamecontroller.txt" "$ROOT/pak-template/README.md" "$PAK/"
cp -r "$WORK/pak/Content" "$PAK/"
( cd "$GAME" && find Content/XACT -type f | while IFS= read -r f; do mkdir -p "$PAK/$(dirname "$f")"; cp "$f" "$PAK/$f"; done )
[ -n "$CMAPS" ] && cp "$CMAPS"/ControllerMaps*.xnb "$PAK/Content/LooseSprites/" && echo "Joy-Con prompts installed"
du -sh "$PAK"; echo "done: copy \"$PAK\" to /Ports/ on your SD card"
