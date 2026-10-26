#:package Microsoft.Diagnostics.Runtime@4.2.751201

// ClrMD query - Lab 1, scenario 2 (unbounded static cache).
// Plain LINQ over ClrMD 4.x: heap.EnumerateObjects() is an IEnumerable<ClrObject>.
//
//   docker compose run --rm tools dotnet run /queries/clrmd-cached-reports.cs -- /dumps/leak-cache-2.dmp

using Microsoft.Diagnostics.Runtime;

using var target = DataTarget.LoadDump(args[0]);
using var runtime = target.ClrVersions[0].CreateRuntime();
var heap = runtime.Heap;

// Records store their properties in compiler-generated "<Name>k__BackingField" fields.
var reports = heap.EnumerateObjects()
    .Where(o => o.Type?.Name == "MemoryLeak.Api.Scenarios.CustomerReport")
    .Select(o => new
    {
        o.Address,
        Customer = o.ReadStringField("<CustomerName>k__BackingField"),
        // AsString() truncates long strings (default 4096 chars) - read the length field instead.
        HtmlLength = o.ReadObjectField("<Html>k__BackingField").ReadField<int>("_stringLength"),
        Retained = o.Size + o.ReadObjectField("<Html>k__BackingField").Size,
    })
    .ToList();

Console.WriteLine($"CustomerReport instances: {reports.Count:N0}");
Console.WriteLine($"Bytes held by reports + their HTML strings: {reports.Sum(r => (long)r.Retained):N0}");
Console.WriteLine($"Distinct customers: {reports.Select(r => r.Customer).Distinct().Count():N0}  <- every entry is unique: the cache never hits");

foreach (var r in reports.Take(5))
    Console.WriteLine($"  {r.Address:x16} {r.Customer,-20} html={r.HtmlLength:N0} chars");

// Bonus: read the static field that roots them and count the dictionary entries.
var cacheType = heap.GetTypeByName("MemoryLeak.Api.Scenarios.ReportCache");
var dictionary = cacheType?.GetStaticFieldByName("_reports")?.ReadObject(runtime.AppDomains[0]);
if (dictionary is { IsNull: false } d)
    Console.WriteLine($"ReportCache._reports = {d.Address:x16} ({d.Type?.Name})");
