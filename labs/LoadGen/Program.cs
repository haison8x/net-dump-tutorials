using System.Diagnostics;

// LoadGen - a tiny HTTP load generator for the labs, so you don't need to install
// any third-party tool.
//
//   dotnet run --project labs/LoadGen -- [-c concurrency] [-d seconds] [-r requestsPerSecond] [-t timeoutSeconds] url [url ...]
//
// Each worker loops over the given URLs in turn (worker i starts at url i % n, so
// two URLs are hit concurrently). Prints one status line per second.
// -d 0 runs until Ctrl+C. -r caps the total request rate (0 = as fast as possible),
// which keeps the leak labs from eating all of your RAM in a few seconds.

var concurrency = 20;
var durationSeconds = 30;
var requestsPerSecond = 0;
var timeoutSeconds = 5;
var urls = new List<string>();

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-c": concurrency = int.Parse(args[++i]); break;
        case "-d": durationSeconds = int.Parse(args[++i]); break;
        case "-r": requestsPerSecond = int.Parse(args[++i]); break;
        case "-t": timeoutSeconds = int.Parse(args[++i]); break;
        default: urls.Add(args[i]); break;
    }
}

if (urls.Count == 0)
{
    Console.WriteLine("Usage: LoadGen [-c concurrency] [-d seconds] [-r requestsPerSecond] [-t timeoutSeconds] url [url ...]");
    return 1;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
if (durationSeconds > 0)
    cts.CancelAfter(TimeSpan.FromSeconds(durationSeconds));

using var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = int.MaxValue })
{
    Timeout = TimeSpan.FromSeconds(timeoutSeconds),
};

long ok = 0, failed = 0, timedOut = 0, inFlight = 0, totalLatencyMs = 0;

Console.WriteLine($"LoadGen: {concurrency} workers, {(durationSeconds > 0 ? durationSeconds + "s" : "until Ctrl+C")}, " +
    $"{(requestsPerSecond > 0 ? requestsPerSecond + " req/s max" : "no rate limit")}, timeout {timeoutSeconds}s");
foreach (var url in urls)
    Console.WriteLine($"  -> {url}");

// With a rate limit, each worker sends one request per `interval`.
var interval = requestsPerSecond > 0 ? TimeSpan.FromSeconds((double)concurrency / requestsPerSecond) : TimeSpan.Zero;

var workers = Enumerable.Range(0, concurrency).Select(async worker =>
{
    var index = worker % urls.Count;
    var nextSend = Stopwatch.GetTimestamp();
    while (!cts.IsCancellationRequested)
    {
        if (interval > TimeSpan.Zero)
        {
            var wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), nextSend);
            if (wait > TimeSpan.Zero)
            {
                try { await Task.Delay(wait, cts.Token); } catch (OperationCanceledException) { break; }
            }
            nextSend += (long)(interval.TotalSeconds * Stopwatch.Frequency);
        }

        var url = urls[index];
        index = (index + 1) % urls.Count;

        Interlocked.Increment(ref inFlight);
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await http.GetAsync(url, cts.Token);
            if (response.IsSuccessStatusCode) Interlocked.Increment(ref ok);
            else Interlocked.Increment(ref failed);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            break;
        }
        catch (TaskCanceledException)
        {
            Interlocked.Increment(ref timedOut); // HttpClient.Timeout elapsed
        }
        catch (HttpRequestException)
        {
            Interlocked.Increment(ref failed);
            await Task.Delay(100);
        }
        finally
        {
            Interlocked.Decrement(ref inFlight);
            Interlocked.Add(ref totalLatencyMs, sw.ElapsedMilliseconds);
        }
    }
}).ToArray();

var started = Stopwatch.StartNew();
long lastCompleted = 0;
while (!cts.IsCancellationRequested)
{
    try { await Task.Delay(1000, cts.Token); } catch (OperationCanceledException) { }

    var completed = Interlocked.Read(ref ok) + Interlocked.Read(ref failed) + Interlocked.Read(ref timedOut);
    var avg = completed == 0 ? 0 : Interlocked.Read(ref totalLatencyMs) / completed;
    Console.WriteLine(
        $"[{started.Elapsed:mm\\:ss}] ok={ok,7} failed={failed,5} timeout={timedOut,5} in-flight={inFlight,4} rps={completed - lastCompleted,5} avg={avg,5}ms");
    lastCompleted = completed;
}

await Task.WhenAll(workers);
Console.WriteLine("LoadGen finished.");
return 0;
