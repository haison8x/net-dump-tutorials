# 7. WinDbg and Visual Studio (optional)

The labs use `dotnet-dump` in a container because it runs everywhere. On Windows you can open the
very same files from `./dumps` with graphical tools. They add two things `dotnet-dump` lacks:
**native** (C/C++) frames and memory, and a UI.

!!! info "What was verified"
    `dotnet-dump analyze` on a Windows host opening the labs' Linux dumps was verified while writing
    this tutorial. The WinDbg and Visual Studio steps below follow Microsoft's documentation but were
    not re-run for this tutorial.

## `dotnet-dump` on the Windows host

```powershell
dotnet tool install -g dotnet-dump
dotnet-dump analyze .\dumps\deadlock-locks.dmp
```

All SOS commands work the same way; the DAC for the Linux runtime is downloaded from Microsoft's
symbol server automatically. Use the **x64** `dotnet-dump` for dumps from Linux x64 or Arm64.

## WinDbg

1. Install: `winget install Microsoft.WinDbg`.
2. *File → Open dump file* → pick a `.dmp` from `./dumps`. WinDbg can open Linux core dumps as
   well as Windows dumps ([MS docs](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debug-linux-dumps#analyze-dumps-on-windows)).
3. SOS commands are prefixed with `!`. Recent WinDbg versions load SOS for .NET automatically when
   they detect the runtime; otherwise install it with the `dotnet-sos` tool:
   ```powershell
   dotnet tool install -g dotnet-sos
   dotnet-sos install
   ```
   and load it with `.load` using the path `dotnet-sos install` prints.
4. Symbols: `.symfix` + `.reload` for Microsoft symbols, `.sympath+ C:\path\to\your\pdbs` for yours.

The commands map one-to-one:

| dotnet-dump | WinDbg |
|---|---|
| `clrthreads` | `!clrthreads` |
| `clrstack -a` | `!clrstack -a` |
| `dumpheap -stat` | `!dumpheap -stat` |
| `gcroot <addr>` | `!gcroot <addr>` |
| `syncblk` | `!syncblk` |
| `dumpasync` | `!dumpasync` |
| `setthread 8` | `~8s` (WinDbg's own thread numbering) |
| - | `k` - native stack, `!analyze -v` - automatic crash analysis |

WinDbg is the tool of choice when the problem is **below** .NET: a crash inside a native library,
a P/Invoke gone wrong, heap corruption, or native memory leaks.

## Visual Studio

Visual Studio opens dump files directly: *File → Open → File* → the `.dmp`, then in the
*Minidump File Summary* page choose **Debug with Managed Only** (or *Mixed*). Visual Studio 16.8
and later can also open **Linux** dumps of .NET Core 3.1.7+ apps
([MS docs](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debug-linux-dumps#analyze-dumps-on-windows)).

Once the dump is loaded you debug it like a paused program:

| Window | Use it for |
|---|---|
| **Parallel Stacks** (Debug → Windows) | the graphical `pstacks` - deadlocks and starvation at a glance |
| **Tasks** / Parallel Stacks *Tasks* view | async "stacks" - the graphical `dumpasync` |
| **Threads**, **Call Stack**, **Locals** | `clrthreads`, `clrstack -a`, but clickable, with source |
| **Exception** helper | the exception that crashed the process |
| **Debug Managed Memory** (Visual Studio Enterprise) | the graphical `dumpheap -stat` + paths to root; can **compare against a baseline dump** - Lab 1's method in a UI |

Visual Studio needs the matching PDBs (and ideally the source) to show code. For the lab dumps,
point *Tools → Options → Debugging → Symbols* to a folder containing the lab PDBs (you can copy them
out of the tools image: `docker compose run --rm -v ${PWD}/symbols:/out tools cp -r /symbols/. /out`).

## Which tool when?

| Situation | Tool |
|---|---|
| Managed leak / hang / crash, any OS, scripting | `dotnet-dump` |
| You want a UI, source navigation, comparing heap snapshots | Visual Studio |
| Native crash, native memory, P/Invoke, mixed-mode | WinDbg (Windows) / lldb (Linux) |
| Automation, CI, custom checks | ClrMD ([Chapter 9](09-automation-clrmd-dumplinq.md)) |

Next: [Linux, Docker and Kubernetes](08-linux-docker-k8s.md).
