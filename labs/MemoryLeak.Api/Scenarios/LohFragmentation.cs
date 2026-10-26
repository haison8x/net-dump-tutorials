using System.Buffers;

namespace MemoryLeak.Api.Scenarios;

// Scenario 4 (bonus) - Large Object Heap (LOH) churn and fragmentation.
//
// Objects >= 85,000 bytes are allocated on the LOH, which is only collected
// during gen2 GCs and is not compacted by default. Allocating a fresh large
// buffer per request, and keeping some of them around, leaves "holes" (Free
// objects) between the survivors and makes the process's memory grow.
//
// What you will see: `eeheap -gc` shows a big LOH, `dumpheap -stat` shows lots of
// System.Byte[] plus large "Free" entries.

public static class LohFragmentation
{
    // Keeps every 2nd upload "for auditing" - the survivors leave holes in between them.
    private static readonly List<byte[]> _recentUploads = new();
    private static readonly Lock _sync = new();
    private static int _requests;

    public static int RetainedCount { get { lock (_sync) return _recentUploads.Count; } }

    public static IResult Handle(bool @fixed)
    {
        var size = 100_000 + Random.Shared.Next(0, 50_000);

        if (@fixed)
        {
            // FIX: rent from a pool instead of allocating a new large array every time,
            // and do not keep request buffers alive after the request.
            var pooled = ArrayPool<byte>.Shared.Rent(size);
            try
            {
                Process(pooled.AsSpan(0, size));
                return Results.Text($"processed {size} bytes (fixed, pooled)");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pooled);
            }
        }

        // BUG: a new LOH allocation per request + keeping half of them forever.
        var buffer = new byte[size];
        Process(buffer);
        lock (_sync)
        {
            if (++_requests % 2 == 0)
                _recentUploads.Add(buffer);
        }
        return Results.Text($"processed {size} bytes, retained={RetainedCount}");
    }

    private static void Process(Span<byte> data) => data.Fill(0x42);
}
