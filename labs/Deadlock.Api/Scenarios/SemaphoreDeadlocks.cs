namespace Deadlock.Api.Scenarios;

// Scenario 2 - async "deadlocks" on SemaphoreSlim.
//
// SemaphoreSlim.WaitAsync() does not block a thread: the waiting request is just
// a suspended async state machine sitting in the semaphore's waiter list. So
// `clrstack -all` and `syncblk` show NOTHING useful. You need `dumpasync` and
// `dumpobj` on the SemaphoreSlim instance instead.

// 2a - re-entrancy. SemaphoreSlim is not re-entrant (unlike Monitor): an async
// method that holds it and then calls another method that awaits the same
// semaphore waits for itself forever.
public sealed class ConfigService
{
    private readonly SemaphoreSlim _configGate = new(1, 1);
    private readonly Dictionary<string, string> _settings = new() { ["FeatureX"] = "on" };

    public async Task<string> RefreshAsync(bool @fixed)
    {
        await _configGate.WaitAsync();
        try
        {
            await Task.Delay(10); // pretend to reload from a remote config store

            // BUG: GetSettingAsync takes _configGate again -> waits for ourselves.
            // FIX: call the lock-free core method while we already hold the gate.
            var featureX = @fixed
                ? GetSettingCore("FeatureX")
                : await GetSettingAsync("FeatureX");

            return $"config refreshed, FeatureX={featureX}";
        }
        finally
        {
            _configGate.Release();
        }
    }

    public async Task<string> GetSettingAsync(string key)
    {
        await _configGate.WaitAsync();
        try
        {
            return GetSettingCore(key);
        }
        finally
        {
            _configGate.Release();
        }
    }

    private string GetSettingCore(string key) => _settings.GetValueOrDefault(key, "<missing>");
}

// 2b - the "orphaned" semaphore. An exception is thrown between WaitAsync() and
// Release() and there is no try/finally, so the permit is never given back.
// From then on every request queues up behind a semaphore nobody owns.
public sealed class TokenService
{
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private int _calls;
    private string? _token;

    public async Task<string> GetTokenAsync(bool @fixed)
    {
        await _tokenGate.WaitAsync();

        if (@fixed)
        {
            // FIX: always release in a finally block.
            try
            {
                return await RenewTokenAsync();
            }
            finally
            {
                _tokenGate.Release();
            }
        }

        // BUG: if RenewTokenAsync throws, Release() is skipped.
        var token = await RenewTokenAsync();
        _tokenGate.Release();
        return token;
    }

    private async Task<string> RenewTokenAsync()
    {
        await Task.Delay(20); // pretend to call an identity provider

        // Every 5th call the "identity provider" fails.
        if (Interlocked.Increment(ref _calls) % 5 == 0)
            throw new HttpRequestException("Identity provider returned 503 Service Unavailable");

        _token = $"token-{Guid.NewGuid():N}";
        return _token;
    }
}
