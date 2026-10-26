# 0. Setup

Everything in this tutorial runs in Docker: the broken lab apps, the load generator, dump
collection, and dump analysis. You do **not** need the .NET SDK on your machine.

## Prerequisites

| | Minimum |
|---|---|
| Docker | Docker Desktop (Windows/macOS) or Docker Engine + Compose v2 (Linux) |
| RAM available to Docker | 4 GB (8 GB recommended) |
| Free disk | ~10 GB - images ~3 GB, and each dump is 150-700 MB |
| CPU | x64 (the labs were validated on linux/amd64) |
| Optional | `curl`, a terminal; the .NET 10 SDK only if you want to [analyze on your host](#optional-analyze-on-your-host) |

## 1. Clone and build

```bash
git clone https://github.com/haison8x/net-dump-tutorials.git
cd net-dump-tutorials
docker compose --profile manual build
```

!!! warning "Linux hosts: make `dumps/` writable"
    The lab apps run as the non-root user of the .NET images (UID 1654) and write dumps into the
    bind-mounted `./dumps` folder. On a Linux host that folder belongs to you, so allow others to
    write to it (Docker Desktop on Windows/macOS does not need this):
    ```bash
    chmod a+rwx dumps
    ```

This builds five images:

| Image | What it is |
|---|---|
| `dumplab/memoryleak` | [Lab 1](04-lab-memory-leak.md) - `labs/MemoryLeak.Api`, port **5100** |
| `dumplab/deadlock` | [Lab 2](05-lab-deadlock.md) - `labs/Deadlock.Api`, port **5200** |
| `dumplab/crash` | [Lab 3](06-lab-crash.md) - `labs/Crash.Console` |
| `dumplab/loadgen` | `labs/LoadGen` - a tiny HTTP load generator |
| `dumplab/tools` | the **analysis workstation**: `dotnet-dump`, `dotnet-gcdump`, `dotnet-counters`, `dotnet-trace`, [DumpAnalyzer](08-automation-clrmd-dumplinq.md), the [queries](08-automation-clrmd-dumplinq.md#ad-hoc-queries), and the lab PDBs in `/symbols` |

The lab app images also contain `dotnet-dump`, `dotnet-counters`, `dotnet-gcdump` and
`dotnet-trace`, so you can collect diagnostics from *inside* a running container
([Chapter 2](02-collecting-dumps.md)).

## 2. Start the lab APIs

```bash
docker compose up -d memoryleak deadlock
curl http://localhost:5100/
curl http://localhost:5200/
```

```text
MemoryLeak.Api - Lab 1
  GET /leak/event?fixed=false   static event handler leak
  GET /leak/cache?fixed=false   unbounded static cache
  GET /leak/timer?fixed=false   undisposed System.Threading.Timer
  GET /leak/loh?fixed=false     Large Object Heap fragmentation
  GET /stats                    managed memory statistics
  GET /gc                       force a full blocking GC
```

## 3. Check the tools

```bash
docker compose run --rm tools dotnet-dump --version
docker compose exec memoryleak dotnet-dump ps
```

```text
10.0.750501+ad724c4b85e7ae775e42f393c6e95a7557a57954
 1  dotnet  /usr/share/dotnet/dotnet  dotnet MemoryLeak.Api.dll
```

The app is **PID 1** inside its container - you will use `-p 1` a lot.

## The three ways you will run commands

```bash
# 1. Inside a lab container (collect dumps, watch counters) - the app is PID 1
docker compose exec memoryleak dotnet-dump collect -p 1 -o /dumps/my.dmp

# 2. The load generator, talking to the APIs over the compose network
docker compose run --rm loadgen -c 10 -r 200 -d 20 http://memoryleak:8080/leak/event

# 3. The analysis workstation - every dump is in /dumps
docker compose run --rm tools dotnet-dump analyze /dumps/my.dmp
```

`./dumps` on your machine is mounted as `/dumps` in **every** container, so a dump collected in a
lab container is immediately visible to the `tools` container (and to your host).

!!! tip "LoadGen options"
    `-c` concurrency (default 20), `-d` duration in seconds (default 30, `0` = until Ctrl+C),
    `-r` max requests/second in total (default: unlimited), `-t` request timeout in seconds
    (default 5). Several URLs are hit round-robin. It prints one status line per second:
    ```text
    [00:19] ok=   3991 failed=    0 timeout=    0 in-flight=   0 rps=  180 avg=    2ms
    ```

!!! warning "Git Bash on Windows: disable path conversion"
    Git Bash (MSYS) rewrites arguments that look like Unix paths, so `/dumps/my.dmp` silently
    becomes `C:/Program Files/Git/dumps/my.dmp`. Run this once per shell:
    ```bash
    export MSYS_NO_PATHCONV=1
    ```
    PowerShell, cmd, WSL, macOS and Linux shells are not affected.

## Helper script

`scripts/collect-dump.sh` / `scripts/collect-dump.ps1` wrap the collect command:

```bash
./scripts/collect-dump.sh memoryleak leak-event-1          # -> ./dumps/leak-event-1.dmp
./scripts/collect-dump.sh deadlock hang Heap               # Heap instead of Full
```
```powershell
./scripts/collect-dump.ps1 memoryleak leak-event-1
```

## Optional: analyze on your host

You can also install the tools on your machine and open the same files from `./dumps`:

```bash
dotnet tool install -g dotnet-dump
dotnet-dump analyze ./dumps/leak-event-2.dmp
```

This works even on Windows for the Linux dumps produced by the labs (verified with
`dotnet-dump` 10.0.750501).

## Cleaning up

```bash
docker compose down                     # stop the labs
rm -f dumps/*.dmp                       # dumps are big - delete them when done
```

Next: [Dump fundamentals](01-dump-fundamentals.md).
