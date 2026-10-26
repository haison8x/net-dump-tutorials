# Builds one of the lab apps (labs/<APP>) into a Linux image that already contains
# the .NET diagnostic tools, so you can collect dumps from INSIDE the container:
#
#   docker compose exec memoryleak dotnet-dump collect -p 1 -o /dumps/my.dmp
#
# Used by docker-compose.yml for: MemoryLeak.Api, Deadlock.Api, Crash.Console, LoadGen.

ARG APP=MemoryLeak.Api

# ---------- build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG APP
WORKDIR /src
COPY labs/ labs/
RUN dotnet publish labs/${APP}/${APP}.csproj -c Release -o /app

# The diagnostic tools are framework-dependent, so the runtime image can run them.
RUN dotnet tool install dotnet-dump --tool-path /tools \
 && dotnet tool install dotnet-counters --tool-path /tools \
 && dotnet tool install dotnet-gcdump --tool-path /tools \
 && dotnet tool install dotnet-trace --tool-path /tools

# ---------- runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG APP
ENV APP_DLL=${APP}.dll \
    PATH="${PATH}:/tools" \
    # Write a dump automatically if the process crashes (docs/02-collecting-dumps.md).
    DOTNET_DbgEnableMiniDump=1 \
    DOTNET_DbgMiniDumpType=4 \
    DOTNET_DbgMiniDumpName=/dumps/crash_%e_%p_%t.dmp \
    # Linux only: also write <dump>.crashreport.json with the faulting stacks.
    DOTNET_EnableCrashReport=1

WORKDIR /app
COPY --from=build /app .
COPY --from=build /tools /tools

# /dumps is a mounted volume (./dumps on your machine) so dumps outlive the container.
RUN mkdir -p /dumps && chown $APP_UID /dumps

# Run as the non-root user that ships with the .NET images. dotnet-dump must run
# as the SAME user as the target process - `docker compose exec` uses this USER too.
USER $APP_UID
EXPOSE 8080

# `exec` makes the app PID 1 (so `dotnet-dump collect -p 1` works) and passes
# container arguments through to the app (Crash.Console scenarios, LoadGen options).
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\" \"$@\"", "--"]
