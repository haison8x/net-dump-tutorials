namespace MemoryLeak.Api.Scenarios;

// Scenario 3 - System.Threading.Timer that is never disposed.
//
// An active timer is referenced by the runtime's static TimerQueue. The timer
// callback is a delegate whose target is our ReportPoller, so the poller (and
// everything it references) stays alive for as long as the timer is scheduled.
// Without Dispose() a periodic timer is scheduled forever.
//
// Root chain you will find with `gcroot`:
//   TimerQueue (static) -> TimerQueueTimer -> TimerCallback -> ReportPoller -> byte[]

public sealed class ReportPoller : IDisposable
{
    private readonly byte[] _lastSnapshot = new byte[32 * 1024];
    private readonly Timer _timer;
    private int _polls;

    public ReportPoller()
    {
        _timer = new Timer(_ => Poll(), state: null, dueTime: TimeSpan.FromSeconds(1), period: TimeSpan.FromSeconds(1));
    }

    private void Poll()
    {
        _polls++;
        _lastSnapshot[_polls % _lastSnapshot.Length] = 1;
    }

    public void Dispose() => _timer.Dispose();
}

public static class TimerLeak
{
    private static int _created;
    public static int Created => _created;

    public static IResult Handle(bool @fixed)
    {
        Interlocked.Increment(ref _created);

        if (@fixed)
        {
            // FIX: dispose the timer (or the object that owns it) when done.
            using var poller = new ReportPoller();
            return Results.Text("poller created and disposed (fixed)");
        }

        // BUG: "fire and forget" poller - the timer keeps it alive forever.
        _ = new ReportPoller();
        return Results.Text($"poller created, total={_created}");
    }
}
