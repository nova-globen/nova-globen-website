---
title: "Practical compare-and-choose guide for Vertical Slice (feature folders) vs DDD in .NET (ASP.NET Core / Minimal APIs / Controllers)"
date: 2025-09-25
slug: vertical-slice-features-vs-ddd
description: "Vertical Slice = organize by feature (Request → Handler → Persistence → Tests in one folder). Fast, low-ceremony, great for CRUD and independent modules.  DDD = organize by domain model (Aggregates, Value Objects, Domain Services; clear layers). Best when the bu"
tags: [architecture]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

# When to pick which

| Situation                                                                | Prefer                    |
| ------------------------------------------------------------------------ | ------------------------- |
| Small team, rapid delivery, endpoints mostly CRUD                        | **Vertical Slice**        |
| Complex invariants, rich business language, long-lived product           | **DDD (or hybrid)**       |
| Many independent endpoints with minimal cross-cutting rules              | **Vertical Slice**        |
| Multiple workflows touch the same core concepts; consistency is critical | **DDD (Aggregates, UoW)** |
| You expect frequent refactors of rules, not endpoints                    | **DDD**                   |
| You need easy local reasoning & test isolation per endpoint              | **Vertical Slice**        |

Rule of thumb: if an “entity” has **>3 invariants** or cross-slice rules, promote it to a DDD Aggregate.

## Typical folder layouts

### Vertical Slice (feature folders)

```
/Features
  /Orders
    /Create
      CreateOrderEndpoint.cs
      CreateOrderRequest.cs
      CreateOrderHandler.cs
      CreateOrderValidator.cs
      Mapping.cs
      CreateOrderTests.cs
    /GetById
      ...
```

* Handlers may use `DbContext` directly (simple) or a thin repository.

### DDD (Clean-ish)

```
/Domain
  /Orders
    Order.cs            // Aggregate Root
    OrderId.cs          // Value Object
    IOrderRepository.cs
    Events/OrderPlaced.cs

/Application
  /Orders
    CreateOrderCommand.cs
    CreateOrderHandler.cs
    Validators/...

/Infrastructure
  Persistence/AppDbContext.cs
  Repositories/OrderRepository.cs
  Configurations/OrderConfig.cs  // EF Core

/Api (or /Web)
  Endpoints/Orders/CreateOrderEndpoint.cs
```

# Minimal API + MediatR (Vertical Slice style)

```csharp
// Program.cs
app.MapPost("/orders", async (CreateOrderRequest req, ISender sender, CancellationToken ct)
    => Results.Ok(await sender.Send(new CreateOrderCommand(req), ct)));

// Features/Orders/Create/CreateOrderCommand.cs
public record CreateOrderRequest(string CustomerId, List<OrderLineDto> Lines);
public record CreateOrderCommand(CreateOrderRequest Request) : IRequest<Guid>;

// Features/Orders/Create/CreateOrderHandler.cs
public class CreateOrderHandler(AppDbContext db) : IRequestHandler<CreateOrderCommand, Guid>
{
    public async Task<Guid> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        // vertical-slice: simple rules inline
        var order = new OrderEntity { /* map from cmd.Request */ };
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return order.Id;
    }
}
```

Pros: tiny surface area, fast to build, tests per slice.
Cons: domain rules can leak across handlers if the domain grows.

# Same endpoint with a DDD core (hybrid)

```csharp
// Application/Orders/CreateOrderHandler.cs
public class CreateOrderHandler(IOrderRepository repo, IUnitOfWork uow)
    : IRequestHandler<CreateOrderCommand, OrderId>
{
    public async Task<OrderId> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        var order = Order.Create(
            new CustomerId(cmd.Request.CustomerId),
            cmd.Request.Lines.Select(l => new OrderLine(new Sku(l.Sku), l.Qty, Money.From(l.Price)))
        );
        await repo.AddAsync(order, ct);
        await uow.SaveChangesAsync(ct);
        return order.Id;
    }
}

// Domain/Orders/Order.cs
public class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderLine> _lines = new();
    public static Order Create(CustomerId customerId, IEnumerable<OrderLine> lines)
    {
        if (!lines.Any()) throw new DomainException("Order must have at least one line.");
        var order = new Order(customerId);
        foreach (var l in lines) order.AddLine(l);
        // enforce invariants here, not in the handler
        return order;
    }
}
```

Pros: invariants live in the **Domain**, reused safely across slices.
Cons: a bit more plumbing (repos, UoW, configs).

# Testing strategy

* **Vertical Slice**: test each handler as a unit (validator + handler), add one or two in-memory `DbContext` integration tests per slice.
* **DDD**: heavy **domain unit tests** around Aggregates/VOs; lighter app-layer tests; a handful of end-to-end tests per user journey.

# Cross-cutting details that often decide it

* **Transactions**:

  * Slice: ambient `DbContext` per handler is fine.
  * DDD: prefer UoW around a command; one Aggregate per transaction boundary.
* **Validation**:

  * Slice: FluentValidation per request.
  * DDD: lightweight request validation + **domain-level guards**.
* **Pipelines** (MediatR behaviors): logging, retries, outbox, validation → works with both.
* **EF Core**:

  * Slice: direct `DbContext` OK.
  * DDD: repositories + aggregate configurations; embrace `Owned` types for VOs.

# Practical recommendation for your stack (Blazor + Aspire)

1. **Start vertical-slice for delivery** (one folder per feature; endpoint + handler + validator + tests).
2. When a concept’s rules spread across 2+ slices, **promote it into the Domain** as an Aggregate + VOs, and switch the affected slices to use the domain API.
3. Keep **Contracts** (request/response DTOs) in a shared project for Blazor; map at the edge of each slice.
4. Add an **Outbox** + MediatR pipeline behavior if you publish integration events (Aspire comps make wiring infra easy).
5. Keep the **Domain** cohesive (not per-slice) to avoid duplicating ubiquitous language.

# Cheatsheet

* Mostly CRUD, simple rules → **Vertical Slice**.
* Rich rules, invariants, ubiquitous language → **DDD**.
* Unsure? **Hybrid**: slices for endpoints, DDD for complex cores.
