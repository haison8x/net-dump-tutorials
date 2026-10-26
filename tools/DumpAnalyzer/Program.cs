using DumpAnalyzer;
using Microsoft.Diagnostics.Runtime;

// DumpAnalyzer - automates the manual SOS workflows from the labs with ClrMD
// (Microsoft.Diagnostics.Runtime). See docs/09-automation-clrmd-dumplinq.md.
//
//   dotnet run --project tools/DumpAnalyzer -- stat  <dump> [top]
//   dotnet run --project tools/DumpAnalyzer -- diff  <before.dmp> <after.dmp> [top]
//   dotnet run --project tools/DumpAnalyzer -- roots <dump> <type-name-suffix> [samples]
//   dotnet run --project tools/DumpAnalyzer -- hang  <dump>
//
// Add `--dac <path-to-mscordaccore>` if ClrMD cannot find the matching DAC by
// itself (typically a Linux dump - see docs/08-linux-docker-k8s.md).

string? dacPath = null;
var dacIndex = Array.IndexOf(args, "--dac");
if (dacIndex >= 0 && dacIndex + 1 < args.Length)
{
    dacPath = args[dacIndex + 1];
    args = [.. args[..dacIndex], .. args[(dacIndex + 2)..]];
}

if (args.Length < 2)
{
    Console.WriteLine("""
        Usage:
          stat  <dump> [top]                       heap statistics by type (like dumpheap -stat)
          diff  <before.dmp> <after.dmp> [top]     which types grew between two dumps
          roots <dump> <type-name-suffix> [n]      root paths of n sample instances (includes static roots)
          hang  <dump>                             Monitor deadlocks, SemaphoreSlim waiters, blocked threads
        """);
    return 1;
}

var command = args[0];
var top = 15;

switch (command)
{
    case "stat":
    {
        if (args.Length > 2) top = int.Parse(args[2]);
        using var runtime = Open(args[1]);
        HeapStats.PrintTop(HeapStats.Collect(runtime.Heap), top);
        break;
    }
    case "diff":
    {
        if (args.Length > 3) top = int.Parse(args[3]);
        Dictionary<string, TypeStat> before, after;
        using (var r1 = Open(args[1])) before = HeapStats.Collect(r1.Heap);
        using (var r2 = Open(args[2])) after = HeapStats.Collect(r2.Heap);
        HeapStats.PrintDiff(before, after, top);
        break;
    }
    case "roots":
    {
        var samples = args.Length > 3 ? int.Parse(args[3]) : 1;
        using var runtime = Open(args[1]);
        RootFinder.Print(runtime, args[2], samples);
        break;
    }
    case "hang":
    {
        using var runtime = Open(args[1]);
        HangAnalyzer.Print(runtime);
        break;
    }
    default:
        Console.WriteLine($"Unknown command '{command}'.");
        return 1;
}

return 0;

ClrRuntime Open(string path)
{
    // The DataTarget owns the dump file; the runtime keeps it alive until disposed.
    var target = DataTarget.LoadDump(path);
    var clr = target.ClrVersions.FirstOrDefault()
        ?? throw new InvalidOperationException($"{path} does not contain a .NET runtime.");
    // ClrMD cannot always read the version from Linux dumps (reports 0.0.0.0).
    var version = clr.Version.Major == 0 ? "" : $"CLR {clr.Version}, ";
    Console.WriteLine($"# {Path.GetFileName(path)}: {version}{target.DataReader.TargetPlatform} {target.DataReader.Architecture}");

    // The DAC (mscordaccore / libmscordaccore.so) must match the runtime that produced the dump.
    if (dacPath is not null)
        return clr.CreateRuntime(dacPath, ignoreMismatch: true);

    try
    {
        return clr.CreateRuntime();
    }
    catch (FileNotFoundException) when (OperatingSystem.IsLinux())
    {
        // ClrMD could not identify/download the DAC (seen with Linux .NET 10 dumps).
        // Fall back to the DAC of the runtime we are running on - correct when this
        // tool runs on the same .NET version as the app (the `tools` container does).
        var localDac = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "libmscordaccore.so");
        Console.WriteLine($"# using local DAC {localDac}");
        return clr.CreateRuntime(localDac, ignoreMismatch: true);
    }
}
