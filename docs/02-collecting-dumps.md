# 2. Collecting dumps

There are two situations:

1. **The process is alive** but misbehaving (high memory, hanging, slow) - you collect a dump
   **on demand**.
2. **The process crashes** - nobody is there to collect anything, so you configure the runtime
   in advance to **write a dump when it dies**.

## On demand: `dotnet-dump collect`

```bash
docker compose exec memoryleak dotnet-dump collect -p 1 -o /dumps/leak-event-1.dmp
```
```text
Writing full to /dumps/leak-event-1.dmp
Complete
```

| Option | Meaning |
|---|---|
| `-p, --process-id` | the target PID (`1` in the lab containers; `dotnet-dump ps` lists .NET processes) |
| `-n, --name` | target by process name instead of PID |
| `-o, --output` | output file - **a path inside the target's container** |
| `--type` | `Full` (default), `Heap`, `Mini`, `Triage` - see [dump types](01-dump-fundamentals.md#dump-types) |
| `--diag` | log diagnostics of the dump collection itself |

How it works: `dotnet-dump` talks to the runtime through the **diagnostic IPC channel** (a Unix
domain socket in `/tmp`, or a named pipe on Windows). The runtime then launches its own
`createdump` helper, which reads the process memory and writes the file. That is why:

- `dotnet-dump` must run as the **same user** as the target process (or with `CAP_SYS_PTRACE`);
- the output path is resolved by the **target** - `/dumps/...` here is the target container's `/dumps`;
- in containers you need either `docker exec` into the app container, or a sidecar that shares
  the app's PID namespace and `/tmp` ([Chapter 7](07-linux-docker.md)).

!!! danger "Collecting a dump freezes the process"
    While the dump is written, the process is suspended. In Lab 2 a 600 MB dump froze the API for
    over 10 seconds: every in-flight request timed out. On a production server:

    - take the instance out of the load balancer first if you can;
    - make sure there is disk space for a file as large as the process's memory;
    - prefer `--type Heap` if you only need managed analysis.

### How many dumps, and when?

| Problem | Collect |
|---|---|
| Memory leak | **two** dumps: one early (baseline) and one after memory has grown. Comparing them shows *what grows*. |
| Hang / deadlock | one dump while it hangs - optionally 2-3 dumps a few seconds apart: stacks that never change = stuck; stacks that move = just slow. |
| High CPU | a dump shows only one instant; prefer `dotnet-trace`. Several dumps can hint at hot stacks. |
| Crash | dump-on-crash (below). |

## On crash: environment variables

The runtime can write a dump automatically when the process crashes (unhandled exception,
stack overflow, fatal error). It is configured with environment variables
([MS docs](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/collect-dumps-crash)):

| Variable | Meaning | Default |
|---|---|---|
| `DOTNET_DbgEnableMiniDump` | `1` = write a dump on crash | `0` |
| `DOTNET_DbgMiniDumpType` | `1` Mini, `2` Heap, `3` Triage, `4` Full | `2` |
| `DOTNET_DbgMiniDumpName` | output path, may contain placeholders | `/tmp/coredump.<pid>` |
| `DOTNET_CreateDumpDiagnostics` | `1` = log what createdump does | `0` |
| `DOTNET_CreateDumpVerboseDiagnostics` | `1` = verbose createdump logging | `0` |
| `DOTNET_CreateDumpLogToFile` | write those diagnostics to a file instead of the console | |
| `DOTNET_EnableCrashReport` | `1` = also write `<dump>.crashreport.json` (**not on Windows**) | |

Placeholders for `DOTNET_DbgMiniDumpName` (.NET 5+):

| | |
|---|---|
| `%p` | PID |
| `%e` | executable file name - note: `dotnet` when started as `dotnet app.dll` |
| `%h` | host name |
| `%t` | time of the dump, seconds since epoch |
| `%%` | a literal `%` |

The lab images set them in [`docker/app.Dockerfile`](https://github.com/haison8x/net-dump-tutorials/blob/main/docker/app.Dockerfile):

```dockerfile
ENV DOTNET_DbgEnableMiniDump=1 \
    DOTNET_DbgMiniDumpType=4 \
    DOTNET_DbgMiniDumpName=/dumps/crash_%e_%p_%t.dmp \
    DOTNET_EnableCrashReport=1
```

When a lab app crashes, the console shows:

```text
[createdump] Gathering state for process 1 dotnet
[createdump] Crashing thread 000e signal 6 (0006)
[createdump] Writing crash report to file /dumps/crash_dotnet_1_1792981570.dmp.crashreport.json
[createdump] Crash report successfully written
[createdump] Writing full dump to file /dumps/crash_dotnet_1_1792981570.dmp
[createdump] Written 168382464 bytes (41109 pages) to core file
[createdump] Target process is alive
[createdump] Dump successfully written in 4073ms
```

!!! tip "Make the dump outlive the container"
    Write crash dumps to a **mounted volume** (`./dumps:/dumps` in the labs). A dump written to the
    container's own filesystem disappears with it.

## Lighter alternatives

### `dotnet-gcdump` - heap graph only

```bash
docker compose exec memoryleak dotnet-gcdump collect -p 1 -o /dumps/leak.gcdump
docker compose run --rm tools dotnet-gcdump report /dumps/leak.gcdump
```

A `.gcdump` contains only the types and references of the managed heap - typically a few MB
instead of hundreds. `dotnet-gcdump report` prints the largest types.
Caveats: collecting it **triggers a full GC**, and it has no thread stacks, no field values, no
locks. Use it for quick leak triage, a real dump for root cause.

!!! note
    On .NET 10 `dotnet-gcdump` prints *"the target runtime does not support non-lossy (Block)
    buffering, which requires .NET 11+ ... the gcdump may be incomplete on large heaps"*.
    Another reason to confirm findings with a real dump.

### `dotnet-counters` - is there a problem at all?

```bash
docker compose exec memoryleak dotnet-counters monitor -p 1 --counters System.Runtime
```

Watch `dotnet.gc.last_collection.heap.size` (heap after the last GC - a leak makes it climb
forever), `dotnet.process.memory.working_set`, `dotnet.thread_pool.thread.count` and
`dotnet.thread_pool.queue.length` (starvation). `monitor` needs an interactive terminal; to record
to a file use `dotnet-counters collect --format csv -o /dumps/counters.csv`.

### `dotnet-monitor` - collection rules

For production, [dotnet-monitor](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-monitor)
runs as a sidecar and can collect dumps automatically based on *collection rules* (for example
"GC heap > 1 GB for 2 minutes" or "request duration > 10 s"), and exposes an HTTP API to collect
on demand. It uses the same diagnostic channel as `dotnet-dump`.

## Windows-native options

If you run on Windows outside containers, you can also use:

- **Task Manager** → Details → right-click process → *Create memory dump file* (full dump);
- **[ProcDump](https://learn.microsoft.com/en-us/sysinternals/downloads/procdump)**: `procdump -ma <pid>`
  for a full dump, `procdump -ma -e <pid>` to wait for an unhandled exception.

Both produce files that `dotnet-dump analyze` can open.

Next: [SOS essentials](03-sos-essentials.md).
