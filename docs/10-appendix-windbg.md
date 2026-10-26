# 10. Appendix: WinDbg

WinDbg is Microsoft's debugger for Windows. It reads the same dumps as `dotnet-dump` and runs the
same SOS commands (with a `!` prefix), and it adds what `dotnet-dump` cannot do:

- **native frames and memory** - crashes inside C/C++ libraries, P/Invoke bugs, the exact faulting
  instruction;
- **`!analyze -v`** - automatic crash triage;
- a GUI with stack, memory, source and disassembly windows.

This appendix is **Windows only** and runs outside Docker. Use it when a problem goes below .NET,
or when your team already lives in WinDbg.

!!! info "What was verified"
    Everything below was run on Windows 11 with WinDbg 1.2610.1001.0 (debugger engine
    10.0.29661.1004), ProcDump from the Sysinternals Suite, the .NET 10 SDK and .NET Framework 4.8.1.
    The outputs were captured with `cdb.exe`, the console debugger that ships inside the WinDbg
    package and uses the same engine: the commands are identical in WinDbg's command window.
    Paths in the outputs are shortened with `...`.

## Install

```powershell
winget install Microsoft.WinDbg
winget install Microsoft.Sysinternals.Suite     # ProcDump has no separate winget package
```

Start **WinDbg** from the Start menu, then *File → Open dump file*. Type commands in the command
bar at the bottom.

- **SOS loads automatically** for .NET and .NET Framework dumps (WinDbg ships it in its `winext\sos`
  folder). You do not need `.loadby sos clr` - in our tests it loaded an older SOS next to the
  bundled one, and commands then failed with *"SOS does not support the current target architecture"*.
- **Symbols:** `.symfix` for Microsoft's symbol server, `.sympath+ <folder with your .pdb files>` for
  your own code, and `.lines -e` to show source file and line numbers.

| dotnet-dump | WinDbg |
|---|---|
| `clrthreads` | `!clrthreads` (`!threads` also works) |
| `setthread 6` | `~6s` - WinDbg's own thread numbers (the first column of `!threads`) |
| `clrstack -a` | `!clrstack -a` |
| `clrstack -all` | `~*e !clrstack` |
| `dumpheap -stat`, `gcroot`, `syncblk`, `dumpasync`... | the same with `!` |
| - | `k` - native + managed stack |
| - | `!analyze -v` - automatic analysis of the exception in the dump |

