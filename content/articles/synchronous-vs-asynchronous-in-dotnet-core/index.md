---
title: "Synchronous vs asynchronous in .NET core - how decide"
date: 2025-09-26
slug: synchronous-vs-asynchronous-in-dotnet-core
description: "The practical way to decide between synchronous and asynchronous in .NET Core, plus quick examples."
tags: [performance]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

# Rule of thumb
If your action **waits on something external**, make it **async**. If it’s **instant CPU**, keep it **sync**; for **expensive CPU**, **offload**.

# The core idea

* **Async shines for I/O-bound work** (DB calls, HTTP calls, queues, files). It frees the request thread while waiting, so **the server can serve more requests with the same thread pool**.
* **Sync is fine for trivial, short CPU work** (formatting, small calculations) where you’re not awaiting anything and the handler returns in a few milliseconds.

# When to choose async

* You call **EF Core** (`SaveChangesAsync`, `ToListAsync`), **HttpClient**, **Azure SDK** (`ServiceBusClient`, `BlobClient`), file I/O, or any API with `Async` methods.
* You expect **latency** from a dependency (tens–hundreds of ms).
* You need **cancellation** and **timeouts** (propagate `HttpContext.RequestAborted`).

# When sync is acceptable

* The action is **pure CPU and trivial** (e.g., quick math, mapping, input validation) and returns immediately.
* There are **no** I/O waits and no benefit from freeing the thread.

> If it’s **CPU-heavy** (image processing, big JSON transforms), do **not** just make it async—offload to a **background queue/worker** or a separate compute service. Async won’t make CPU faster.

# Pitfalls to avoid

* **Don’t block async**: never use `.Result` / `.Wait()` on Tasks (deadlocks/thread-pool starvation).
* **Async all the way down**: if the controller is async, downstream calls should be too.
* **Don’t fake async**: returning `Task.Run` around synchronous I/O just burns threads.
* **Keep concurrency bounded** when fanning out to multiple I/O calls.

# Mini decision checklist

1. **Any I/O?** → Use **async** (end-to-end).
2. **Pure CPU?**

   * **Tiny (≤ a few ms)** → Sync is fine.
   * **Heavy/variable** → Offload to background worker; controller returns 202/Location or uses a queue.

# ASP.NET Core examples

**Async (I/O-bound) — recommended**

```csharp
[ApiController]
[Route("orders")]
public class OrdersController : ControllerBase
{
    private readonly OrdersDbContext _db;
    private readonly HttpClient _http;

    public OrdersController(OrdersDbContext db, IHttpClientFactory f)
    {
        _db = db;
        _http = f.CreateClient("catalog");
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OrderDto>> Get(string id, CancellationToken ct)
    {
        var order = await _db.Orders.FindAsync([id], ct);
        if (order is null) return NotFound();

        // Call another service
        var resp = await _http.GetAsync($"/inventory/{order.Sku}", ct);
        resp.EnsureSuccessStatusCode();
        var inv = await resp.Content.ReadFromJsonAsync<InventoryDto>(cancellationToken: ct);

        return Ok(new OrderDto(order, inv));
    }
}
```

**Sync (trivial CPU) — acceptable**

```csharp
[HttpGet("ping")]
public ActionResult<string> Ping() => "pong"; // no I/O, returns immediately
```

**CPU-heavy work — offload rather than “asyncifying”**

```csharp
// Controller: accept request, enqueue, return 202
[HttpPost("render")]
public async Task<IActionResult> Render(RenderRequest req, [FromServices] IBackgroundQueue queue)
{
    var jobId = Guid.NewGuid().ToString("N");
    await queue.EnqueueAsync(jobId, req);
    return Accepted($"/render/{jobId}");
}

// Hosted service: single place that uses CPU, bounded concurrency
public class RenderWorker : BackgroundService
{
    private readonly IBackgroundQueue _q;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _q.DequeueAllAsync(stoppingToken))
        {
            // CPU-bound work here (e.g., image/video), controlled degree of parallelism
        }
    }
}
```

# Tuning & tips

* Pass `CancellationToken` from `HttpContext.RequestAborted`.
* Use `IHttpClientFactory` and async Azure SDKs.
* For streaming, expose `IAsyncEnumerable<T>` or chunked responses (async).
* In ASP.NET Core there’s **no UI SynchronizationContext**, so `ConfigureAwait(false)` is usually unnecessary.
