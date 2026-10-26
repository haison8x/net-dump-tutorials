using Microsoft.Diagnostics.Runtime;

namespace DumpAnalyzer;

// Like SOS `gcroot`, but ClrMD also enumerates *static field* roots
// (ClrRootKind.StaticVar). On .NET 9+ statics are reported to the GC as their
// own root kind, and SOS `gcroot` can print "Found 0 unique roots" for objects
// that are only reachable from a static field. See docs/04-lab-memory-leak.md.
public static class RootFinder
{
    public static void Print(ClrRuntime runtime, string typeSuffix, int samples)
    {
        var heap = runtime.Heap;
        var instances = heap.EnumerateObjects()
            .Where(o => o.IsValid && o.Type?.Name is { } n && (n == typeSuffix || n.EndsWith("." + typeSuffix) || n.EndsWith("+" + typeSuffix)))
            .ToList();

        Console.WriteLine($"{instances.Count:N0} instance(s) of *{typeSuffix} on the heap (live + dead).");
        if (instances.Count == 0) return;

        // Pick samples spread over the list - the first objects are often warm-up leftovers.
        var picks = Enumerable.Range(0, Math.Min(samples, instances.Count))
            .Select(i => instances[(int)((i + 0.5) * instances.Count / Math.Min(samples, instances.Count))])
            .ToList();

        Dictionary<ulong, string>? staticNames = null;

        foreach (var target in picks)
        {
            Console.WriteLine();
            Console.WriteLine($"Root path for {target.Address:x16} {target.Type?.Name}:");

            var gcroot = new GCRoot(heap, [target.Address]);
            var found = false;
            foreach (var (root, path) in gcroot.EnumerateRootPaths())
            {
                found = true;
                Console.WriteLine($"  ROOT: {Describe(root)}");

                var links = new List<(ulong Address, string? Type)>();
                for (var link = path; link is not null; link = link.Next)
                    links.Add((link.Object, heap.GetObject(link.Object).Type?.Name));

                for (var i = 0; i < links.Count; i++)
                {
                    // Collapse long runs of the same type (linked lists such as TimerQueueTimer chains).
                    var run = 1;
                    while (i + run < links.Count && links[i + run].Type == links[i].Type) run++;
                    if (run > 3)
                    {
                        Console.WriteLine($"    -> {links[i].Address:x16} {links[i].Type}");
                        Console.WriteLine($"       ... {run - 2} more {links[i].Type} (linked list) ...");
                        i += run - 1;
                    }

                    var label = "";
                    if (root.RootKind == ClrRootKind.StaticVar)
                    {
                        staticNames ??= BuildStaticFieldMap(runtime);
                        if (staticNames.TryGetValue(links[i].Address, out var field)) label = $"   <-- static field {field}";
                    }
                    Console.WriteLine($"    -> {links[i].Address:x16} {links[i].Type}{label}");
                }
                break; // one path per object is enough
            }

            if (!found)
            {
                // Some DACs (e.g. Linux dumps) do not report static roots at all. Fall back to
                // walking from every static reference field - like SOS `pathto <static> <obj>`.
                staticNames ??= BuildStaticFieldMap(runtime);
                foreach (var (address, field) in staticNames.OrderBy(kv => IsFramework(kv.Value)))
                {
                    var link = gcroot.FindPathFrom(heap.GetObject(address));
                    if (link is null) continue;

                    found = true;
                    Console.WriteLine($"  ROOT: static field {field} (found by walking from static fields)");
                    for (; link is not null; link = link.Next)
                        Console.WriteLine($"    -> {link.Object:x16} {heap.GetObject(link.Object).Type?.Name}");
                    break;
                }
            }

            if (!found)
                Console.WriteLine("  No root found - the object is garbage that has not been collected yet.");
        }
    }

    private static string Describe(ClrRoot root) => root is ClrStackRoot s
        ? $"stack of thread {s.StackFrame?.Thread?.ManagedThreadId} in {s.StackFrame?.Method?.Signature ?? s.StackFrame?.FrameName}"
        : $"{root.RootKind} @ {root.Address:x16}";

    private static bool IsFramework(string name) => name.StartsWith("System.") || name.StartsWith("Microsoft.");

    // Maps "object address -> Namespace.Type.Field" for every initialized static
    // reference field, so we can name the static that roots a leak.
    private static Dictionary<ulong, string> BuildStaticFieldMap(ClrRuntime runtime)
    {
        var map = new Dictionary<ulong, string>();
        var domain = runtime.AppDomains.FirstOrDefault() ?? runtime.SharedDomain;
        if (domain is null) return map;

        foreach (var module in runtime.EnumerateModules())
        {
            foreach (var (mt, _) in module.EnumerateTypeDefToMethodTableMap())
            {
                if (mt == 0) continue;
                var type = runtime.Heap.GetTypeByMethodTable(mt);
                if (type is null) continue;
                foreach (var field in type.StaticFields)
                {
                    if (!field.IsObjectReference || !field.IsInitialized(domain)) continue;
                    var value = field.ReadObject(domain);
                    if (!value.IsNull) map.TryAdd(value.Address, $"{type.Name}.{field.Name}");
                }
            }
        }
        return map;
    }
}
