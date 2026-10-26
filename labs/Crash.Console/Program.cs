// Crash.Console - a console app that crashes in different ways, used by Lab 3
// (docs/06-lab-crash.md). Run it with the crash-dump environment variables set
// (see scripts/run-with-crash-dumps.ps1 / .sh) and the runtime writes a dump
// file at the moment of the crash.
//
//   dotnet run -- unhandled      unhandled exception on a background thread
//   dotnet run -- async-void     exception escaping an async void method
//   dotnet run -- stackoverflow  infinite recursion (cannot be caught!)

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

    default:
        Console.WriteLine("Usage: dotnet run -- <unhandled|async-void|stackoverflow>");
        return 1;
}

Console.WriteLine("You should never see this line.");
return 0;

namespace CrashLab
{
    public sealed class OrderImporter
    {
        public void ImportBatch(string fileName)
        {
            string[] lines = ["1001;Widget;3", "1002;Gadget;-1", "1003;Doohickey;2"];
            foreach (var line in lines)
                ImportLine(fileName, line);
        }

        private static void ImportLine(string fileName, string line)
        {
            var parts = line.Split(';');
            var quantity = int.Parse(parts[2]);
            if (quantity <= 0)
            {
                // Nobody catches this on a raw Thread -> the runtime terminates the process.
                throw new InvalidOperationException(
                    $"Invalid quantity {quantity} for order {parts[0]} in {fileName}");
            }
        }
    }

    public sealed class NotificationSender
    {
        // BUG: async void - exceptions cannot be observed by the caller and are
        // re-thrown on the SynchronizationContext / thread pool, crashing the process.
        public async void SendWelcomeEmail(int customerId)
        {
            await Task.Delay(100);
            throw new TimeoutException($"SMTP server did not respond while emailing customer {customerId}");
        }
    }

    public sealed class Customer(string name)
    {
        public string Name { get; } = name;

        // BUG: the property calls itself instead of returning a field -> infinite recursion.
        public string DisplayName => string.IsNullOrEmpty(Name) ? "<unknown>" : DisplayName;
    }
}
