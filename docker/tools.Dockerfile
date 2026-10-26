# The "analysis workstation" image: everything you need to analyze dumps, so the
# only thing you have to install on your machine is Docker.
#
#   docker compose run --rm tools dotnet-dump analyze /dumps/leak-event-2.dmp
#   docker compose run --rm tools dumpanalyzer diff /dumps/leak-event-1.dmp /dumps/leak-event-2.dmp
#   docker compose run --rm tools dotnet run /queries/leaked-subscribers.cs -- /dumps/leak-event-2.dmp
#
# It uses the same .NET 10 runtime version as the lab images, so the DAC
# (libmscordaccore.so) that SOS/ClrMD need matches the dumps.

FROM mcr.microsoft.com/dotnet/sdk:10.0

ENV PATH="${PATH}:/tools" \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

RUN dotnet tool install dotnet-dump --tool-path /tools \
 && dotnet tool install dotnet-counters --tool-path /tools \
 && dotnet tool install dotnet-gcdump --tool-path /tools \
 && dotnet tool install dotnet-trace --tool-path /tools

# Symbols (PDBs) of the lab apps. SOS needs them to show source file/line numbers,
# exactly like you need the PDBs of YOUR build in real life. The lab images are
# built from the same sources at the same path (/src/labs) - .NET builds are
# deterministic, so these PDBs match the binaries inside the dumps.
#   (in dotnet-dump)  setsymbolserver -directory /symbols
COPY labs/ /src/labs/
RUN for app in MemoryLeak.Api Deadlock.Api Crash.Console LoadGen; do \
      dotnet publish /src/labs/$app/$app.csproj -c Release -o /tmp/publish/$app || exit 1; \
    done \
 && mkdir -p /symbols && cp /tmp/publish/*/*.pdb /symbols/ && rm -rf /tmp/publish

# DumpAnalyzer (ClrMD) - tools/DumpAnalyzer
COPY tools/Directory.Build.props /src/tools/
COPY tools/DumpAnalyzer/ /src/tools/DumpAnalyzer/
RUN dotnet publish /src/tools/DumpAnalyzer -c Release -o /opt/dumpanalyzer \
 && printf '#!/bin/sh\nexec dotnet /opt/dumpanalyzer/DumpAnalyzer.dll "$@"\n' > /tools/dumpanalyzer \
 && chmod +x /tools/dumpanalyzer

# DumpLinq queries - tools/queries (file-based C# apps). Build once so the
# NuGet packages are cached in the image and `dotnet run` starts quickly.
COPY tools/queries/ /queries/
RUN for f in /queries/*.cs; do dotnet build "$f" > /dev/null || exit 1; done

WORKDIR /dumps
