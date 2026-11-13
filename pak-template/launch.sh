#!/bin/sh
# Stardew Valley 1.6.8 - NativeAOT + GLES2 port for the RG35XX.
# Needs Content/ from the Steam game in this folder (CJK/Russian fonts may be stripped; XACT banks included).
GAMEDIR="$(cd "$(dirname "$0")" >/dev/null 2>&1 && pwd)"
cd "$GAMEDIR"

## buttons by label: A (right) = check/confirm, B (bottom) = cancel/inventory, Y (left) = use tool, X (top) = crafting
export SDL_GAMECONTROLLERCONFIG="$(cat "$GAMEDIR/gamecontroller.txt")"

## saves, settings and error logs inside the pak: $GAMEDIR/StardewValley/Saves (PC saves can be copied in as they are)
export XDG_CONFIG_HOME="$GAMEDIR"
export XDG_DATA_HOME="$GAMEDIR"

## CPU: MinUI's game speed (1.2 GHz); the port was tested at 1.2 GHz
overclock.elf $CPU_SPEED_GAME

"$GAMEDIR"/StardewAot
