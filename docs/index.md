# .NET Dump Analysis Tutorial

**Trace memory leaks, deadlocks, hangs and crashes in .NET 10 apps using memory dumps - hands-on, 100% in Docker.**

A memory dump is a snapshot of a process: every object on the heap, every thread and its stack,
every lock. When a production app leaks memory, hangs, or crashes, a dump is often the only
evidence you get - and it is usually enough to find the exact line of code at fault.

This tutorial teaches you to read that evidence. You will run deliberately broken .NET 10 apps in
Docker, break them, collect dumps, and track every bug down to its root cause with `dotnet-dump`
and the SOS commands - then automate the analysis with ClrMD.

!!! info "Every output is real"
    All command outputs in this tutorial were captured from actual runs of the labs
    (.NET 10.0.14 in Linux containers, `dotnet-dump` 10.0.750501). Addresses and thread IDs will be
    different on your machine - the *shape* of the output will be the same.

## What you will learn

| Problem | Lab | Key techniques |
|---|---|---|
| Memory leaks | [Lab 1](04-lab-memory-leak.md) | `dumpheap -stat` diff, `gcroot`, `pathto`, static roots, `timerinfo`, LOH analysis |
| Deadlocks and hangs | [Lab 2](05-lab-deadlock.md) | `syncblk`, `clrstack -a`, `pstacks`, `dumpasync`, `threadpool`, `threadpoolqueue` |
| Crashes | [Lab 3](06-lab-crash.md) | dump-on-crash, crash reports, `pe`, `clrthreads`, symbols |
| Automation | [Chapter 8](08-automation-clrmd-dumplinq.md) | ClrMD, DumpLinq, LINQ over the heap |

## Who this is for

.NET developers who are comfortable with C# and async/await, and who have used Docker before.
No prior debugging-tools experience is needed.

## How to use it

1. **[Setup](00-setup.md)** - clone the repo and build the images (10 minutes).
2. Read **[Dump fundamentals](01-dump-fundamentals.md)**, **[Collecting dumps](02-collecting-dumps.md)**
   and **[SOS essentials](03-sos-essentials.md)** - the concepts the labs build on.
3. Do the **labs** in order. Each lab follows the same rhythm:

    **run the app → reproduce the problem → observe symptoms → collect a dump → analyze →
    find the root cause → fix and verify.**

4. Keep the **[Cheat sheet](cheatsheet.md)** open for your real incidents.

!!! warning "Dumps contain secrets"
    A dump contains everything that was in the process memory - connection strings, access tokens,
    personal data. Treat dumps like production data: restrict access, never commit them, delete
    them when you are done. The repo's `dumps/` folder is git-ignored for that reason.

## Source code

Everything is on GitHub: [haison8x/net-dump-tutorials](https://github.com/haison8x/net-dump-tutorials).
