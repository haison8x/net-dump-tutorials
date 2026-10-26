# 9. Troubleshooting and FAQ

Every problem below was hit while building and testing this tutorial.

## Collecting

??? question "`Unable to connect to Process 1. Please verify that /tmp/ is writable by the current user...`"
    `dotnet-dump` cannot reach the runtime's diagnostic socket. Check, in this order:

    1. **Same user** - `dotnet-dump` must run as the target's user (or as root with `CAP_SYS_PTRACE`).
       In the .NET images that is UID 1654 (`$APP_UID`).
    2. **Same `/tmp`** - the socket is in the target's `/tmp` (or `$TMPDIR`). From another container
       share `/tmp` with a volume, or set `TMPDIR=/proc/<pid>/root/tmp` when you share the PID
       namespace ([Chapter 7](07-linux-docker.md#option-3-shared-pid-namespace-only-proc1roottmp)).
    3. **Diagnostics disabled** - `DOTNET_EnableDiagnostics=0` in the target turns the channel off.

??? question "`[createdump] ... open(/proc/1234/mem) FAILED Permission denied (13)`"
    `ptrace` is blocked: a seccomp profile, Yama `ptrace_scope` 2/3, SELinux or AppArmor. See
    the [Microsoft FAQ](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/faq-dumps). In Docker,
    `--cap-add SYS_PTRACE` helps on old kernels.

??? question "The app stopped responding while I collected the dump"
    Expected: the process is suspended while the dump is written - over 10 seconds for a 600 MB dump
    in Lab 2. Take the instance out of rotation first, use `--type Heap`, and relax liveness probes.

??? question "Git Bash: `/dumps/x.dmp` becomes `C:/Program Files/Git/dumps/x.dmp`"
    MSYS path conversion. `export MSYS_NO_PATHCONV=1`, or use PowerShell/WSL.

??? question "`dotnet-counters monitor` crashes with `System.IO.IOException: The handle is invalid`"
    `monitor` draws a live table and needs a real terminal. Use `docker compose exec -it ...`, or record
    with `dotnet-counters collect --format csv -o file.csv` instead.

??? question "Dumps are huge / the disk filled up"
    A Full dump is about the size of the process's memory. Use `--type Heap` (153 MB vs 272 MB for a
    fresh lab API), write to a volume with space, and delete dumps when done. Don't let a load test
    run unthrottled against a leaking app: our first, unthrottled lab run grew the heap to 15 GB in
    15 seconds - that's why LoadGen has `-r`.

??? question "No dump was written when my container was killed"
    Being killed for exceeding a memory limit, or by `kill -9`, is not a crash - the process gets a
    `SIGKILL` and the runtime has no chance to run `createdump`. Collect a dump proactively before
    the memory limit is reached, e.g. with `dotnet-monitor` collection rules.

## Analyzing

??? question "`gcroot` says `Found 0 unique roots`, but the object survives every GC"
    On .NET 10 dumps, SOS `gcroot` did not report roots that go through **static fields** in our
    tests. Read the static with `name2ee` + `dumpclass`, then `pathto <static value> <object>`, or run
    `dumpanalyzer roots`. See [Lab 1](04-lab-memory-leak.md#when-gcroot-finds-nothing-static-fields).
    (If the object really is unrooted, it is just garbage not collected yet - `dumpheap` lists dead
    objects too.)

??? question "`There is no current managed exception on this thread`"
    The current thread is not the one that threw. `clrthreads`, find the thread with an exception in
    the last column, `setthread <DBG index>`, then `pe`.

??? question "`setthread -t 0x27` fails: `Cannot parse argument '0x27'`"
    `setthread -t` expects the OS thread id in **decimal**: `setthread -t 39`. Or use the DBG index:
    `setthread 32`.

??? question "Stacks have no file/line numbers"
    SOS cannot find matching PDBs. `setsymbolserver -directory <folder with your .pdb>` (the tools
    image: `/symbols`). The PDB must come from the **exact same build** as the binary in the dump.

??? question "A method I expected is missing from the stack"
    It was inlined by the JIT (Release builds). Look at the caller. See the
    [note in Lab 2](05-lab-deadlock.md#root-cause-and-fix_1).

??? question "`crashinfo` printed nothing and my other `-c` commands did not run"
    `crashinfo` prints `No crash info to display` and stops the `-c` chain. Run it on its own.

??? question "`clrstack` is empty for a stack overflow (Windows)"
    Use `dumpstackobjects` - the same object repeated thousands of times on one stack points to the
    recursion. On Linux, `clrstack` showed all 261,950 frames.

??? question "`Could not find matching DAC for this runtime` (ClrMD)"
    ClrMD needs the DAC of the exact runtime version in the dump. In the labs' `tools` container it
    is found automatically. Elsewhere pass it explicitly - DumpAnalyzer: `--dac <path to
    libmscordaccore.so / mscordaccore.dll>` - or analyze in a container built from the same .NET image.

??? question "`It was not possible to find any compatible framework version` (dotnet-dump on Linux)"
    `DOTNET_ROOT` points to a different .NET installation. Unset it or point it to the right one.

??? question "DumpLinq returns fewer objects than `dumpheap`"
    DumpLinq 0.1.3 uses ClrMD 3.1, which missed objects in our .NET 10 dumps. Cross-check with
    ClrMD 4.x ([Chapter 8](08-automation-clrmd-dumplinq.md#dumplinq)).

## Docker

??? question "`failed to prepare extraction snapshot ... parent snapshot ... does not exist`"
    A Docker BuildKit cache glitch we hit a few times while building. Run the build again; if it
    persists, `docker builder prune` and rebuild.

??? question "`dotnet-dump ps` shows nothing in the sidecar"
    The sidecar must share the app's **PID namespace** (`pid: "service:memoryleak"`) - and to
    connect, its `/tmp` too, or use `TMPDIR=/proc/1/root/tmp`.

## FAQ

**Is it safe to collect a dump in production?** The process pauses for seconds and the file
contains all its data. It is routine practice, but plan for the pause and handle the file as
sensitive data.

**Full or Heap?** Heap for managed leaks/hangs/crashes. Full when native code may be involved
or the dump must be analyzed with lldb for native frames.

**Can I analyze a Linux dump on Windows?** Yes - `dotnet-dump analyze` on a Windows host opens the lab dumps fine.
Microsoft recommends the same OS/distro/architecture when possible; a container from the same base
image is the easiest way.

**One dump or two?** Leaks: two (diff). Hangs: one, or 2-3 a few seconds apart to see whether
anything moves. Crashes: the one written at crash time.
