// Crash.Console - a console app that crashes in different ways, used by Lab 3
// (docs/06-lab-crash.md). The lab image sets the crash-dump environment variables,
// so the runtime writes a dump file at the moment of the crash:
//
//   docker compose run --rm crash unhandled      unhandled exception on a background thread
//   docker compose run --rm crash async-void     exception escaping an async void method
//   docker compose run --rm crash stackoverflow  infinite recursion (cannot be caught!)
//   docker compose run --rm crash native         access violation inside native code (P/Invoke)

using CrashLab;

var scenario = args.FirstOrDefault() ?? "";
Console.WriteLine($"Crash.Console PID {Environment.ProcessId} - scenario '{scenario}'");

switch (scenario)
{
    case "unhandled":
        var worker = new Thread(() => new OrderImporter().ImportBatch("orders-2026-10.csv"))
        {
            Name = "OrderImportWorker",
        };
        worker.Start();
        worker.Join();
        break;

    case "async-void":
        new NotificationSender().SendWelcomeEmail(customerId: 42);
        // The exception is re-thrown on a thread pool thread and kills the process.
        // (Wait forever: writing the crash dump takes a few seconds, during which this thread still runs.)
        Thread.Sleep(Timeout.Infinite);
        break;

    case "stackoverflow":
        var customer = new Customer("Alice");
        Console.WriteLine($"Display name: {customer.DisplayName}");
        break;

    case "native":
        Console.WriteLine($"Header length: {new PacketParser().HeaderLength(null)}");
        break;

    default:
        Console.WriteLine("Usage: dotnet run -- <unhandled|async-void|stackoverflow|native>");
        return 1;
}

Console.WriteLine("You should never see this line.");
return 0;
