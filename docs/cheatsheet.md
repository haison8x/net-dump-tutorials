# Cheat sheet

## Collect

```bash
# on demand (app is PID 1 in its container)
docker compose exec <svc> dotnet-dump collect -p 1 [--type Heap] -o /dumps/<name>.dmp
# any Linux host / container with the tools
dotnet-dump ps
dotnet-dump collect -p <pid> --type Heap -o /path/on/target/x.dmp
# from another container sharing only the PID namespace
TMPDIR=/proc/<pid>/root/tmp dotnet-dump collect -p <pid> -o /dumps/x.dmp      # same UID as the app
# lightweight heap graph (triggers a full GC)
dotnet-gcdump collect -p <pid> -o x.gcdump
```

```bash
# dump on crash
DOTNET_DbgEnableMiniDump=1
DOTNET_DbgMiniDumpType=2                       # 1 Mini, 2 Heap, 3 Triage, 4 Full
DOTNET_DbgMiniDumpName=/dumps/crash_%h_%p_%t.dmp
DOTNET_EnableCrashReport=1                     # Linux: + .crashreport.json
```

## Open

```bash
docker compose run --rm tools dotnet-dump analyze /dumps/x.dmp
dotnet-dump analyze x.dmp -c "dumpheap -stat" -c exit > out.txt     # scripted
> setsymbolserver -directory /symbols                                # your PDBs
```

## Symptom → commands

| Symptom | Start with | Then |
|---|---|---|
| **Memory grows** | two dumps → `dumpheap -stat` diff (or `dumpanalyzer diff`) | `dumpheap -type X` → `dumpobj` → `gcroot` |
| `gcroot`: 0 roots, object survives GC | `name2ee M!T` → `dumpclass <MT>` (static values) | `pathto <static> <obj>` / `dumpanalyzer roots` |
| Many `Timer*` / callbacks | `timerinfo` | `gcroot` (root: `TimerQueue.TimerThread`) |
| LOH / fragmentation | `gcheapstat`, `eeheap -gc` | `dumpheap -stat -min 85000` |
| **Requests hang** | `pstacks` | see below |
| …threads in `Monitor.Enter` | `syncblk` | `setthread -t <decimal os id>` → `clrstack -a` → `dumpobj` |
| …threads idle, nothing blocked | `dumpasync --coalesce` | `dumpheap -type SemaphoreSlim` → `dumpobj` (`m_currentCount`, `m_asyncHead`) |
| …many threads in `Task.Wait`/`.Result` | `threadpool` | `threadpoolqueue` - starvation |
| **Crash** | `clrthreads` (Exception column) | `setthread <DBG>` → `pe` → `clrstack` |
| `async void` crash | `pe` (exception's own stack) | `clrstack` only shows the re-throw |
| Stack overflow | `clrstack` (repeating frame) | `dumpstackobjects` |

## Read the output

- `dumpheap -stat`: sorted by **shallow** size; look for **your types** and growing counts, not `byte[]`/`string`.
- `dumpheap` includes **dead** objects - force a GC first or diff two dumps.
- `syncblk` `MonitorHeld` = 1 (owner) + 2 × waiters.
- `dumpasync`: top line = what is awaited; `(n)` = state machine state; `[n]` = count (`--coalesce`).
- `threadpool`: many workers + 0% CPU = blocked threads.
- `clrthreads` `Exception` column = which thread threw.
- Addresses/MTs/thread numbers are **per dump**.

## Root-cause patterns

| You see | Usually |
|---|---|
| your type + `EventHandler<T>` growing together | missing `-=` on a long-lived event |
| `ConcurrentDictionary+Node` / `Dictionary+Entry[]` growing | cache without size limit / eviction |
| `TimerQueueTimer` + `ExecutionContext` + `Activity` growing | timers never disposed (and they capture request context) |
| big `Free` blocks between `byte[]` on the LOH | large buffers allocated per request - use `ArrayPool` |
| two owners each waiting in `Monitor.Enter` | lock-ordering deadlock |
| `SemaphoreSlim` `currentCount=0`, waiter's caller holds it | async re-entrancy |
| `SemaphoreSlim` `currentCount=0`, no owner anywhere | missing `Release()` (no `finally`) |
| all workers in `Task.Wait`, queue full of `TimerQueueTimer`/continuations | sync-over-async starvation |
