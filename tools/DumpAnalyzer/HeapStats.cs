using Microsoft.Diagnostics.Runtime;

namespace DumpAnalyzer;

public sealed record TypeStat(string Name, long Count, long TotalSize);

// The ClrMD equivalent of `dumpheap -stat`, plus a "diff" of two dumps -
// the single most useful thing to automate when hunting a leak.
public static class HeapStats
{
    public static Dictionary<string, TypeStat> Collect(ClrHeap heap)
    {
        var stats = new Dictionary<string, (long Count, long Size)>();
        foreach (var obj in heap.EnumerateObjects())
        {
            if (!obj.IsValid) continue;
            var name = obj.IsFree ? "Free" : obj.Type?.Name ?? "<unknown>";
            stats.TryGetValue(name, out var s);
            stats[name] = (s.Count + 1, s.Size + (long)obj.Size);
        }
        return stats.ToDictionary(kv => kv.Key, kv => new TypeStat(kv.Key, kv.Value.Count, kv.Value.Size));
    }

    public static void PrintTop(Dictionary<string, TypeStat> stats, int top)
    {
        Console.WriteLine($"{"Count",12} {"TotalSize",16}  Type");
        foreach (var s in stats.Values.OrderByDescending(s => s.TotalSize).Take(top))
            Console.WriteLine($"{s.Count,12:N0} {s.TotalSize,16:N0}  {s.Name}");
        Console.WriteLine($"Total: {stats.Values.Sum(s => s.Count):N0} objects, {stats.Values.Sum(s => s.TotalSize):N0} bytes");
    }

    public static void PrintDiff(Dictionary<string, TypeStat> before, Dictionary<string, TypeStat> after, int top)
    {
        var rows = after.Keys.Union(before.Keys)
            .Select(name =>
            {
                before.TryGetValue(name, out var b);
                after.TryGetValue(name, out var a);
                return (Name: name,
                        CountDelta: (a?.Count ?? 0) - (b?.Count ?? 0),
                        SizeDelta: (a?.TotalSize ?? 0) - (b?.TotalSize ?? 0),
                        Count: a?.Count ?? 0);
            })
            .Where(r => r.Name != "Free")
            .OrderByDescending(r => r.SizeDelta)
            .Take(top);

        Console.WriteLine($"{"+Count",12} {"+Bytes",16} {"Count now",12}  Type");
        foreach (var r in rows)
            Console.WriteLine($"{r.CountDelta,12:+#,0;-#,0;0} {r.SizeDelta,16:+#,0;-#,0;0} {r.Count,12:N0}  {r.Name}");
        Console.WriteLine();
        Console.WriteLine("Tip: types whose count grows by the same amount usually belong to the same leak -");
        Console.WriteLine("look for YOUR types among them, then run `roots` on one of them.");
    }
}
