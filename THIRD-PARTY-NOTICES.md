# Third-party components

| Component | Licence | How MiniValley uses it |
|---|---|---|
| Stardew Valley 1.6.8 | proprietary © ConcernedApe | Used from your own installation |
| MonoGame 3.8 (Stardew's fork, `MonoGame.Framework.dll`) | Microsoft Public License (MonoGame) | decompiled from your copy at build time; we ship only our diff (`forks/monogame`) |
| xTile (`xTile.dll`) | proprietary | decompiled from your copy at build time; we ship only our diff (`forks/xtile`) |
| FAudio 21.07 | zlib | `prebuilt/libFAudio.so`, unmodified |
| Mono.Cecil 0.11.6 | MIT | NuGet package used by the patcher |
| ILSpy / ilspycmd 11.2 | MIT | decompiler used |
| .NET 10 SDK and NativeAOT runtime | MIT | build image (`mcr.microsoft.com/dotnet/sdk:10.0`) |
| NVorbis, StbImageSharp, StbImageWriteSharp | MIT / public domain | part of the MonoGame fork, from your copy |
| SDL2, glibc, libstdc++ | zlib / LGPL / GPL with runtime exception | from the device firmware and toolchain sysroot; not shipped |
