using Microsoft.Diagnostics.Runtime;

namespace DumpAnalyzer;

// Automates the hang triage from Lab 2:
//   1. Monitor (lock) ownership + waiters -> wait-for graph -> deadlock cycles   (SOS: syncblk + clrstack -all)
//   2. SemaphoreSlim instances with no free permits and queued waiters          (SOS: dumpheap -type SemaphoreSlim + dumpobj)
//   3. Threads blocked synchronously on tasks (sync-over-async)                 (SOS: threadpool + pstacks)
public static class HangAnalyzer
{
    private static readonly string[] MonitorWaitFrames = ["Monitor.Enter", "Monitor.ReliableEnter", "Monitor.TryEnter"];
    private static readonly string[] TaskWaitFrames = ["Task.Wait", "Task.InternalWait", "SpinThenBlockingWait", "Task`1.GetResultCore", "TaskAwaiter.GetResult"];

    public static void Print(ClrRuntime runtime)
    {
        AnalyzeMonitors(runtime);
        AnalyzeSemaphores(runtime);
        AnalyzeBlockedThreads(runtime);
    }

    private static void AnalyzeMonitors(ClrRuntime runtime)
    {
        Console.WriteLine();
        Console.WriteLine("== Monitor locks (lock statements) ==");

        var threadsByAddress = runtime.Threads.ToDictionary(t => t.Address);
        var held = runtime.Heap.EnumerateSyncBlocks().Where(sb => sb.IsMonitorHeld).ToList();
        if (held.Count == 0)
        {
            Console.WriteLine("No Monitor is currently held.");
            return;
        }

        // object address -> owning thread
        var owners = new Dictionary<ulong, ClrThread>();
        foreach (var sb in held)
        {
            if (!threadsByAddress.TryGetValue(sb.HoldingThreadAddress, out var owner)) continue;
            owners[sb.Object] = owner;
            var obj = runtime.Heap.GetObject(sb.Object);
            Console.WriteLine($"  {obj.Type?.Name} @ {sb.Object:x16} owned by thread {owner.ManagedThreadId} (OS 0x{owner.OSThreadId:x}), {sb.WaitingThreadCount} waiter(s)");
        }

        // A thread waiting in Monitor.Enter has the lock object on its stack. Match
        // its stack roots against the held locks to find WHICH lock it waits for.
        var waitsFor = new Dictionary<ClrThread, (ClrThread Owner, ulong Lock)>();
        foreach (var thread in runtime.Threads.Where(t => t.IsAlive))
        {
            var frames = thread.EnumerateStackTrace().Take(15).Select(Name).ToList();
            if (!frames.Any(f => MonitorWaitFrames.Any(f.Contains))) continue;

            var candidate = thread.EnumerateStackRoots()
                .Select(r => r.Object.Address)
                .FirstOrDefault(a => owners.TryGetValue(a, out var o) && o.Address != thread.Address);
            if (candidate != 0)
                waitsFor[thread] = (owners[candidate], candidate);
        }

        foreach (var (waiter, (owner, lockObj)) in waitsFor)
        {
            var firstUserFrame = UserFrame(waiter.EnumerateStackTrace());
            Console.WriteLine($"  thread {waiter.ManagedThreadId} waits for {lockObj:x16} (owned by thread {owner.ManagedThreadId}) in {firstUserFrame}");
        }

        // Cycle detection in the wait-for graph.
        var reported = new HashSet<ulong>();
        foreach (var start in waitsFor.Keys)
        {
            var chain = new List<ClrThread>();
            var current = start;
            while (waitsFor.TryGetValue(current, out var edge) && !chain.Contains(current))
            {
                chain.Add(current);
                current = edge.Owner;
            }
            if (current == start && chain.Count > 1 && reported.Add(chain.Min(t => t.Address)))
            {
                Console.WriteLine();
                Console.WriteLine($"  *** DEADLOCK: {string.Join(" -> ", chain.Select(t => $"thread {t.ManagedThreadId}"))} -> thread {start.ManagedThreadId}");
            }
        }
    }

    private static void AnalyzeSemaphores(ClrRuntime runtime)
    {
        Console.WriteLine();
        Console.WriteLine("== SemaphoreSlim instances with waiters ==");

        var any = false;
        foreach (var sem in runtime.Heap.EnumerateObjects().Where(o => o.Type?.Name == "System.Threading.SemaphoreSlim"))
        {
            var currentCount = sem.ReadField<int>("m_currentCount");
            var syncWaiters = sem.ReadField<int>("m_waitCount");
            var asyncWaiters = 0;
            for (var node = sem.ReadObjectField("m_asyncHead"); !node.IsNull; node = node.ReadObjectField("Next"))
                asyncWaiters++;

            if (syncWaiters == 0 && asyncWaiters == 0) continue;
            any = true;
            Console.WriteLine($"  SemaphoreSlim @ {sem.Address:x16}: currentCount={currentCount}, sync waiters={syncWaiters}, async waiters={asyncWaiters}");
            if (currentCount == 0)
                Console.WriteLine("    -> no free permit: whoever holds it is not releasing it (re-entrancy? missing finally?)");
        }
        if (!any) Console.WriteLine("None.");
    }

    private static void AnalyzeBlockedThreads(ClrRuntime runtime)
    {
        Console.WriteLine();
        Console.WriteLine("== Threads blocked synchronously on a Task (sync-over-async) ==");

        var blocked = runtime.Threads
            .Where(t => t.IsAlive)
            .Select(t => (Thread: t, Frames: t.EnumerateStackTrace().Take(40).Select(Name).ToList()))
            .Where(x => x.Frames.Any(f => TaskWaitFrames.Any(f.Contains)))
            .ToList();

        var pool = runtime.ThreadPool;
        if (pool is not null)
            Console.WriteLine($"  Thread pool: {pool.ActiveWorkerThreads} active / {pool.IdleWorkerThreads} idle workers, min {pool.MinThreads}, max {pool.MaxThreads}" +
                              (pool.UsingWindowsThreadPool ? " (Windows thread pool)" : ""));

        Console.WriteLine($"  {blocked.Count} thread(s) blocked in Task.Wait/.Result/GetResult");
        foreach (var group in blocked.GroupBy(x => UserFrame(x.Thread.EnumerateStackTrace()))
                                     .OrderByDescending(g => g.Count()))
            Console.WriteLine($"    {group.Count(),4} x {group.Key}");
    }

    private static string Name(ClrStackFrame f) => f.Method?.Signature ?? f.FrameName ?? "?";

    // The first managed frame that is not framework code - usually where YOUR code is stuck.
    private static string UserFrame(IEnumerable<ClrStackFrame> frames) =>
        frames.Where(f => f.Method is not null)
              .Select(Name)
              .FirstOrDefault(n => !n.StartsWith("System.") && !n.StartsWith("Microsoft.")) ?? "?";
}
