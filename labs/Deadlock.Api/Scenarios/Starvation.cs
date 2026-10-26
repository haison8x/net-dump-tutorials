using System.Runtime.CompilerServices;

namespace Deadlock.Api.Scenarios;

// Scenario 3 - sync-over-async thread pool starvation.
//
// GetQuoteBlocking() blocks a thread pool thread with .Result while the async
// work it waits for needs ANOTHER thread pool thread to run its continuation.
// Under load, every pool thread is blocked waiting, continuations pile up in the
// thread pool queue, and the pool only adds threads slowly (hill climbing). The
// whole app - including /health - stops responding, although CPU is ~0%.
//
// Not a "true" deadlock (it slowly recovers if load stops), but in production it
// looks exactly like one.

public static class Starvation
{
    // BUG: sync-over-async.
    // NoInlining only keeps this frame visible in the lab's stacks. In your own
    // Release builds, small methods like this are often inlined into their caller,
    // so in a real dump look one frame up as well (docs/05-lab-deadlock.md).
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string GetQuoteBlocking() => FetchQuoteFromDownstreamAsync().Result;

    // FIX: async all the way.
    public static Task<string> GetQuoteAsync() => FetchQuoteFromDownstreamAsync();

    private static async Task<string> FetchQuoteFromDownstreamAsync()
    {
        await Task.Delay(100); // pretend to call a downstream HTTP service
        return $"quote={Random.Shared.Next(100, 200)}";
    }
}
