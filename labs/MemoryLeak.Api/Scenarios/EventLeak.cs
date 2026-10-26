namespace MemoryLeak.Api.Scenarios;

// Scenario 1 - static event handler leak.
//
// A static event keeps a strong reference to every subscriber's delegate, and the
// delegate keeps a strong reference to its target object. If a short-lived object
// subscribes to a long-lived (static) event and never unsubscribes, it can never
// be garbage collected.
//
// Root chain you will find with `gcroot`:
//   static PriceTicker.PriceChanged -> EventHandler<decimal> -> object[] -> EventHandler<decimal> -> PriceAlertSubscriber

public static class PriceTicker
{
    public static event EventHandler<decimal>? PriceChanged;

    public static int SubscriberCount => PriceChanged?.GetInvocationList().Length ?? 0;

    public static void Publish(decimal price) => PriceChanged?.Invoke(null, price);
}

public sealed class PriceAlertSubscriber : IDisposable
{
    // Simulates per-subscriber state (a buffer, a user session, a view model...).
    private readonly byte[] _history = new byte[16 * 1024];
    private readonly string _userId;
    private decimal _lastPrice;

    public PriceAlertSubscriber(string userId)
    {
        _userId = userId;
        PriceTicker.PriceChanged += OnPriceChanged;
    }

    private void OnPriceChanged(object? sender, decimal price)
    {
        _lastPrice = price;
        _history[0] = (byte)price;
    }

    public string UserId => _userId;
    public decimal LastPrice => _lastPrice;

    public void Dispose() => PriceTicker.PriceChanged -= OnPriceChanged;
}

public static class EventLeak
{
    public static IResult Handle(bool @fixed)
    {
        if (@fixed)
        {
            // FIX: unsubscribe when the subscriber's lifetime ends.
            using var subscriber = new PriceAlertSubscriber($"user-{Guid.NewGuid():N}");
            PriceTicker.Publish(Random.Shared.Next(100, 200));
            return Results.Text($"price={subscriber.LastPrice} (fixed) subscribers={PriceTicker.SubscriberCount}");
        }

        // BUG: the subscriber is never disposed, so it stays subscribed forever.
        var leaky = new PriceAlertSubscriber($"user-{Guid.NewGuid():N}");
        PriceTicker.Publish(Random.Shared.Next(100, 200));
        return Results.Text($"price={leaky.LastPrice} subscribers={PriceTicker.SubscriberCount}");
    }
}
