# 9. Automation with ClrMD and DumpLinq

Typing SOS commands is fine for one incident. When you analyze dumps regularly - or want a CI job
to say *"this crash dump has a deadlock"* - you write code against the dump instead.

## ClrMD

[ClrMD](https://github.com/microsoft/clrmd) (`Microsoft.Diagnostics.Runtime`) is the library
Microsoft's own tools are built on. It opens a dump (or attaches to a live process) and gives you
the runtime's view as .NET objects: heap, objects, types, fields, threads, stacks, roots, locks.

```csharp
#:package Microsoft.Diagnostics.Runtime@4.2.751201
using Microsoft.Diagnostics.Runtime;

using var target  = DataTarget.LoadDump("/dumps/leak-cache-2.dmp");
using var runtime = target.ClrVersions[0].CreateRuntime();   // finds the matching DAC
ClrHeap heap = runtime.Heap;

// dumpheap -stat, in LINQ
var top = heap.EnumerateObjects()
    .Where(o => o.Type is not null)
    .GroupBy(o => o.Type!.Name)
    .Select(g => (Type: g.Key, Count: g.Count(), Size: g.Sum(o => (long)o.Size)))
    .OrderByDescending(x => x.Size)
    .Take(10);
```

| You need | ClrMD |
|---|---|
| all objects | `heap.EnumerateObjects()` → `ClrObject` (`Address`, `Type`, `Size`) |
| a field value | `obj.ReadField<int>("m_currentCount")`, `obj.ReadObjectField("_next")`, `obj.ReadStringField("_name")` |
| a static field | `heap.GetTypeByName("Ns.Type")!.GetStaticFieldByName("_cache")!.ReadObject(runtime.AppDomains[0])` |
| roots | `heap.EnumerateRoots()`, `new GCRoot(heap, [address]).EnumerateRootPaths()` / `.FindPathFrom(obj)` |
| threads and stacks | `runtime.Threads`, `thread.EnumerateStackTrace()`, `thread.EnumerateStackRoots()` |
| `lock` owners | `heap.EnumerateSyncBlocks()` (`Object`, `HoldingThreadAddress`, `WaitingThreadCount`) |
| thread pool | `runtime.ThreadPool` |

.NET 10's *file-based apps* make ClrMD perfect for ad-hoc scripts: a single `.cs` file with a `#:package` line, run with
`dotnet run file.cs` - no project needed.

## DumpAnalyzer

[`tools/DumpAnalyzer`](https://github.com/haison8x/net-dump-tutorials/tree/main/tools/DumpAnalyzer)
packages the labs' manual workflows into four commands (available as `dumpanalyzer` in the tools image):

```text
stat  <dump> [top]                       heap statistics by type (like dumpheap -stat)
diff  <before.dmp> <after.dmp> [top]     which types grew between two dumps
roots <dump> <type-name-suffix> [n]      root paths of n sample instances (includes static roots)
hang  <dump>                             Monitor deadlocks, SemaphoreSlim waiters, blocked threads
```

### `diff` - what grows ([HeapStats.cs](https://github.com/haison8x/net-dump-tutorials/blob/main/tools/DumpAnalyzer/HeapStats.cs))

```bash
docker compose run --rm tools dumpanalyzer diff /dumps/leak-event-1.dmp /dumps/leak-event-2.dmp 5
```
```text
      +Count           +Bytes    Count now  Type
      +3,891      +65,133,210        4,305  System.Byte[]
      +3,127         +330,446        5,894  System.String
      +3,952         +252,928        3,992  System.EventHandler<System.Decimal>
      +3,971         +190,608        3,991  MemoryLeak.Api.Scenarios.PriceAlertSubscriber
         -19          +31,312            1  System.Delegate[]
```

### `roots` - who holds it ([RootFinder.cs](https://github.com/haison8x/net-dump-tutorials/blob/main/tools/DumpAnalyzer/RootFinder.cs))

```bash
docker compose run --rm tools dumpanalyzer roots /dumps/leak-cache-2.dmp CustomerReport
```
```text
4,020 instance(s) of *CustomerReport on the heap (live + dead).

Root path for 00007cf86f96eaf8 MemoryLeak.Api.Scenarios.CustomerReport:
  ROOT: static field MemoryLeak.Api.Scenarios.ReportCache._reports (found by walking from static fields)
    -> 00007cf86c0b82b0 System.Collections.Concurrent.ConcurrentDictionary<System.Guid, MemoryLeak.Api.Scenarios.CustomerReport>
    -> 00007cf86ea80318 System.Collections.Concurrent.ConcurrentDictionary<...>+Tables
    -> 00007cf878000048 System.Collections.Concurrent.ConcurrentDictionary<...>+VolatileNode[]
    -> 00007cf86ea937f8 System.Collections.Concurrent.ConcurrentDictionary<...>+Node
    -> 00007cf86ea8a0d0 System.Collections.Concurrent.ConcurrentDictionary<...>+Node
    -> 00007cf86f96eaf8 MemoryLeak.Api.Scenarios.CustomerReport
```

How it works - and how it deals with the static-root gap of SOS:

1. `GCRoot.EnumerateRootPaths()` - ClrMD's equivalent of `gcroot`. Depending on the DAC, ClrMD
   reports static fields as a `StaticVar` root kind (we saw this on Windows dumps), or not at all
   (Linux dumps).
2. If no path is found, it builds a map of **every initialized static reference field** (all types
   in all modules, `type.StaticFields` + `field.ReadObject(domain)`) and calls
   `GCRoot.FindPathFrom(staticValue)` on each, application types first - the automated
   `dumpclass` + `pathto` from [Lab 1](04-lab-memory-leak.md#when-gcroot-finds-nothing-static-fields).
3. Long runs of the same type (linked lists like `TimerQueueTimer`) are collapsed.

### `hang` - deadlocks and starvation ([HangAnalyzer.cs](https://github.com/haison8x/net-dump-tutorials/blob/main/tools/DumpAnalyzer/HangAnalyzer.cs))

- **Monitor deadlocks**: `EnumerateSyncBlocks()` gives each held lock and its owner. A thread whose
  stack is in `Monitor.Enter` and has a held lock object among its **stack roots** is waiting for that
  lock. That gives a wait-for graph; a cycle is a deadlock.
- **SemaphoreSlim**: every `SemaphoreSlim` with `m_currentCount == 0` and a non-empty
  `m_asyncHead` list.
- **Sync-over-async**: threads with `Task.Wait`/`GetResult` frames, grouped by the first frame
  of *your* code, plus thread pool counts.

```text
== Monitor locks (lock statements) ==
  System.Object @ 0000745cb497c348 owned by thread 12 (OS 0x27), 6 waiter(s)
  System.Object @ 0000745cb498f3c0 owned by thread 9 (OS 0x23), 6 waiter(s)
  thread 7 waits for 0000745cb497c348 (owned by thread 12) in Deadlock.Api.Scenarios.OrderService.ShipOrder(Int32)
  thread 9 waits for 0000745cb497c348 (owned by thread 12) in Deadlock.Api.Scenarios.OrderService.NotifyBackorders(Int32)
  thread 12 waits for 0000745cb498f3c0 (owned by thread 9) in Deadlock.Api.Scenarios.InventoryService.ReserveStock(Int32)
  ...
  *** DEADLOCK: thread 9 -> thread 12 -> thread 9
```

## Ad-hoc queries

[`tools/queries`](https://github.com/haison8x/net-dump-tutorials/tree/main/tools/queries) contains
single-file queries you can copy and adapt. They are in the tools image under `/queries`:

| Query | Lab |
|---|---|
| `clrmd-cached-reports.cs` | Lab 1, cache leak: count, total size, distinct keys, the static that holds them |
| `clrmd-stuck-semaphores.cs` | Lab 2, semaphores with no permit and **who** awaits them |
| `dumplinq-leaked-subscribers.cs` | Lab 1, event leak - written with DumpLinq |

```bash
docker compose run --rm tools dotnet run /queries/clrmd-stuck-semaphores.cs -- /dumps/deadlock-semaphore.dmp
```
```text
SemaphoreSlim 000078d4b89852b8: currentCount=0, 1 async waiter(s)
      1 x System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.String>+AsyncStateMachineBox<Deadlock.Api.Scenarios.ConfigService+<GetSettingAsync>d__3>
SemaphoreSlim 000078d4b898e370: currentCount=0, 8 async waiter(s)
      8 x System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.String>+AsyncStateMachineBox<Deadlock.Api.Scenarios.TokenService+<GetTokenAsync>d__3>
```

The interesting line in that query is how it finds *who* waits: each async waiter is a
`SemaphoreSlim+TaskNode`, and its `m_continuationObject` is the state machine box of the method that
awaited `WaitAsync()`:

```csharp
foreach (var group in x.Waiters.GroupBy(w => w.ReadObjectField("m_continuationObject").Type?.Name ?? "?"))
    Console.WriteLine($"    {group.Count(),3} x {group.Key}");
```

To write your own: copy a file into `./dumps` (it is mounted into the tools container) and run it:

```bash
cp tools/queries/clrmd-cached-reports.cs dumps/my-query.cs
docker compose run --rm tools dotnet run /dumps/my-query.cs -- /dumps/leak-cache-2.dmp
```

## DumpLinq

[DumpLinq](https://github.com/anderspedersen/DumpLinq) (NuGet `DumpLinq`, by Anders Pedersen -
see his [article](https://medium.com/@anders_pedersen/net-memory-dump-analysis-with-dumplinq-8a7a73f1c136))
is a thin, friendly layer over ClrMD for exactly this kind of query:

```csharp
#:package DumpLinq@0.1.3
using DumpLinq;

using var dump = Dump.Open(args[0]);

var subscribers = dump.EnumerateHeapObjects("*.PriceAlertSubscriber").ToList();   // wildcards
foreach (var s in subscribers.Take(3))
    Console.WriteLine($"{s.Address:x16} user={s.GetField("_userId").AsString().Value}");

Console.WriteLine(subscribers[^1].ToString(depth: 2, arrayItems: 3));          // tree view
```
```text
PriceAlertSubscriber instances: 3,991
  00007143a40cae98 user=user-20834a7885b74128a25ed859f319fdce
  ...
Largest EventHandler<decimal>: 00007143a740d450 with 3,991 subscribers

One subscriber, as a tree:
MemoryLeak.Api.Scenarios.PriceAlertSubscriber @ 0x00007143AD7F2350
  _history: System.Byte[] @ 0x00007143ACC04340
    [0]: 196
    [1]: 0
    [2]: 0
  _userId: "user-01dbb7162e0c46839fd2ab591bb2e0b3"
  _lastPrice: System.Decimal @ 0x00007143AD7F2368
    _flags: 0
    _hi32: 0
    _lo64: 196
```

API in a nutshell: `Dump.Open(path)`, `EnumerateHeapObjects(pattern, liveOnly = false)`,
`GetField(name)` (every value - even an `int` - is a `DumpObject`), `EnumerateArrayItems()`,
`AsString()`, `ReadAs<T>()` for unmanaged values, `ToString(depth, arrayItems)`. The companion
package `DumpLinq.LINQPad` adds `ToDump()` for LINQPad's interactive object viewer.

!!! warning "Verify DumpLinq results on .NET 10 dumps"
    DumpLinq 0.1.3 is built on **ClrMD 3.1** and cannot run on ClrMD 4.x (`MissingMethodException`).
    On a .NET 10 Linux dump from Lab 2, ClrMD 3.1 enumerated **24,044** objects where ClrMD 4.2 found
    **27,450** - and a query for `TokenService` returned nothing although the object existed. On the
    Lab 1 dumps its counts were correct. Use DumpLinq for exploration, but cross-check important
    numbers with `dumpheap -stat` or a ClrMD 4.x query.

## Ideas for your own automation

- **Crash triage in CI/CD or on the node**: when a `crash_*.dmp` appears, run a small ClrMD tool
  that prints the crashing thread's exception and stack (`thread.CurrentException`) and posts it to
  your chat/incident tool together with the `.crashreport.json`.
- **Leak regression test**: run a load test, collect two dumps, `diff`, and fail the build when one
  of *your* types grows by more than N instances.
- **Hang watchdog**: when health checks fail, collect a dump (e.g. via `dotnet-monitor`) and run
  `hang` on it before restarting the instance - you keep the evidence.

Next: [Troubleshooting and FAQ](10-troubleshooting-faq.md).
