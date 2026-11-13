# MiniValley build image: .NET 10 SDK (NativeAOT) + clang/lld to cross-link for armhf.
FROM mcr.microsoft.com/dotnet/sdk:10.0
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends clang lld llvm zlib1g-dev file git \
 && rm -rf /var/lib/apt/lists/*
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
# ILSpy decompiler, pinned: the fork patches apply to exactly this version's output
RUN DOTNET_ROLL_FORWARD=Major dotnet tool install --tool-path /opt/ilspy ilspycmd --version 11.2.0.10050
