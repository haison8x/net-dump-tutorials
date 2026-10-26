# .NET Dump Analysis Tutorial

**Trace memory leaks, deadlocks, hangs and crashes in .NET 10 apps using memory dumps - hands-on, 100% in Docker.**

📖 **Read it online: <https://haison8x.github.io/net-dump-tutorials/>**

This tutorial teaches .NET developers how to find production problems from a memory dump:

| You will learn to... | Lab |
|---|---|
| find **memory leaks** (static events, unbounded caches, undisposed timers, LOH fragmentation) | [Lab 1](docs/04-lab-memory-leak.md) |
| find **deadlocks and hangs** (`lock` ordering, `SemaphoreSlim` async deadlocks, thread pool starvation) | [Lab 2](docs/05-lab-deadlock.md) |
| find the cause of **crashes** (unhandled exceptions, `async void`, `StackOverflow`) | [Lab 3](docs/06-lab-crash.md) |
| **automate** dump analysis with ClrMD and DumpLinq | [Chapter 9](docs/09-automation-clrmd-dumplinq.md) |

Every lab app is deliberately broken. You run it, break it, collect a dump, and find the bug with
`dotnet-dump`/SOS - the same way you would in production. Every command output in the docs was
captured from a real run of these labs.

## Quick start

The only prerequisite is **Docker** (Docker Desktop or Docker Engine with Compose v2).

```bash
git clone https://github.com/haison8x/net-dump-tutorials.git
cd net-dump-tutorials
docker compose --profile manual build        # build lab apps + the analysis "tools" image
docker compose up -d memoryleak deadlock     # start the two lab APIs
curl http://localhost:5100/                  # MemoryLeak.Api
curl http://localhost:5200/                  # Deadlock.Api
```

Then follow the chapters in order, starting with [Setup](docs/00-setup.md).

## Chapters

0. [Setup](docs/00-setup.md)
1. [Dump fundamentals](docs/01-dump-fundamentals.md)
2. [Collecting dumps](docs/02-collecting-dumps.md)
3. [SOS essentials](docs/03-sos-essentials.md)
4. [Lab 1 - Memory leaks](docs/04-lab-memory-leak.md)
5. [Lab 2 - Deadlocks and hangs](docs/05-lab-deadlock.md)
6. [Lab 3 - Crashes](docs/06-lab-crash.md)
7. [WinDbg and Visual Studio (optional)](docs/07-windbg-and-visual-studio.md)
8. [Linux, Docker and Kubernetes](docs/08-linux-docker-k8s.md)
9. [Automation with ClrMD and DumpLinq](docs/09-automation-clrmd-dumplinq.md)
10. [Troubleshooting and FAQ](docs/10-troubleshooting-faq.md)
- [Cheat sheet](docs/cheatsheet.md)

## Repository layout

```
docker-compose.yml        all services: lab apps, load generator, analysis tools
docker/
  app.Dockerfile          lab app image (+ dotnet-dump/-counters/-gcdump/-trace inside)
  tools.Dockerfile        analysis image: dotnet-dump, DumpAnalyzer, queries, lab symbols
labs/
  MemoryLeak.Api/         Lab 1 - four kinds of memory leaks
  Deadlock.Api/           Lab 2 - lock deadlock, async deadlocks, thread pool starvation
  Crash.Console/          Lab 3 - crashes
  LoadGen/                tiny HTTP load generator
tools/
  DumpAnalyzer/           ClrMD-based analyzer: heap diff, root paths, hang report
  queries/                ad-hoc dump queries (ClrMD and DumpLinq), .NET 10 file-based apps
k8s/                      Kubernetes manifests (Chapter 8)
scripts/                  collect-dump.ps1 / collect-dump.sh
docs/                     the tutorial (published with MkDocs to GitHub Pages)
dumps/                    your dumps land here (git-ignored!)
```

> ⚠️ **Dumps contain everything that was in the process memory** - connection strings, tokens,
> customer data. Treat them like production data. `dumps/` is git-ignored on purpose.

## Sources and further reading

- Microsoft Learn: [Dumps overview](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dumps),
  [Collect dumps on crash](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/collect-dumps-crash),
  [SOS debugging extension](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/sos-debugging-extension),
  [Debugger extensions](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debugger-extensions),
  [Debug Windows dumps](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debug-windows-dumps),
  [Debug Linux dumps](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debug-linux-dumps),
  [LLDB on Linux](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/lldb-linux),
  [Dumps FAQ](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/faq-dumps)
- Anders Pedersen: [.NET memory dump analysis with DumpLinq](https://medium.com/@anders_pedersen/net-memory-dump-analysis-with-dumplinq-8a7a73f1c136)
- Bacancy: [dotnet-dump analysis](https://www.bacancytechnology.com/qanda/dot-net/dotnet-dump-analysis)
- C# Corner: [Find memory leaks in .NET application using dump file](https://www.c-sharpcorner.com/blogs/find-memory-leaks-in-net-application-using-dump-file)
- MCP Market: [.NET crash dump collection skill](https://mcpmarket.com/tools/skills/net-crash-dump-collection)
