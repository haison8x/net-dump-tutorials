#:package DumpLinq@0.1.3

// DumpLinq query - Lab 1, scenario 1 (static event handler leak).
// A .NET 10 file-based app: no project file needed.
//
// CAVEAT: DumpLinq 0.1.3 is built on ClrMD 3.1, which does not see every object
// in some .NET 10 dumps (we measured 24,044 vs 27,450 objects on a Linux dump).
// Cross-check important numbers with the ClrMD 4.x queries (clrmd-*.cs) or
// with `dumpheap -stat`. See docs/09-automation-clrmd-dumplinq.md.
//
//   docker compose run --rm tools dotnet run /queries/dumplinq-leaked-subscribers.cs -- /dumps/leak-event-2.dmp

using DumpLinq;

using var dump = Dump.Open(args[0]);

var subscribers = dump.EnumerateHeapObjects("*.PriceAlertSubscriber").ToList();
Console.WriteLine($"PriceAlertSubscriber instances: {subscribers.Count:N0}");

// Field values are DumpObjects too - navigate them like a normal object graph.
foreach (var s in subscribers.Take(3))
    Console.WriteLine($"  {s.Address:x16} user={s.GetField("_userId").AsString().Value}");

// How many subscribers does the static event currently hold?
// (The multicast delegate keeps its targets in _invocationList and the count in _invocationCount.)
var ticker = dump.EnumerateHeapObjects("System.EventHandler<System.Decimal>")
    .Select(d => (Delegate: d, Count: d.GetField("_invocationCount").ReadAs<nint>()))
    .Where(x => !x.Count.IsError)
    .MaxBy(x => x.Count.Value);
Console.WriteLine($"Largest EventHandler<decimal>: {ticker.Delegate.Address:x16} with {ticker.Count.Value:N0} subscribers");

Console.WriteLine();
Console.WriteLine("One subscriber, as a tree:");
Console.WriteLine(subscribers[^1].ToString(depth: 2, arrayItems: 3));
