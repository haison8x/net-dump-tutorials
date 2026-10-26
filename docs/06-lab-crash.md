# 6. Lab 3 - Crashes

**App:** [`labs/Crash.Console`](https://github.com/haison8x/net-dump-tutorials/blob/main/labs/Crash.Console/Program.cs) - a console app that crashes in three ways.

| Scenario | Command | Crash |
|---|---|---|
| 1 | `unhandled` | unhandled exception on a background `Thread` |
| 2 | `async-void` | exception escaping an `async void` method |
| 3 | `stackoverflow` | infinite recursion - cannot be caught |

A crashed process cannot be asked for a dump. The runtime must be told **in advance** to write one
when it dies - with the environment variables from [Chapter 2](02-collecting-dumps.md#on-crash-environment-variables).
The lab image sets them already:

```dockerfile
ENV DOTNET_DbgEnableMiniDump=1 \
    DOTNET_DbgMiniDumpType=4 \
    DOTNET_DbgMiniDumpName=/dumps/crash_%e_%p_%t.dmp \
    DOTNET_EnableCrashReport=1
```

---

## Scenario 1: unhandled exception on a background thread

```bash
docker compose run --rm crash unhandled
```
```text
Crash.Console PID 1 - scenario 'unhandled'
Unhandled exception. System.InvalidOperationException: Invalid quantity -1 for order 1002 in orders-2026-10.csv
   at CrashLab.OrderImporter.ImportLine(String fileName, String line) in /src/labs/Crash.Console/Program.cs:line 64
   at CrashLab.OrderImporter.ImportBatch(String fileName) in /src/labs/Crash.Console/Program.cs:line 54
   at Program.<>c.<<Main>$>b__0_0() in /src/labs/Crash.Console/Program.cs:line 18
   at System.Threading.Thread.StartCallback()
[createdump] Gathering state for process 1 dotnet
[createdump] Crashing thread 000e signal 6 (0006)
[createdump] Writing crash report to file /dumps/crash_dotnet_1_1792982515.dmp.crashreport.json
[createdump] Crash report successfully written
[createdump] Writing full dump to file /dumps/crash_dotnet_1_1792982515.dmp
[createdump] Written 168386560 bytes (41110 pages) to core file
[createdump] Target process is alive
[createdump] Dump successfully written in 4130ms
```

Here the console shows the exception already - in production that output is often lost
(container replaced, logs rotated), but the dump is safe in the volume.

### Which thread crashed? `clrthreads`

```bash
docker compose run --rm tools dotnet-dump analyze /dumps/crash_dotnet_1_1792982515.dmp
```

```text
> pe
There is no current managed exception on this thread
```

The analyzer starts on the main thread, which did not throw. Find the thread that did - the last
column of `clrthreads` shows the exception each thread is throwing:

```text
> clrthreads
 DBG   ID     OSID ThreadOBJ           State GC Mode     GC Alloc Context                  Domain           Count Apt Exception
...
   8    4        e 00005CD1539C01B0    21020 Cooperative 00007879E34139B0:00007879E3415010 00005CD15393AA70 -00001 Ukn System.InvalidOperationException 00007879e340cc40
```

The thread with **DBG index 8** (OS id `0xe` - the *"Crashing thread 000e"* from createdump) has an
`InvalidOperationException`.

```text
> setthread 8
> pe
Exception object: 00007879e340cc40
Exception type:   System.InvalidOperationException
Message:          Invalid quantity -1 for order 1002 in orders-2026-10.csv
InnerException:   <none>
StackTrace (generated):
    SP               IP               Function
    0000788214725B00 000078819B9C2C2A Crash.Console.dll!CrashLab.OrderImporter.ImportLine(System.String, System.String)+0x16a
    0000788214725B80 000078819B9C2A52 Crash.Console.dll!CrashLab.OrderImporter.ImportBatch(System.String)+0xd2
    0000788214725BF0 000078819B9C2924 Crash.Console.dll!Program+<>c.<<Main>$>b__0_0()+0x44
    0000788214725C10 000078819ABD9735 System.Private.CoreLib.dll!System.Threading.Thread.StartCallback()+0x85

StackTraceString: <none>
HResult: 80131509
```

`dumpexceptions` lists every exception object on the heap - useful to also see exceptions that were
thrown and caught earlier:

```text
> dumpexceptions
         Address      MethodTable Message Name
    7879e3400088     78819b9b08c8 System.OutOfMemoryException
        Message: <null>
    7879e3400100     78819b9b09f0 System.StackOverflowException
        Message: <null>
    7879e3400178     78819b9b0b18 System.ExecutionEngineException
        Message: <null>
    7879e340cc40     78819bc27428 System.InvalidOperationException
        Message: Invalid quantity -1 for order 1002 in orders-2026-10.csv
        StackFrame: CrashLab.OrderImporter.ImportLine(System.String, System.String)

    Total: 4 objects
```

!!! note
    The `OutOfMemoryException`, `StackOverflowException` and `ExecutionEngineException` objects
    exist in **every** .NET process - the runtime pre-allocates them at startup (you cannot
    allocate an exception object when you are out of memory). They do not mean anything happened.

### Symbols: get line numbers

`clrstack` without symbols shows method names only:

```text
> clrstack
OS Thread Id: 0xe (8)
        Child SP               IP Call Site
0000788214724D00 000078821a228913 [SoftwareExceptionFrame: 0000788214724d00]
0000788214725B00 000078819B9C2C2B CrashLab.OrderImporter.ImportLine(System.String, System.String)
0000788214725B80 000078819B9C2A53 CrashLab.OrderImporter.ImportBatch(System.String)
0000788214725BF0 000078819B9C2925 Program+<>c.<<Main>$>b__0_0()
0000788214725C10 000078819ABD9736 System.Threading.Thread.StartCallback()
0000788214725DB0 0000788219f4bb9c [DebuggerU2MCatchHandlerFrame: 0000788214725db0]
```

SOS looks for `Crash.Console.pdb` next to the module path recorded in the dump (`/app/...` inside
the crashed container) - which does not exist in the `tools` container. Point it at the lab PDBs:

```text
> setsymbolserver -directory /symbols
Current symbol store settings:
-> Directory: /symbols
...
> clrstack
OS Thread Id: 0xe (8)
        Child SP               IP Call Site
0000788214724D00 000078821a228913 [SoftwareExceptionFrame: 0000788214724d00]
0000788214725B00 000078819B9C2C2B CrashLab.OrderImporter.ImportLine(System.String, System.String) [/src/labs/Crash.Console/Program.cs @ 64]
0000788214725B80 000078819B9C2A53 CrashLab.OrderImporter.ImportBatch(System.String) [/src/labs/Crash.Console/Program.cs @ 54]
0000788214725BF0 000078819B9C2925 Program+<>c.<<Main>$>b__0_0() [/src/labs/Crash.Console/Program.cs @ 18]
0000788214725C10 000078819ABD9736 System.Threading.Thread.StartCallback()
0000788214725DB0 0000788219f4bb9c [DebuggerU2MCatchHandlerFrame: 0000788214725db0]
```

!!! important "Keep the PDBs of every build you ship"
    PDBs must match the exact binaries in the dump - a rebuild from *changed* source does not match
    (we hit exactly that while writing this lab). Publish PDBs as build artifacts or to a symbol
    server for every release, or ship them next to the DLLs (the default for `dotnet publish`).
    The `tools` image rebuilds the labs from the same source at the same path; .NET builds are
    deterministic, so those PDBs match.

### Root cause

```csharp
var worker = new Thread(() => new OrderImporter().ImportBatch("orders-2026-10.csv"));
...
throw new InvalidOperationException($"Invalid quantity {quantity} for order {parts[0]} in {fileName}");
```

An exception that escapes a thread's start method terminates the **whole process** in .NET. Catch
and handle (or log) exceptions at the top of every thread you start yourself.

---

## Scenario 2: `async void`

```bash
docker compose run --rm crash async-void
```
```text
Unhandled exception. System.TimeoutException: SMTP server did not respond while emailing customer 42
   at CrashLab.NotificationSender.SendWelcomeEmail(Int32 customerId) in /src/labs/Crash.Console/Program.cs:line 77
   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__124_1(Object state)
   at System.Threading.ThreadPoolWorkQueue.Dispatch()
   at System.Threading.PortableThreadPool.WorkerThread.WorkerThreadStart()
   at System.Threading.Thread.StartCallback()
[createdump] Gathering state for process 1 dotnet
[createdump] Crashing thread 0011 signal 6 (0006)
...
```

```text
> clrthreads
 DBG   ID     OSID ThreadOBJ           State GC Mode     GC Alloc Context                  Domain           Count Apt Exception
...
  11    7       11 0000744920001AA0  1021220 Cooperative 000074494B41D470:000074494B41F0B0 00005D242C254A70 -00001 Ukn (Threadpool Worker) System.TimeoutException 000074494b40e9a8
> setthread 11
> pe
Exception object: 000074494b40e9a8
Exception type:   System.TimeoutException
Message:          SMTP server did not respond while emailing customer 42
InnerException:   <none>
StackTrace (generated):
    SP               IP               Function
    000074517D255800 0000745104D22B9B Crash.Console.dll!CrashLab.NotificationSender+<SendWelcomeEmail>d__0.MoveNext()+0x17b
    000074517CA0BAB0 0000745104D23BB8 System.Private.CoreLib.dll!System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()+0x38
    000074517CA0BAD0 0000745103F603A4 System.Private.CoreLib.dll!System.Threading.Tasks.Task+<>c.<ThrowAsync>b__124_1(System.Object)+0x14
    000074517CA0BAE0 0000745103F4CCCA System.Private.CoreLib.dll!System.Threading.ThreadPoolWorkQueue.Dispatch()+0x18a
    000074517CA0BB40 0000745103F564AF System.Private.CoreLib.dll!System.Threading.PortableThreadPool+WorkerThread.WorkerThreadStart()+0xff
    000074517CA0BC10 0000745103F39735 System.Private.CoreLib.dll!System.Threading.Thread.StartCallback()+0x85

StackTraceString: <none>
HResult: 80131505
> clrstack
OS Thread Id: 0x11 (11)
        Child SP               IP Call Site
000074517CA0ACB0 000074518355d913 [SoftwareExceptionFrame: 000074517ca0acb0]
000074517CA0BAB0 0000745104D23BB9 System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw() [...]
000074517CA0BAD0 0000745103F603A5 System.Threading.Tasks.Task+<>c.<ThrowAsync>b__124_1(System.Object) [...]
000074517CA0BAE0 0000745103F4CCCB System.Threading.ThreadPoolWorkQueue.Dispatch() [...]
000074517CA0BB40 0000745103F564B0 System.Threading.PortableThreadPool+WorkerThread.WorkerThreadStart() [...]
000074517CA0BC10 0000745103F39736 System.Threading.Thread.StartCallback()
```

Compare the two stacks:

- `clrstack` shows the **thread's current stack**: a thread pool worker running
  `Task.ThrowAsync` - the runtime *re-throwing* the exception. Your code is not on it at all.
- `pe` shows the **exception's own stack trace**, captured where it was originally thrown:
  `NotificationSender.<SendWelcomeEmail>d__0.MoveNext()` - the state machine of the
  `async void` method.

**For async code, trust `pe`, not `clrstack`.** An exception from an `async void` method cannot be
observed by its caller; it is re-thrown on the thread pool and takes down the process. Fix: make it
`async Task` and await it (or handle all exceptions inside it). The only legitimate `async void`
methods are event handlers.

---

## Scenario 3: stack overflow

```bash
docker compose run --rm crash stackoverflow
```
```text
Crash.Console PID 1 - scenario 'stackoverflow'
Stack overflow.
Repeated 261950 times:
--------------------------------
   at CrashLab.Customer.get_DisplayName()
--------------------------------
   at Program.<Main>$(System.String[])
[createdump] Gathering state for process 1 dotnet
[createdump] Crashing thread 0001 signal 6 (0006)
...
[createdump] Dump successfully written in 7125ms
```

A `StackOverflowException` **cannot be caught**; the process is always terminated. The runtime
prints a compact summary, and dump-on-crash still works.

```text
> clrthreads
 DBG   ID     OSID ThreadOBJ           State GC Mode     GC Alloc Context                  Domain           Count Apt Exception
   0    1        1 0000623C9063E3F0    20060 Cooperative 00007816E740AC38:00007816E740C2D0 0000623C90611A70 -00001 Ukn System.StackOverflowException 00007816e7400100
> pe
Exception object: 00007816e7400100
Exception type:   System.StackOverflowException
Message:          <none>
InnerException:   <none>
StackTrace (generated):
<none>
```

`pe` has no stack trace - that is the pre-allocated exception object. Look at the thread itself:

```text
> clrstack
OS Thread Id: 0x1 (0)
        Child SP               IP Call Site
0000781F1DB7FA80 0000781f1ec66913 [FaultingExceptionFrame: 0000781f1db7fa80]
00007FFFF8856000 0000781EA04028A2 CrashLab.Customer.get_DisplayName()
00007FFFF8856020 0000781EA04028BF CrashLab.Customer.get_DisplayName()
...
```

Don't scroll through a quarter of a million frames - save the output and count:

```bash
docker compose run --rm tools dotnet-dump analyze /dumps/crash_dotnet_1_1792982533.dmp \
    -c "setthread 0" -c "clrstack" -c exit > dumps/so-stack.txt
grep -c get_DisplayName dumps/so-stack.txt
```
```text
261950
```

The repeating frame is the bug:

```csharp
// BUG: the property calls itself instead of returning a field -> infinite recursion.
public string DisplayName => string.IsNullOrEmpty(Name) ? "<unknown>" : DisplayName;
```

`dumpstackobjects` tells the same story from the data side - the same `Customer` object is
referenced from the stack 261,953 times.

!!! note "On Windows `clrstack` may be empty"
    When we ran the same scenario natively on Windows, `clrstack` on the overflowed thread printed
    no frames. `dumpstackobjects` still worked and gave it away (16,074 references to the same
    `CrashLab.Customer` object on one thread's stack).

---

## The crash report (Linux only)

With `DOTNET_EnableCrashReport=1`, createdump also writes `<dump>.crashreport.json` - a few dozen
KB of JSON with every thread's native and managed frames:

```json
{
 "payload" : {
  "protocol_version": "1.0.0",
  "configuration" : {
   "architecture": "amd64",
   "version": "10.0.1426.52326 @Commit: 0427b9abbead3fa64c6655eb4290c537397131fa "
  },
  "process_name": "Crash.Console.dll",
  "threads" : [
   ...
   {
    "is_managed": "true",
    "crashed": "true",
    "managed_exception_object": "0x7879e340cc40",
    "managed_exception_type": "System.InvalidOperationException",
    "managed_exception_hresult": "0x80131509",
    "native_thread_id": "0xe",
    ...
```

Managed frames carry a `"method_name"`. The report is small enough to attach to an alert or ship to
your log system, so you get a first idea of a crash **without** moving a 170 MB dump around.

## Summary

| Crash | First command | Where the truth is |
|---|---|---|
| Unhandled exception | `clrthreads` → `setthread` | `pe` (exception stack), `clrstack` with symbols |
| `async void` | `clrthreads` → `setthread` | `pe` - `clrstack` only shows the re-throw |
| Stack overflow | `clrthreads` → `setthread` | `clrstack` (repeating frame), `dumpstackobjects` |

## Try it yourself

1. Add `DOTNET_DbgMiniDumpType: "1"` (Mini) under an `environment:` key of the `crash` service in
   `docker-compose.yml`, rerun `unhandled`, and compare the size. Which commands still work?
2. Read the crash report of the `async-void` run. Which thread has `"crashed": "true"`?

Next: [Linux and Docker](07-linux-docker.md).
