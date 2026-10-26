#:package Microsoft.Diagnostics.Runtime@4.2.751201

// ClrMD query - Lab 2, scenario 2 (async deadlocks on SemaphoreSlim).
// Finds semaphores with no free permit and lists the async methods queued on them.
//
//   docker compose run --rm tools dotnet run /queries/clrmd-stuck-semaphores.cs -- /dumps/deadlock-semaphore.dmp

using Microsoft.Diagnostics.Runtime;

using var target = DataTarget.LoadDump(args[0]);
using var runtime = target.ClrVersions[0].CreateRuntime();

var stuck = runtime.Heap.EnumerateObjects()
    .Where(o => o.Type?.Name == "System.Threading.SemaphoreSlim")
    .Select(s => new { Semaphore = s, CurrentCount = s.ReadField<int>("m_currentCount"), Waiters = Waiters(s).ToList() })
    .Where(x => x.CurrentCount == 0 && x.Waiters.Count > 0);

foreach (var x in stuck)
{
    Console.WriteLine($"SemaphoreSlim {x.Semaphore.Address:x16}: currentCount=0, {x.Waiters.Count} async waiter(s)");

    // Each waiter is a SemaphoreSlim+TaskNode (a Task<bool>). Its continuation is
    // the async state machine that awaited WaitAsync() - i.e. WHO is waiting.
    foreach (var group in x.Waiters.GroupBy(w => w.ReadObjectField("m_continuationObject").Type?.Name ?? "?"))
        Console.WriteLine($"    {group.Count(),3} x {group.Key}");
}

// SemaphoreSlim keeps async waiters in a linked list: m_asyncHead -> Next -> ...
static IEnumerable<ClrObject> Waiters(ClrObject semaphore)
{
    for (var node = semaphore.ReadObjectField("m_asyncHead"); !node.IsNull; node = node.ReadObjectField("Next"))
        yield return node;
}
