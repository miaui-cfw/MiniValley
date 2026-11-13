#!/bin/sh
# Runs inside the MiniValley build image. Mounts: /src = this repo (read-only), /game = the player's Stardew Valley
# folder (read-only), /sysroot = armhf sysroot (read-only), /w = scratch work dir. Output: /w/out/StardewAot
set -e
say() { echo "== $*"; }
mkdir -p /w/libs /w/out
cp -r /src/src/patcher /src/src/patches /src/src/game /src/src/xmlgen /src/src/xmlser /w/

say "decompiling your MonoGame.Framework.dll and xTile.dll (ILSpy 11.2) and applying the MiniValley patches"
for fork in mgfork:MonoGame.Framework:monogame xtile-src:xTile:xtile; do
  dir=${fork%%:*}; rest=${fork#*:}; dll=${rest%%:*}; name=${rest#*:}
  rm -rf /w/$dir; mkdir -p /w/$dir
  DOTNET_ROLL_FORWARD=Major /opt/ilspy/ilspycmd -p -o /w/$dir /game/$dll.dll > /dev/null
  cp /src/forks/$name/*.csproj /w/$dir/
  (cd /w/$dir && git apply --whitespace=nowarn /src/forks/$name/rg35xx.patch)
done

say "building the GLES2 MonoGame fork and xTile"
(cd /w/mgfork && dotnet build -c Release -p:RgProfile=0 -o /w/mgfork/out 2>&1 | grep -E " error |rror\(s\)" | sort -u)
(cd /w/xtile-src && dotnet build -c Release -o /w/xtile-src/out 2>&1 | grep -E " error |rror\(s\)" | sort -u)
cp /w/mgfork/out/MonoGame.Framework.dll /w/xtile-src/out/xTile.dll /w/libs/

say "patcher"
(cd /w/patcher && dotnet build -c Release -o /w/patcher/out 2>&1 | grep -E " error " || true)
P="dotnet /w/patcher/out/Patcher.dll"
$P fixsteam /game/Steamworks.NET.dll /w/libs/Steamworks.NET.dll | tail -1
(cd /w/patches && dotnet build -c Release -o /w/patches/out 2>&1 | grep -E " error |rror\(s\)" || true)
cp /w/patches/out/SdvAotPatches.dll /w/libs/
$P redirect "/game/Stardew Valley.dll" /w/libs/SdvAotPatches.dll "/w/libs/Stardew Valley.dll" | grep -cE "redirected|prefixed|viewport fixed" | sed 's/^/patched methods: /'

say "generating the XML serializers from your game assembly"
(cd /w/xmlgen && dotnet build -c Release -o out 2>&1 | grep -E " error " || true)
mkdir -p /w/xmlgen/gen && (cd /w/xmlgen && dotnet out/XmlGen.dll /w/xmlgen/gen | tail -1)
(cd /w/xmlser && dotnet build -c Release -o /w/xmlser/out 2>&1 | grep -E " error |rror\(s\)" || true)
cp /w/xmlser/out/SdvXmlSerializers.dll /w/libs/
$P xmlnew "/w/libs/Stardew Valley.dll" /w/libs/SdvXmlSerializers.dll "/w/libs/Stardew Valley.dll" | tail -1
$P netslim "/w/libs/Stardew Valley.dll" /w/libs/SdvAotPatches.dll "/w/libs/Stardew Valley.dll" | tail -1
$P rt16 "/w/libs/Stardew Valley.dll" "/w/libs/Stardew Valley.dll" | tail -1
$P loadclear "/w/libs/Stardew Valley.dll" "/w/libs/Stardew Valley.dll" | tail -1

say "NativeAOT compile for linux-arm (this takes a while)"
cd /w/game
dotnet restore -r linux-arm > /dev/null
PACK=$(ls -d /root/.nuget/packages/microsoft.netcore.app.runtime.nativeaot.linux-arm/10.*/runtimes/linux-arm/lib/net10.0 | head -1)
$P xmlfix $PACK/System.Private.Xml.dll /w/libs/SdvAotPatches.dll /tmp/System.Private.Xml.dll | tail -1
cp /tmp/System.Private.Xml.dll $PACK/System.Private.Xml.dll
dotnet publish --no-restore -c Release -r linux-arm -p:SysRoot=/sysroot -p:SdvRelease=1 -o /w/game/out-linux-arm 2>&1 | grep -E " error " | head || true
cp /w/game/out-linux-arm/StardewAot /w/out/StardewAot
file /w/out/StardewAot
