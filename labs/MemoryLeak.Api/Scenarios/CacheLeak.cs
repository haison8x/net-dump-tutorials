using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace MemoryLeak.Api.Scenarios;

// Scenario 2 - unbounded static cache.
//
// "Let's cache it, it's faster!" - a static dictionary with no size limit and no
// expiration, keyed by something with unbounded cardinality (request id, user
// input, timestamp...). It grows until the process runs out of memory.
//
// Root chain you will find with `gcroot`:
//   static ReportCache._reports -> ConcurrentDictionary -> VolatileNode[] -> Node -> CustomerReport

public sealed record CustomerReport(Guid Id, string CustomerName, DateTime GeneratedAt, string Html);

public static class ReportCache
{
    private static readonly ConcurrentDictionary<Guid, CustomerReport> _reports = new();

    public static int Count => _reports.Count;

    public static CustomerReport GetOrAdd(Guid id, Func<Guid, CustomerReport> factory)
        => _reports.GetOrAdd(id, factory);
}

public static class CacheLeak
{
    public static IResult Handle(bool @fixed, IMemoryCache cache)
    {
        // Every request asks for a "new" report id, so the cache never gets a hit.
        var reportId = Guid.NewGuid();

        if (@fixed)
        {
            // FIX: a bounded cache with a size limit + expiration (IMemoryCache with SizeLimit).
            var report = cache.GetOrCreate(reportId, entry =>
            {
                entry.Size = 1;
                entry.SlidingExpiration = TimeSpan.FromSeconds(30);
                return BuildReport(reportId);
            })!;
            return Results.Text($"report {report.Id} built (fixed)");
        }

        // BUG: static, unbounded, never evicted.
        var leaked = ReportCache.GetOrAdd(reportId, BuildReport);
        return Results.Text($"report {leaked.Id} built, cache size={ReportCache.Count}");
    }

    private static CustomerReport BuildReport(Guid id) =>
        new(id, $"Customer {id.ToString()[..8]}", DateTime.UtcNow,
            Html: $"<html><body><h1>Report {id}</h1>{new string('x', 10_000)}</body></html>");
}
