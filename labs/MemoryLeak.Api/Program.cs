using MemoryLeak.Api.Scenarios;
using Microsoft.Extensions.Caching.Memory;

// MemoryLeak.Api - a deliberately leaky web API used by Lab 1 (docs/04-lab-memory-leak.md).
//
// Every scenario has a buggy default and a fixed variant (?fixed=true) so you can
// compare dumps "before" and "after" the fix.

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMemoryCache(options => options.SizeLimit = 1_000);

var app = builder.Build();

app.MapGet("/", () => Results.Text("""
    MemoryLeak.Api - Lab 1
      GET /leak/event?fixed=false   static event handler leak
      GET /leak/cache?fixed=false   unbounded static cache
      GET /leak/timer?fixed=false   undisposed System.Threading.Timer
      GET /leak/loh?fixed=false     Large Object Heap fragmentation
      GET /stats                    managed memory statistics
      GET /gc                       force a full blocking GC
    """));

app.MapGet("/leak/event", (bool? @fixed) => EventLeak.Handle(@fixed ?? false));
app.MapGet("/leak/cache", (bool? @fixed, IMemoryCache cache) => CacheLeak.Handle(@fixed ?? false, cache));
app.MapGet("/leak/timer", (bool? @fixed) => TimerLeak.Handle(@fixed ?? false));
app.MapGet("/leak/loh", (bool? @fixed) => LohFragmentation.Handle(@fixed ?? false));

app.MapGet("/stats", () =>
{
    var info = GC.GetGCMemoryInfo();
    return Results.Json(new
    {
        managedHeapMB = GC.GetTotalMemory(forceFullCollection: false) / 1024 / 1024,
        heapSizeMB = info.HeapSizeBytes / 1024 / 1024,
        fragmentedMB = info.FragmentedBytes / 1024 / 1024,
        gen0Collections = GC.CollectionCount(0),
        gen1Collections = GC.CollectionCount(1),
        gen2Collections = GC.CollectionCount(2),
        eventSubscribers = PriceTicker.SubscriberCount,
        cachedReports = ReportCache.Count,
        pollersCreated = TimerLeak.Created,
        retainedUploads = LohFragmentation.RetainedCount,
    });
});

// Forcing a GC proves that what is left on the heap is really *rooted* (leaked)
// and not just garbage that has not been collected yet.
app.MapGet("/gc", () =>
{
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    GC.WaitForPendingFinalizers();
    GC.Collect();
    return Results.Text($"Full GC done. Managed heap: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
});

app.Run();
