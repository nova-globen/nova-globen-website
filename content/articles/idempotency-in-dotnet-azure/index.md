---
title: "A practical, “ship-it” guide to idempotency keys, request hashing, persistence, PUT semantics, and de-dup in handlers/queues on .NET + Azure"
date: 2025-09-25
slug: idempotency-in-dotnet-azure
description: "Idempotency = same request run N times ⇒ same effect & same response.  Keys & hashing = client or server chooses a stable key (e.g., Idempotency-Key header) and/or a hash of the normalized request to detect duplicates.  Persistence = store the first request’s"
tags: [apis, reliability]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

# API layer pattern (HTTP)

## 1) Contract

* Require `Idempotency-Key` on **POST** that creates resources (optional on GET, safe methods).
* For **PUT**/**PATCH**, prefer natural keys (resource URI) + concurrency (ETag) over custom keys.
* Return `Idempotency-Replayed: true` on replays.

## 2) What to persist

* `(TenantId, IdempotencyKey)` — **unique**.
* Request **fingerprint** (stable hash of method + path + canonicalized body).
* First **status code**, **headers** (whitelisted), **response body** (or a pointer), timestamps, and a short **TTL**.

### SQL table (Azure SQL) example

```sql
CREATE TABLE Idempotency (
  TenantId        NVARCHAR(64) NOT NULL,
  IdempotencyKey  NVARCHAR(128) NOT NULL,
  RequestHash     VARBINARY(32) NOT NULL, -- SHA-256
  StatusCode      INT           NULL,
  ResponseBody    VARBINARY(MAX) NULL,    -- or NVARCHAR(MAX) if JSON
  CreatedAtUtc    DATETIME2(3)  NOT NULL  DEFAULT SYSUTCDATETIME(),
  CompletedAtUtc  DATETIME2(3)  NULL,
  CONSTRAINT PK_Idem PRIMARY KEY (TenantId, IdempotencyKey)
);
CREATE INDEX IX_Idem_Created ON Idempotency (CreatedAtUtc);
```

*(Cosmos DB works too: define a unique key on `/tenantId/idempotencyKey` and set TTL on the container.)*

## 3) Minimal middleware/filter in ASP.NET Core

```csharp
public class IdempotencyMiddleware : IMiddleware
{
    private readonly IIdemStore _store; // wraps Azure SQL or Cosmos
    public IdempotencyMiddleware(IIdemStore store) => _store = store;

    public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
    {
        var tenantId = ctx.User.FindFirst("tenant_id")?.Value ?? "public";
        var key = ctx.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key)) { await next(ctx); return; }

        var hash = await HashRequestAsync(ctx.Request); // method+path+normalized body
        var existing = await _store.TryGetAsync(tenantId, key);

        if (existing is { Completed: true } && existing.RequestHash.SequenceEqual(hash))
        {
            ctx.Response.Headers["Idempotency-Replayed"] = "true";
            ctx.Response.StatusCode = existing.StatusCode;
            if (existing.ResponseBody is { Length: > 0 })
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.Body.WriteAsync(existing.ResponseBody);
            }
            return;
        }

        // Reserve the key up-front (prevents thundering herds)
        var reserved = await _store.ReserveAsync(tenantId, key, hash); // insert if not exists
        if (!reserved) // concurrent duplicate while first is in-flight
        {
            // Poll or short 409 telling client to retry shortly; or wait on a small backoff and re-check
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            await ctx.Response.WriteAsync("{\"error\":\"Request in progress\"}");
            return;
        }

        // Capture response
        var originalBody = ctx.Response.Body;
        await using var mem = new MemoryStream();
        ctx.Response.Body = mem;

        try
        {
            await next(ctx);
            await _store.CompleteAsync(tenantId, key, ctx.Response.StatusCode, mem.ToArray());
        }
        finally
        {
            mem.Position = 0;
            await mem.CopyToAsync(originalBody);
            ctx.Response.Body = originalBody;
        }
    }

    static async Task<byte[]> HashRequestAsync(HttpRequest req)
    {
        req.EnableBuffering();
        using var sha = System.Security.Cryptography.SHA256.Create();
        var body = "";
        if (req.ContentLength > 0)
        {
            using var reader = new StreamReader(req.Body, leaveOpen:true);
            body = await reader.ReadToEndAsync();
            req.Body.Position = 0;
        }
        var canonical = $"{req.Method}\n{req.Path}\n{NormalizeJson(body)}";
        return sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonical));
    }

    static string NormalizeJson(string json) => string.IsNullOrWhiteSpace(json)
        ? ""
        : System.Text.Json.JsonDocument.Parse(json).RootElement.GetRawText(); // canonical spacing/order for objects mostly stable in client contracts
}
```

**Notes**

* `ReserveAsync` does an **INSERT** guarded by the unique key; if it fails, another request holds the key.
* Set a **TTL**/cleanup job (e.g., 24–72h) to trim completed entries.

---

# PUT semantics with ETags (Concurrency)

* PUT should replace the full resource representation (idempotent by nature).
* Use **ETag** + `If-Match` to prevent lost updates.

### Cosmos DB / Azure Table / Azure SQL

