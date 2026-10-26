using Deadlock.Api.Scenarios;

// Deadlock.Api - a web API full of hangs, used by Lab 2 (docs/05-lab-deadlock.md).
//
// Every scenario has a buggy default and a fixed variant (?fixed=true).

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<InventoryService>();
builder.Services.AddSingleton<OrderService>();
builder.Services.AddSingleton<ConfigService>();
builder.Services.AddSingleton<TokenService>();

var app = builder.Build();

app.MapGet("/", () => Results.Text("""
    Deadlock.Api - Lab 2
      GET /orders/ship                      lock-ordering deadlock (part 1, call together with /inventory/restock)
      GET /inventory/restock?fixed=false    lock-ordering deadlock (part 2)
      GET /config/refresh?fixed=false       async re-entrancy deadlock on SemaphoreSlim
      GET /token?fixed=false                SemaphoreSlim never released after an exception
      GET /starvation?fixed=false           sync-over-async thread pool starvation
      GET /health                           should always answer instantly
    """));

// Scenario 1 - classic lock-ordering deadlock (Monitor / `lock`).
// The fix lives in InventoryService.Restock, so only /inventory/restock takes ?fixed=true.
app.MapGet("/orders/ship", (OrderService orders) =>
    Results.Text(orders.ShipOrder(Random.Shared.Next(1, 1000))));
app.MapGet("/inventory/restock", (bool? @fixed, InventoryService inventory) =>
    Results.Text(inventory.Restock(Random.Shared.Next(1, 100), @fixed ?? false)));

// Scenario 2 - async deadlocks on SemaphoreSlim (no thread is blocked!).
app.MapGet("/config/refresh", async (bool? @fixed, ConfigService config) =>
    Results.Text(await config.RefreshAsync(@fixed ?? false)));
app.MapGet("/token", async (bool? @fixed, TokenService tokens) =>
{
    try
    {
        return Results.Text(await tokens.GetTokenAsync(@fixed ?? false));
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(ex.Message, statusCode: 502);
    }
});

// Scenario 3 - sync-over-async thread pool starvation.
app.MapGet("/starvation", async (bool? @fixed) =>
    Results.Text(@fixed == true
        ? await Starvation.GetQuoteAsync()
        : Starvation.GetQuoteBlocking()));

app.MapGet("/health", () => Results.Text(
    $"OK threads={ThreadPool.ThreadCount} queued={ThreadPool.PendingWorkItemCount}"));

app.Run();