!!! tip "Scripting WinDbg"
    `cdb.exe` lives in the WinDbg package folder (`Get-AppxPackage Microsoft.WinDbg` shows it, under
    `amd64\`). `cdb -z <dump> -c "!syncblk; q"` runs commands and exits. Note that `~*e` and
    `.sympath+` consume the **rest** of a `-c` command line - put them last, or pass the symbol path
    through the `_NT_SYMBOL_PATH` environment variable.

---

## 1. Open the lab dumps

WinDbg opens Linux core dumps as well. The lock-ordering dump from [Lab 2](05-lab-deadlock.md),
collected in a Linux container, gives exactly the same answer as `dotnet-dump`:

```text
0:000> !syncblk
Index         SyncBlock MonitorHeld Recursion Owning Thread Info          SyncBlock Owner
    8 000062E9F35DEDB0           13         1 0000745B90001150 27  32   0000745cb497c348 System.Object
    9 000062E9F35DEE08           13         1 0000745BAC002060 23  28   0000745cb498f3c0 System.Object
-----------------------------
Total           9
```

So for managed problems you can follow any lab in WinDbg with the same commands. The rest of this
appendix is about what only WinDbg shows.

---

## 2. A native crash: what `dotnet-dump` cannot see

Lab 3 has a fourth scenario, `native`
([`PacketParser.cs`](https://github.com/haison8x/net-dump-tutorials/blob/main/labs/Crash.Console/PacketParser.cs)):
a P/Invoke into the C runtime's `strlen` with a null pointer.

```csharp
// BUG: no null check - strlen(NULL) dereferences address 0 inside the C runtime.
return (int)(OperatingSystem.IsWindows() ? WindowsCrt.strlen(buffer) : Libc.strlen(buffer));
```

### Collect the dump at the moment of the crash

Build the lab on Windows and let **ProcDump** start it and write a dump on the access violation:

```powershell
dotnet build labs/Crash.Console
procdump64 -ma -e 1 -f C0000005 -x dumps\win labs\Crash.Console\bin\Debug\net10.0\Crash.Console.exe native
```
```text
[13:23:20]Dump 1 initiated: ...\dumps\win\Crash.Console.exe_261026_132320.dmp
[13:23:21]Dump 1 writing: Estimated dump file size is 102 MB.
[13:23:21]Dump 1 complete: 102 MB written in 0.2 seconds
Fatal error.
0xC0000005
   at CrashLab.PacketParser+WindowsCrt.strlen(IntPtr)
   at CrashLab.PacketParser.HeaderLength(System.String)
   at Program.<Main>$(System.String[])
[13:23:21]Dump count reached.
```

- `-ma` full dump, `-x <folder> <exe> <args>` launch the program under ProcDump.
- `-e 1 -f C0000005` dump on the **first-chance** access violation. Plain `-e` (unhandled exceptions
  only) wrote **no dump** in our test: the .NET runtime handles the access violation itself and turns
  it into a *Fatal error*, so ProcDump never sees an unhandled exception.

!!! warning "Dump-on-crash dumps confuse `!analyze`"
    A dump written by the runtime's own crash handler (`DOTNET_DbgEnableMiniDump=1`, as in Lab 3)
    works fine with SOS, but `!analyze -v` blames the wrong thing on it:
    `FAILURE_BUCKET_ID: BREAKPOINT_80000003_coreclr.dll!LaunchCreateDump` - the breakpoint the runtime
    hits while launching `createdump`. For WinDbg crash analysis on Windows, collect with ProcDump.

### `!analyze -v`

```text
0:000> !analyze -v
...
KEY_VALUES_STRING: 1

    Key  : AV.Dereference
    Value: NullPtr

    Key  : AV.Type
    Value: Read
...
EXCEPTION_RECORD:  (.exr -1)
ExceptionAddress: 00007fff3e0bd921 (ucrtbase!strlen+0x0000000000000031)
   ExceptionCode: c0000005 (Access violation)
  ExceptionFlags: 00000000
NumberParameters: 2
   Parameter[0]: 0000000000000000
   Parameter[1]: 0000000000000000
Attempt to read from address 0000000000000000

PROCESS_NAME:  Crash.Console.dll
...
IP_IN_PAGED_CODE:
ucrtbase!strlen+31
00007fff`3e0bd921 488b10          mov     rdx,qword ptr [rax]

SYMBOL_NAME:  ucrtbase!strlen+31
MODULE_NAME: ucrtbase
IMAGE_NAME:  ucrtbase.dll
FAILURE_BUCKET_ID:  INVALID_POINTER_READ_c0000005_ucrtbase.dll!strlen
```

In one command: a **read** of address **0** (a null pointer) by the instruction
`mov rdx,qword ptr [rax]` inside **`ucrtbase!strlen`**.

### One stack, native and managed: `k`

```text
0:000> .lines -e
0:000> k
Child-SP          RetAddr               Call Site
00000033`bab7e7c8 00007ffe`3c30523e     ucrtbase!strlen+0x31
00000033`bab7e7d0 00007ffe`3c305120     Crash_Console!ILStubClass.IL_STUB_PInvoke(IntPtr)+0xae
00000033`bab7e8c0 00007ffe`3c304b9b     Crash_Console!CrashLab.PacketParser.HeaderLength+0x90 [...\labs\Crash.Console\PacketParser.cs @ 16]
00000033`bab7e930 00007ffe`9bf099f3     Crash_Console!Program.<Main>$+0x46b [...\labs\Crash.Console\Program.cs @ 39]
00000033`bab7ea90 00007ffe`9be94912     coreclr!CallDescrWorkerInternal+0x83
00000033`bab7ead0 00007ffe`9be663fc     coreclr!MethodDescCallSite::CallTargetWorker+0x28e
...
00000033`bab7f4b0 00007fff`1ff7029c     hostfxr!execute_app+0x2bb
...
00000033`bab7fb90 00007fff`40b9ee4c     kernel32!BaseThreadInitThunk+0x17
00000033`bab7fbc0 00000000`00000000     ntdll!RtlUserThreadStart+0x2c
```

The native C runtime frame, the P/Invoke stub, your managed methods with file and line, the .NET
runtime and host, down to the Windows thread start - one stack.

### The same dump in `dotnet-dump`

```text
> clrstack
OS Thread Id: 0x90f0 (0)
        Child SP               IP Call Site
00000033BAB7E7F8 00007fff3e0bd921 [InlinedCallFrame: 00000033bab7e7f8] CrashLab.PacketParser+WindowsCrt.strlen(IntPtr)
00000033BAB7E7F8 00007ffe3c30523e [InlinedCallFrame: 00000033bab7e7f8] CrashLab.PacketParser+WindowsCrt.strlen(IntPtr)
00000033BAB7E7D0 00007FFE3C30523E ILStubClass.IL_STUB_PInvoke(IntPtr)
00000033BAB7E8C0 00007FFE3C305120 CrashLab.PacketParser.HeaderLength(System.String) [...\labs\Crash.Console\PacketParser.cs @ 16]
00000033BAB7E930 00007FFE3C304B9B Program.<Main>$(System.String[]) [...\labs\Crash.Console\Program.cs @ 39]
```

`dotnet-dump` stops at the P/Invoke boundary: you see *that* `strlen` was called, but not which
native module failed, at which instruction, or why. For managed bugs that is enough; for native ones
it is the start of the story.

!!! note "On Linux"
    The same scenario crashes in a container too (`docker compose run --rm crash native`), with
    `SIGSEGV` (`signal 11`) instead of `0xC0000005`. Native frames on Linux are the job of lldb
    ([Chapter 7](07-linux-docker.md#lldb-sos)).

---

## 3. .NET Framework apps

[`labs/Framework.Console`](https://github.com/haison8x/net-dump-tutorials/blob/main/labs/Framework.Console/Program.cs)
targets **.NET Framework 4.8** and has two scenarios: `deadlock` (two bank transfers lock two
accounts in opposite order) and `leak` (a static session cache that never evicts). It builds with
the normal .NET SDK - the reference assemblies come from NuGet, no Developer Pack needed.

```powershell
dotnet build labs/Framework.Console
labs\Framework.Console\bin\Debug\net48\Framework.Console.exe deadlock
```
```text
Framework.Console PID 5284 - CLR 4.0.30319.42000 - scenario 'deadlock'
Running. Collect a dump now, then press Ctrl+C to exit.
```

In a second terminal:

```powershell
procdump64 -ma 5284 dumps\win\framework-deadlock.dmp
```
```text
[13:24:40]Dump 1 complete: 83 MB written in 0.5 seconds
```

!!! note "`dotnet-dump` and .NET Framework"
    A .NET Framework process has no diagnostics IPC channel, so `dotnet-dump ps` does **not** list
    it. On Windows, `dotnet-dump collect -p <pid>` still worked in our test, and `dotnet-dump analyze`
    opened the dump and ran `syncblk` fine. WinDbg is not required for .NET Framework - it is the
    traditional tool, and most older articles and runbooks use it.

### The deadlock

```text
0:000> !syncblk
Index SyncBlock MonitorHeld Recursion Owning Thread Info  SyncBlock Owner
    3 000001a1b12a5fe8            3         1 000001a1b12a5290 7b70   6   000001a1b2df6910 FrameworkLab.Account
    4 000001a1b12a6038            3         1 000001a1b12a8690 93d0   7   000001a1b2df6938 FrameworkLab.Account

0:000> !threads
       ID OSID ThreadOBJ           State GC Mode     GC Alloc Context                  Domain           Count Apt Exception
   0    1 8c2c 000001a1b1245a40  202a020 Preemptive  000001A1B2DF6CF0:000001A1B2DF7FD0 000001a1b121b340 0     MTA
   5    2 1308 000001a1b126e6d0    2b220 Preemptive  0000000000000000:0000000000000000 000001a1b121b340 0     MTA (Finalizer)
   6    3 7b70 000001a1b12a5290  202b220 Preemptive  000001A1B2DF8000:000001A1B2DF9FD0 000001a1b121b340 1     MTA
   7    4 93d0 000001a1b12a8690  202b220 Preemptive  0000000000000000:0000000000000000 000001a1b121b340 1     MTA
```

Two `Account` objects, each owned by one thread; `MonitorHeld` 3 = the owner (1) + one waiter (2).
The first column of `!threads` is WinDbg's thread number - switch with `~6s`:

```text
0:000> ~6s
0:006> !clrstack -a
OS Thread Id: 0x7b70 (6)
        Child SP               IP Call Site
...
0000009d958feb38 00007fff40c828a4 [HelperMethodFrame_1OBJ: 0000009d958feb38] System.Threading.Monitor.Enter(System.Object)
0000009d958fec30 00007ffeab98127e FrameworkLab.Bank.Transfer(FrameworkLab.Account, FrameworkLab.Account, System.Decimal)
    PARAMETERS:
        this (0x0000009d958fed70) = 0x000001a1b2df6898
        from (0x0000009d958fed78) = 0x000001a1b2df6910
        to (0x0000009d958fed80) = 0x000001a1b2df6938
        amount (0x0000009d958fedc0) = 0x0000000000000000
    LOCALS:
        0x0000009d958fec88 = 0x000001a1b2df6910
        0x0000009d958fed48 = 0x0000000000000001
        0x0000009d958fec80 = 0x000001a1b2df6938
        0x0000009d958fed40 = 0x0000000000000000
...
```

Thread 6 holds `from` (`…6910`, with its `lockTaken` flag = 1) and waits in `Monitor.Enter` for `to`
(`…6938`). Thread 7 does the same with the two accounts swapped. The fix is a global lock order,
for example always locking the account with the smaller id first.

### The leak: here `gcroot` finds the static

```text
0:000> !dumpheap -stat
...
00007ffeab876cc0     5000       200000 FrameworkLab.UserSession
000001bab043c1e0     5073       261186      Free
00007ffeab877678        2       399024 System.Collections.Generic.Dictionary`2+Entry[[System.Guid, mscorlib],[FrameworkLab.UserSession, Framework.Console]][]
00007fff04c99018     5001     41080281 System.Byte[]
Total 15403 objects

0:000> !gcroot 000001bab36025a8
HandleTable:
    000001bab06a17d8 (pinned handle)
    -> 000001bac2225c98 System.Object[]
    -> 000001bab22288d8 System.Collections.Generic.Dictionary`2[[System.Guid, mscorlib],[FrameworkLab.UserSession, Framework.Console]]
    -> 000001bac22496e8 System.Collections.Generic.Dictionary`2+Entry[[System.Guid, mscorlib],[FrameworkLab.UserSession, Framework.Console]][]
    -> 000001bab36025a8 FrameworkLab.UserSession

Found 1 unique roots (run '!GCRoot -all' to see all roots).
```

Compare with [Lab 1](04-lab-memory-leak.md#who-holds-it-gcroot): on .NET 10, `gcroot` printed
*Found 0 unique roots* for an object held by a static field. On .NET Framework the static fields
live in an `Object[]` behind a **pinned handle**, which is exactly the kind of root `gcroot` searches,
so the full path to the static `Dictionary` shows up.

---

## Summary

| Situation | Use |
|---|---|
| Managed leak, hang or crash (.NET or .NET Framework) | `dotnet-dump` or WinDbg - same SOS commands |
| Crash inside native code, P/Invoke, the faulting instruction | WinDbg: `!analyze -v`, `k` |
| Dump at a first-chance exception on Windows | ProcDump: `-e 1 -f <code>` |
| `!analyze -v` on a dump-on-crash dump blames `LaunchCreateDump` | collect with ProcDump instead |
| Native frames on Linux | lldb ([Chapter 7](07-linux-docker.md#lldb-sos)) |
