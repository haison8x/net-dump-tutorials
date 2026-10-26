// Framework.Console - a .NET Framework 4.8 app for the WinDbg appendix
// (docs/10-appendix-windbg.md). Windows only.
//
//   Framework.Console.exe deadlock   two threads take two locks in opposite order
//   Framework.Console.exe leak       a static cache that only grows
//
// The process stays alive so you can collect a dump (it is not listed by
// `dotnet-dump ps`, but `dotnet-dump collect -p <pid>` works on Windows):
//   procdump64 -ma <pid> dumps\win\framework-deadlock.dmp

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace FrameworkLab
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var scenario = args.Length > 0 ? args[0] : "";
            Console.WriteLine($"Framework.Console PID {Process.GetCurrentProcess().Id} - CLR {Environment.Version} - scenario '{scenario}'");

            switch (scenario)
            {
                case "deadlock":
                    var bank = new Bank();
                    new Thread(() => bank.Transfer(bank.Checking, bank.Savings, 100)) { Name = "TransferA", IsBackground = true }.Start();
                    new Thread(() => bank.Transfer(bank.Savings, bank.Checking, 50)) { Name = "TransferB", IsBackground = true }.Start();
                    break;

                case "leak":
                    for (var i = 0; i < 5_000; i++)
                        SessionCache.Remember(new UserSession(Guid.NewGuid()));
                    Console.WriteLine($"Sessions cached: {SessionCache.Count:N0}");
                    break;

                default:
                    Console.WriteLine("Usage: Framework.Console.exe <deadlock|leak>");
                    return 1;
            }

            Console.WriteLine("Running. Collect a dump now, then press Ctrl+C to exit.");
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }
    }

    // Scenario 1 - lock-ordering deadlock: Transfer locks `from` then `to`.
    // Thread A transfers Checking -> Savings, thread B Savings -> Checking.
    public sealed class Account
    {
        public Account(string name, decimal balance) { Name = name; Balance = balance; }
        public string Name { get; }
        public decimal Balance { get; set; }
    }

    public sealed class Bank
    {
        public Account Checking { get; } = new Account("Checking", 1_000);
        public Account Savings { get; } = new Account("Savings", 5_000);

        public void Transfer(Account from, Account to, decimal amount)
        {
            lock (from)
            {
                Thread.Sleep(200);          // widen the race window
                // BUG: lock order depends on the call -> two transfers in opposite directions deadlock.
                lock (to)
                {
                    from.Balance -= amount;
                    to.Balance += amount;
                }
            }
        }
    }

    // Scenario 2 - a static cache with no eviction.
    public sealed class UserSession
    {
        private readonly byte[] _state = new byte[8 * 1024];
        public UserSession(Guid id) { Id = id; _state[0] = 1; }
        public Guid Id { get; }
    }

    public static class SessionCache
    {
        // BUG: never evicted.
        private static readonly Dictionary<Guid, UserSession> _sessions = new Dictionary<Guid, UserSession>();
        public static int Count => _sessions.Count;
        public static void Remember(UserSession session) => _sessions[session.Id] = session;
    }
}
