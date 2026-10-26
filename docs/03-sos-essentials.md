# 3. SOS essentials

SOS is the debugger extension that understands .NET. `dotnet-dump analyze` is SOS in a small
standalone REPL; the same commands also work in [lldb](07-linux-docker.md#lldb-sos).

## Opening a dump

```bash
docker compose run --rm tools dotnet-dump analyze /dumps/leak-event-2.dmp
```
```text
Loading core dump: /dumps/leak-event-2.dmp ...
Ready to process analysis commands. Type 'help' to list available commands or 'help [command]' to get detailed help on a command.
Type 'quit' or 'exit' to exit the session.
>
```

Non-interactively, pass commands with `-c` (handy for scripts and for saving output):

```bash
docker compose run --rm tools dotnet-dump analyze /dumps/leak-event-2.dmp \
    -c "dumpheap -stat" -c "exit" > dumps/heapstat.txt
```

!!! tip
    `help` lists every command, `help <command>` shows its options. Commands are
    case-insensitive and most have short aliases (`do` = `dumpobj`, `pe` = `printexception`).

## The commands you will actually use

### Overview

| Command | What it shows |
|---|---|
| `clrthreads` | managed threads, their state, held lock count, and **the exception each thread is throwing** |
| `threadpool` | worker counts (running/idle/min/max) and CPU usage - starvation triage |
| `eeheap -gc` | GC heap segments per generation, LOH, POH, totals |
| `gcheapstat` | size and free space per generation - fragmentation at a glance |
| `dumpheap -stat` | **objects grouped by type**, sorted by total size - the starting point of every leak hunt |

### Objects

| Command | What it shows |
|---|---|
| `dumpheap -type <name>` | all objects whose type name *contains* `<name>` (`-short`: addresses only) |
| `dumpheap -mt <MT>` | all objects of one exact MethodTable |
| `dumpheap -stat -min 85000` | only large objects (LOH) |
| `dumpobj <addr>` (`do`) | an object's type and **field values** |
| `dumparray <addr>` | elements of an array |
| `dumpvc <MT> <addr>` | a value type (struct) stored inline |
| `dumpconcurrentdictionary <addr>` (`dcd`) | contents of a `ConcurrentDictionary` |
| `name2ee <module>!<type>` | find a type's MethodTable by name |
| `dumpclass <MT/EEClass>` | type info **including the values of its static fields** |

### Why is it alive?

| Command | What it shows |
|---|---|
| `gcroot <addr>` | root paths that keep the object alive (stacks, handles, finalizer queue) |
| `pathto <source> <target>` | the reference path between two objects |
| `gchandles -stat` | GC handles by type |
| `finalizequeue` | objects waiting for finalization |

### Threads and stacks

| Command | What it shows |
|---|---|
| `setthread <n>` / `setthread -t <os-id>` | switch the current thread (DBG index, or OS id **in decimal**) |
| `clrstack` | managed stack of the current thread; `-a` adds arguments/locals; `-all` = every thread |
| `pstacks` | **merged** stacks of all threads, like Visual Studio's *Parallel Stacks* - best overview for hangs |
| `dumpstackobjects` (`dso`) | objects referenced from the current thread's stack |

### Locks, async, timers

| Command | What it shows |
|---|---|
| `syncblk` | `Monitor` locks (`lock` statements) currently held: owner thread and waiters |
| `dumpasync` | async "stacks" reconstructed from the heap (`--coalesce`, `--fields`, `--type`) |
| `threadpoolqueue` (`tpq`) | work items queued in the thread pool |
| `timerinfo` (`ti`) | all active timers, their period and callback |

### Exceptions and crashes

| Command | What it shows |
|---|---|
| `pe` (`printexception`) | the current thread's exception, or `pe <addr>` for any exception object |
| `dumpexceptions` | all exception objects on the heap |
| `crashinfo` | crash details recorded in the dump, if any |

### Symbols

| Command | What it does |
|---|---|
| `setsymbolserver -directory <path>` | look for PDBs in a local folder (the `tools` image: `/symbols`) |
| `setsymbolserver -ms` | use Microsoft's public symbol server |
| `setclrpath <path>` | where to load the DAC from, if it is not found automatically |

## Reading `dumpheap -stat`

```text
              MT    Count    TotalSize Class Name
...
71839d8c76c0    3,991      191,568 MemoryLeak.Api.Scenarios.PriceAlertSubscriber
71839d6cf0c0    3,992      255,488 System.EventHandler<System.Decimal>
718399efdd58    5,894      559,246 System.String
71839ab73d00    4,305   65,564,928 System.Byte[]
Total 29,879 objects, 67,677,748 bytes
```

- **MT** (MethodTable) identifies the exact type - use it with `dumpheap -mt`.
- **TotalSize** is the *shallow* size: the objects themselves, not what they reference. The
  `byte[]` above are "big" but the interesting line is the **count** of `PriceAlertSubscriber`,
  which owns them.
- Sorting is by TotalSize, so primitive arrays and strings almost always top the list. **Look for
  your own types** and for suspiciously round or growing counts.
- `dumpheap` includes **dead objects** that have not been collected yet. Either force a GC before
  collecting (the labs have a `/gc` endpoint) or compare two dumps.

## Caveats learned the hard way

!!! warning "`gcroot` can miss static-field roots on .NET 10"
    In our tests (dotnet-dump 10.0.750501, .NET 10 on Linux and Windows), `gcroot` printed
    `Found 0 unique roots.` for objects that were alive only because of a **static field** - and
    `dumpheap -live` treated them as dead. In .NET 10 static fields are stored in an array on the
    pinned heap that SOS's root search does not report. Workaround in
    [Lab 1](04-lab-memory-leak.md#when-gcroot-finds-nothing-static-fields): read the static with
    `dumpclass`, then `pathto <static value> <object>` - or use DumpAnalyzer `roots`.

!!! warning "`crashinfo` stops a `-c` chain"
    When a dump has no crash info, `crashinfo` prints `No crash info to display` and the following
    `-c` commands are not executed. Run it separately.

!!! note "Addresses are per dump"
    Object addresses, MethodTables and thread numbers differ between dumps - and between your
    machine and this tutorial. Always copy them from *your* output.

Next: [Lab 1 - Memory leaks](04-lab-memory-leak.md).
