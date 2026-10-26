namespace Deadlock.Api.Scenarios;

// Scenario 1 - classic lock-ordering deadlock.
//
//   Thread A: /orders/ship       lock(orders)    -> ... -> lock(inventory)
//   Thread B: /inventory/restock lock(inventory) -> ... -> lock(orders)
//
// When both requests run at the same time, A owns `orders` and waits for
// `inventory`, while B owns `inventory` and waits for `orders`. Neither can
// continue - forever.
//
// We deliberately lock on plain `object` instances: `lock(object)` uses
// System.Threading.Monitor, whose owner is recorded in a *sync block* that the
// SOS `syncblk` command can show. (C# 13's System.Threading.Lock does NOT use
// sync blocks - see the lab text.)

public sealed class OrderService(IServiceProvider services)
{
    private readonly object _ordersLock = new();
    private readonly Dictionary<int, string> _orders = new();

    internal object SyncRoot => _ordersLock;

    public string ShipOrder(int orderId)
    {
        lock (_ordersLock)
        {
            _orders[orderId] = "shipping";
            Thread.Sleep(50); // simulate work while holding the lock - widens the race window

            // Needs the inventory lock while still holding the orders lock.
            services.GetRequiredService<InventoryService>().ReserveStock(orderId);

            _orders[orderId] = "shipped";
            return $"order {orderId} shipped";
        }
    }

    public void NotifyBackorders(int sku)
    {
        lock (_ordersLock)
        {
            foreach (var key in _orders.Keys.Where(k => k % 100 == sku).ToList())
                _orders[key] = "backorder-filled";
        }
    }
}

public sealed class InventoryService(IServiceProvider services)
{
    private readonly object _inventoryLock = new();
    private readonly Dictionary<int, int> _stock = new();

    public void ReserveStock(int orderId)
    {
        lock (_inventoryLock)
        {
            var sku = orderId % 100;
            _stock[sku] = _stock.GetValueOrDefault(sku) - 1;
        }
    }

    public string Restock(int sku, bool @fixed)
    {
        var orders = services.GetRequiredService<OrderService>();

        if (@fixed)
        {
            // FIX: every code path acquires the locks in the SAME global order
            // (orders -> inventory), so a cycle is impossible.
            lock (orders.SyncRoot)
            lock (_inventoryLock)
            {
                _stock[sku] = _stock.GetValueOrDefault(sku) + 10;
                Thread.Sleep(50);
                orders.NotifyBackorders(sku); // re-entering a Monitor we already own is fine
                return $"sku {sku} restocked (fixed)";
            }
        }

        // BUG: opposite lock order to OrderService.ShipOrder.
        lock (_inventoryLock)
        {
            _stock[sku] = _stock.GetValueOrDefault(sku) + 10;
            Thread.Sleep(50);

            // Needs the orders lock while still holding the inventory lock.
            orders.NotifyBackorders(sku);
            return $"sku {sku} restocked";
        }
    }
}