* Cosmos & Tables expose ETags natively; for SQL, emulate with a `rowversion` column.

**Controller snippet**

```csharp
[HttpPut("{id}")]
public async Task<IActionResult> Put(string id, [FromBody] Widget dto, [FromHeader(Name="If-Match")] string etag)
{
    // Load current, compare ETag/rowversion; if mismatch => 412 Precondition Failed
    var ok = await _repo.ReplaceAsync(id, dto, etag);
    return ok ? NoContent() : StatusCode(StatusCodes.Status412PreconditionFailed);
}
```

---

# Messaging/Queue consumers (de-dup & exactly-once illusions)

## Azure Service Bus (recommended over Storage Queues for de-dup)

* Enable **Duplicate Detection** on the queue/topic (`DuplicateDetectionHistoryTimeWindow`, e.g., 10 minutes–7 days).
* Set `MessageId` to a **business idempotency key** (e.g., the order ID or the HTTP Idempotency-Key).
* Still make the handler idempotent (at-least-once delivery can still surface via re-enqueue/abandon).

**Producer**

```csharp
var client = new ServiceBusClient(connStr);
var sender = client.CreateSender("orders");
var msg = new ServiceBusMessage(BinaryData.FromObjectAsJson(order))
{
    MessageId = order.Id, // business key
    Subject = "OrderCreated"
};
await sender.SendMessageAsync(msg);
```

**Function/Worker handler (inbox pattern)**

```csharp
public class OrderHandler
{
    private readonly IInboxStore _inbox; // Azure SQL/Cosmos unique on (MessageId)

    public async Task HandleAsync(ServiceBusReceivedMessage m, CancellationToken ct)
    {
        // Fast de-dup check
        if (!await _inbox.ReserveAsync(m.MessageId)) return; // already processed

        try
        {
            var order = m.Body.ToObjectFromJson<OrderCreated>();
            await ProcessAsync(order, ct); // make this idempotent
            await _inbox.CompleteAsync(m.MessageId);
        }
        catch
        {
            await _inbox.ReleaseAsync(m.MessageId); // allow retry
            throw;
        }
    }
}
```

## Azure Storage Queues / Event Hubs

* No built-in de-dup for Storage Queues; **always** use an **inbox table** keyed by a message/business id.
* Event Hubs is stream-oriented; treat processing as idempotent, or checkpoint and maintain an inbox keyed by event idempotency key.

## Outbox (publish exactly-once to the bus)

* In the **same DB transaction** as state change, write **Outbox** rows (events to publish).
* A background dispatcher reads un-sent rows and publishes to Service Bus; upon success, marks them sent.
* Libraries: NServiceBus/MassTransit have outbox patterns built-in; or roll your own with EF Core.

**EF Core Outbox table**

```sql
CREATE TABLE Outbox (
  Id UNIQUEIDENTIFIER PRIMARY KEY,
  OccurredUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
  Type NVARCHAR(200) NOT NULL,
  Payload NVARCHAR(MAX) NOT NULL,
  Sent BIT NOT NULL DEFAULT 0
);
```

---

# Hashing strategy (practical)

* Canonicalize: `METHOD + PATH + canonical(JSON body) + critical headers (e.g., Content-Type, Version) + TenantId`.
* Use **SHA-256**; store 32-byte hash.
* Hash matters for **replay safety**: if key reused with **different** payload, reject with `422 Unprocessable Entity` (or `409 Conflict`) to avoid accidental cross-use.

---

# Where to put what (Azure choices)

* **Idempotency store**:

  * **Cosmos DB**: low latency, unique key on `(tenantId, idemKey)`, TTL per item.
  * **Azure SQL**: great if you already use SQL; unique composite key, `rowversion` for concurrency.
  * **Redis**: fast reservation locks via `SETNX` + TTL—but still persist the *final response* in durable storage.
* **Queue**: **Azure Service Bus** with duplicate detection + sessions if you need ordered processing per aggregate.

---

# Error handling & UX

* If a duplicate arrives **before** the first completes: return **409** (“in progress”) or **202** with a **status URL** the client can poll.
* If a duplicate arrives **after** completion with the **same hash**: return the original status/body with `Idempotency-Replayed: true`.
* If **same key, different hash**: **422/409** to force client to pick a new key.

---

# Observability

* Log the idempotency key, request hash, and replay status.
* Add metrics: “idem\_hits”, “idem\_inflight”, “idem\_conflicts”, and “handler\_dedup\_hits”.

---

# Summary: Implementation Checklist (.NET + Azure)

1. **HTTP**: require `Idempotency-Key` on POST creates; middleware reserves key → executes → stores response → replays on duplicates.
2. **PUT**: design as full replace + **ETag** (`If-Match`) to make retries safe and prevent lost updates.
3. **Queues**: use **Service Bus** with `DuplicateDetection` + `MessageId`; consumers maintain an **inbox** table and business-idempotent handlers.
4. **Outbox**: publish messages from a DB outbox to get “exactly-once” effect across boundaries.
5. **Persistence**: Cosmos (unique key + TTL) or SQL (unique index).
6. **Hashing**: canonical SHA-256 over method/path/body/tenant; reject key reuse with different payloads.
